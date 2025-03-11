using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Threading; // Add explicit import for System.Threading.Timer

namespace AudioDual.Core
{
    public class AudioDeviceOutput : IDisposable
    {
        private readonly IWavePlayer _wavePlayer;
        private readonly BufferedWaveProvider _waveProvider;
        private readonly SampleChannel _sampleChannel;
        private float _volume;
        private WaveFormat _sourceFormat;
        private IWaveProvider? _resampler;
        private readonly int _bufferMilliseconds;
        private readonly Stopwatch _bufferWatch;
        private int _underflowCount;
        private int _overflowCount;
        private readonly ConcurrentQueue<byte[]> _audioQueue;
        private readonly System.Threading.Timer _playbackTimer; // Use fully qualified name
        private readonly int _processIntervalMs;
        private readonly byte[] _silenceBuffer;

        public float Volume => _volume;

        public AudioDeviceOutput(MMDevice device, float initialVolume = 1.0f, 
            WaveFormat? sourceFormat = null, int bufferMilliseconds = 500)
        {
            _volume = initialVolume;
            _bufferMilliseconds = bufferMilliseconds;
            _bufferWatch = new Stopwatch();
            _bufferWatch.Start();
            _audioQueue = new ConcurrentQueue<byte[]>();
            _processIntervalMs = 10; // Process audio every 10ms
            
            // Use device's preferred format or a sensible default
            var deviceFormat = sourceFormat ?? new WaveFormat(48000, 16, 2);
            _sourceFormat = deviceFormat;
            
            // Create output for this device with a larger buffer to prevent stuttering
            _waveProvider = new BufferedWaveProvider(deviceFormat) {
                DiscardOnBufferOverflow = true
            };
            
            // Calculate buffer size based on desired latency - make it larger than needed to avoid stuttering
            int bufferSize = deviceFormat.AverageBytesPerSecond * _bufferMilliseconds / 1000;
            _waveProvider.BufferLength = bufferSize * 2; // Double the buffer size for stability
            
            // Create a silence buffer for filling gaps
            _silenceBuffer = new byte[deviceFormat.AverageBytesPerSecond / 20]; // 50ms of silence
            
            // Set up sample channel for volume control
            _sampleChannel = new SampleChannel(_waveProvider);
            _sampleChannel.Volume = _volume;

            try
            {
                // Always use shared mode for better compatibility
                _wavePlayer = new WasapiOut(
                    device,
                    AudioClientShareMode.Shared,
                    true, // Use event-driven model for more responsive playback
                    _bufferMilliseconds);
                
                // Check if we need to convert the format
                var devicePreferredFormat = _wavePlayer.OutputWaveFormat;
                
                if (!devicePreferredFormat.Equals(_sourceFormat))
                {
                    Console.WriteLine($"Format conversion needed: Source={_sourceFormat}, Target={devicePreferredFormat}");
                    
                    // Use high-quality resampling
                    _resampler = new MediaFoundationResampler(
                        new SampleToWaveProvider(_sampleChannel),
                        devicePreferredFormat)
                    {
                        ResamplerQuality = 60 // High quality
                    };
                    
                    _wavePlayer.Init(_resampler);
                }
                else
                {
                    _wavePlayer.Init(_sampleChannel);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing WASAPI output: {ex.Message}");
                
                // Fallback with even more conservative settings
                _wavePlayer = new WasapiOut(device, AudioClientShareMode.Shared, false, 750);
                _wavePlayer.Init(_sampleChannel);
            }

            // Start a timer to process audio at regular intervals
            _playbackTimer = new System.Threading.Timer(ProcessAudioQueue, null, 0, _processIntervalMs);
            
            _wavePlayer.Play();
        }

        // Queue the audio data instead of sending it directly
        public void ProcessAudioData(byte[] buffer, int bytesRecorded)
        {
            if (bytesRecorded <= 0) return;
            
            // Make a copy of the buffer to avoid issues with reused buffers
            byte[] bufferCopy = new byte[bytesRecorded];
            Buffer.BlockCopy(buffer, 0, bufferCopy, 0, bytesRecorded);
            
            // Add to queue for processing at consistent intervals
            _audioQueue.Enqueue(bufferCopy);
        }

        private void ProcessAudioQueue(object? state)
        {
            // Check buffer health
            MonitorBufferHealth();
            
            // Number of items to process in one batch
            int itemsToProcess = Math.Min(_audioQueue.Count, 5);
            int bytesAdded = 0;
            
            // Process multiple items at once to reduce overhead
            for (int i = 0; i < itemsToProcess; i++)
            {
                if (_audioQueue.TryDequeue(out byte[]? buffer) && buffer != null)
                {
                    _waveProvider.AddSamples(buffer, 0, buffer.Length);
                    bytesAdded += buffer.Length;
                }
            }
            
            // If buffer is getting low, add some silence to prevent stuttering
            if (_waveProvider.BufferedBytes < _sourceFormat.AverageBytesPerSecond / 10 && bytesAdded == 0)
            {
                _waveProvider.AddSamples(_silenceBuffer, 0, _silenceBuffer.Length);
            }
        }

        private void MonitorBufferHealth()
        {
            // Monitor buffer health every second
            if (_bufferWatch.ElapsedMilliseconds >= 1000)
            {
                double bufferedSeconds = (double)_waveProvider.BufferedBytes / _sourceFormat.AverageBytesPerSecond;
                double optimalBuffer = _bufferMilliseconds / 1000.0 * 0.5; // 50% of desired buffer
                
                // Check for buffer underrun (too little data)
                if (bufferedSeconds < 0.05 && _waveProvider.BufferedBytes > 0)
                {
                    _underflowCount++;
                    if (_underflowCount % 5 == 0)
                    {
                        Console.WriteLine($"Audio buffer underflow detected: {bufferedSeconds:F3}s buffered");
                        
                        // Add some silence to prevent stuttering on underflow
                        _waveProvider.AddSamples(_silenceBuffer, 0, _silenceBuffer.Length);
                    }
                }
                // Check for buffer overflow (too much data)
                else if (bufferedSeconds > (_bufferMilliseconds * 2 / 1000.0))
                {
                    _overflowCount++;
                    if (_overflowCount % 5 == 0)
                    {
                        Console.WriteLine($"Audio buffer overflow detected: {bufferedSeconds:F3}s buffered");
                    }
                }
                
                _bufferWatch.Restart();
            }
        }

        public void SetVolume(float volume)
        {
            _volume = Math.Clamp(volume, 0.0f, 1.0f);
            _sampleChannel.Volume = _volume;
        }

        public void Dispose()
        {
            try
            {
                _playbackTimer?.Dispose();
                
                _wavePlayer?.Stop();
                _wavePlayer?.Dispose();
                
                // The MediaFoundationResampler implements IDisposable
                if (_resampler is IDisposable disposableResampler)
                {
                    disposableResampler.Dispose();
                }
                
                // Clear any remaining audio data
                while (_audioQueue.TryDequeue(out _)) { }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error disposing AudioDeviceOutput: {ex.Message}");
            }
        }
    }
}
