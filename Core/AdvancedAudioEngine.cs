using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AudioDual.Core
{
    public class AdvancedAudioEngine : IDisposable
    {
        private readonly MMDeviceEnumerator _deviceEnumerator;
        private readonly ConcurrentDictionary<string, AdvancedAudioOutput> _activeOutputs;
        private WasapiCapture? _loopbackCapture;
        private WaveFormat _captureFormat;
        private readonly VirtualAudioDevice _virtualDevice;
        private readonly ConcurrentQueue<(byte[] Buffer, int BytesRecorded)> _audioQueue;
        private bool _isRunning;
        private Task? _processingTask;
        private readonly CancellationTokenSource _cancellation;
        
        public event EventHandler<AudioDataEventArgs>? AudioDataAvailable;
        
        public AdvancedAudioEngine()
        {
            // Subscribe to unhandled exceptions
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                try
                {
                    var exception = args.ExceptionObject as Exception;
                    string message = "Unhandled exception: " + (exception?.ToString() ?? "Unknown error");
                    Console.WriteLine(message);
                    
                    // Log to file
                    string logPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "AudioDual", 
                        "error_log.txt");
                        
                    Directory.CreateDirectory(Path.GetDirectoryName(logPath));
                    File.AppendAllText(logPath, $"[{DateTime.Now}] {message}\n");
                    
                    // If fatal, try to clean up before termination
                    if (args.IsTerminating)
                    {
                        StopAudioCapture();
                        foreach (var output in _activeOutputs.Values)
                        {
                            try { output.Dispose(); } catch { }
                        }
                    }
                }
                catch { /* Last resort handler should never throw */ }
            };
            
            _deviceEnumerator = new MMDeviceEnumerator();
            _activeOutputs = new ConcurrentDictionary<string, AdvancedAudioOutput>();
            _captureFormat = new WaveFormat(48000, 16, 2);
            _virtualDevice = new VirtualAudioDevice();
            _audioQueue = new ConcurrentQueue<(byte[] Buffer, int BytesRecorded)>();
            _cancellation = new CancellationTokenSource();
            
            // Start the processing task with higher priority
            _isRunning = true;
            _processingTask = Task.Factory.StartNew(
                ProcessAudioQueueAsync, 
                _cancellation.Token,
                TaskCreationOptions.LongRunning | TaskCreationOptions.PreferFairness, 
                TaskScheduler.Default);
            
            // Try to set thread priority, using proper Thread.Priority property instead of SetThreadPriority method
            if (_processingTask.Status == TaskStatus.Running)
            {
                try {
                    var threadFieldInfo = typeof(Task).GetField("m_thread", 
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (threadFieldInfo != null)
                    {
                        var thread = threadFieldInfo.GetValue(_processingTask) as Thread;
                        if (thread != null)
                        {
                            thread.Priority = ThreadPriority.Highest;
                        }
                    }
                }
                catch { /* Ignore if we can't set thread priority */ }
            }
        }
        
        public List<AudioDevice> GetAudioDevices()
        {
            var devices = new List<AudioDevice>();
            string defaultDeviceId = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID;
            
            foreach (var device in _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                devices.Add(new AudioDevice
                {
                    Id = device.ID,
                    Name = device.FriendlyName,
                    IsDefault = device.ID == defaultDeviceId,
                    IsEnabled = _activeOutputs.ContainsKey(device.ID),
                    Volume = _activeOutputs.TryGetValue(device.ID, out var output) ? output.Volume : 1.0f
                });
            }
            
            return devices;
        }
        
        public bool EnableDevice(string deviceId, float volume = 1.0f)
        {
            try
            {
                var device = _deviceEnumerator.GetDevice(deviceId);
                if (device == null) return false;
                
                // Check if device is already enabled
                if (_activeOutputs.ContainsKey(deviceId))
                {
                    _activeOutputs[deviceId].SetVolume(volume);
                    return true;
                }
                
                // Start audio capture if this is the first device
                if (_activeOutputs.IsEmpty)
                {
                    StartAudioCapture();
                }
                
                // Create new output device
                var outputDevice = new AdvancedAudioOutput(device, volume, _captureFormat, 500);
                _activeOutputs[deviceId] = outputDevice;
                
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error enabling device: {ex.Message}");
                return false;
            }
        }
        
        public bool DisableDevice(string deviceId)
        {
            if (_activeOutputs.TryRemove(deviceId, out var output))
            {
                output.Dispose();
                
                // If no more outputs, stop capturing
                if (_activeOutputs.IsEmpty)
                {
                    StopAudioCapture();
                }
                
                return true;
            }
            
            return false;
        }
        
        public bool SetDeviceVolume(string deviceId, float volume)
        {
            if (_activeOutputs.TryGetValue(deviceId, out var output))
            {
                output.SetVolume(volume);
                return true;
            }
            
            return false;
        }
        
        private void StartAudioCapture()
        {
            try
            {
                // Get the default device for capturing system audio
                var defaultDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                
                // Create a loopback capture with optimized latency settings
                _loopbackCapture = new WasapiLoopbackCapture(defaultDevice)
                {
                    ShareMode = AudioClientShareMode.Shared
                };
                _captureFormat = _loopbackCapture.WaveFormat; // Fixed typo from WWaveFormat to WaveFormat
                
                // Set up event handlers with direct processing for low latency
                _loopbackCapture.DataAvailable += OnAudioDataAvailable;
                _loopbackCapture.RecordingStopped += (s, e) => {
                    _loopbackCapture?.Dispose();
                    _loopbackCapture = null;
                };
                
                // Start capturing with a smaller buffer for reduced latency
                _loopbackCapture.StartRecording();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting audio capture: {ex.Message}");
            }
        }
        
        private void StopAudioCapture()
        {
            _loopbackCapture?.StopRecording();
        }
        
        private void OnAudioDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded <= 0) return;
            
            try
            {
                // For reduced latency, process directly in some cases
                if (_activeOutputs.Count <= 2) // Direct processing for 1-2 devices
                {
                    foreach (var output in _activeOutputs.Values)
                    {
                        output.ProcessAudio(e.Buffer, e.BytesRecorded);
                    }
                    // Still notify subscribers
                    AudioDataAvailable?.Invoke(this, new AudioDataEventArgs(e.Buffer, e.BytesRecorded));
                    return;
                }
                
                // Otherwise use queuing for 3+ devices
                byte[] bufferCopy = new byte[e.BytesRecorded];
                Buffer.BlockCopy(e.Buffer, 0, bufferCopy, 0, e.BytesRecorded);
                _audioQueue.Enqueue((bufferCopy, e.BytesRecorded));
                AudioDataAvailable?.Invoke(this, new AudioDataEventArgs(bufferCopy, e.BytesRecorded));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in audio data handling: {ex.Message}");
            }
        }
        
        private async Task ProcessAudioQueueAsync()
        {
            // Small fixed buffer for memory efficiency
            byte[] tempBuffer = new byte[16384];
            
            while (_isRunning && !_cancellation.IsCancellationRequested)
            {
                try
                {
                    bool processed = false;
                    
                    // Process all available audio data - increased batch size for efficiency
                    while (_audioQueue.TryDequeue(out var audioData))
                    {
                        // Send to all outputs with minimal overhead
                        var buffer = audioData.Buffer.Length <= tempBuffer.Length ? tempBuffer : audioData.Buffer;
                        if (audioData.Buffer != buffer)
                            Buffer.BlockCopy(audioData.Buffer, 0, buffer, 0, audioData.BytesRecorded);
                        
                        foreach (var output in _activeOutputs.Values)
                        {
                            output.ProcessAudio(buffer, audioData.BytesRecorded);
                        }
                        processed = true;
                        
                        // Check cancellation token periodically to avoid long blocking periods
                        if (_cancellation.IsCancellationRequested) break;
                    }
                    
                    // Use very short delay when no data to prevent CPU spinning but keep latency low
                    if (!processed)
                    {
                        await Task.Delay(1, _cancellation.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in audio processing task: {ex.Message}");
                    await Task.Delay(5, _cancellation.Token); // Reduced from 100ms to 5ms
                }
            }
        }
        
        public void Dispose()
        {
            _isRunning = false;
            _cancellation.Cancel();
            
            try
            {
                _processingTask?.Wait(1000);
            }
            catch { }
            
            StopAudioCapture();
            
            foreach (var output in _activeOutputs.Values)
            {
                output.Dispose();
            }
            
            _activeOutputs.Clear();
            _virtualDevice.Dispose();
            _cancellation.Dispose();
        }
        
        // Inner class for output device management
        private class AdvancedAudioOutput : IDisposable
        {
            private readonly IWavePlayer _wavePlayer;
            private readonly BufferedWaveProvider _waveProvider;
            private readonly SampleChannel _sampleChannel;
            private float _volume;
            private readonly byte[] _silenceBuffer;
            private readonly int _bufferSize;
            private long _lastBufferClearTime;
            private readonly object _bufferLock = new object();
            private readonly WaveFormat _format;
            private readonly CircularBuffer _circularBuffer; // Added for smoother playback
            private bool _isStarting = true;
            private int _underrunCounter = 0;
            
            public float Volume => _volume;
            
            public AdvancedAudioOutput(MMDevice device, float initialVolume, WaveFormat format, int bufferMs)
            {
                _volume = initialVolume;
                _lastBufferClearTime = Environment.TickCount64;
                _format = format;
                
                // Maintain a constant buffer size - best value determined empirically for most devices
                int actualBufferMs = 120; // Fixed buffer size that works well with most hardware
                
                // Create a circular buffer for smoother audio delivery
                _circularBuffer = new CircularBuffer(format.AverageBytesPerSecond / 4); // 250ms circular buffer
                
                // Create providers for audio processing with optimized settings
                _waveProvider = new BufferedWaveProvider(format) {
                    DiscardOnBufferOverflow = false // Changed to avoid losing audio data
                };
                
                // Calculate buffer size for stable playback
                _bufferSize = format.AverageBytesPerSecond * actualBufferMs / 1000;
                _waveProvider.BufferLength = _bufferSize * 2; // Double for safety
                
                // 10ms of silence for gap filling
                _silenceBuffer = new byte[format.AverageBytesPerSecond / 100]; 
                
                // Set up volume control
                _sampleChannel = new SampleChannel(_waveProvider);
                _sampleChannel.Volume = _volume;
                
                try
                {
                    // Standard shared mode for consistent playback across devices
                    _wavePlayer = new WasapiOut(
                        device,
                        AudioClientShareMode.Shared,
                        true,  // Event-driven for responsive playback
                        actualBufferMs);
                    
                    _wavePlayer.Init(_sampleChannel);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Primary mode failed: {ex.Message}, trying fallback mode");
                    // Fallback mode with more conservative settings
                    _wavePlayer = new WasapiOut(
                        device,
                        AudioClientShareMode.Shared,
                        false, // Timer-based fallback
                        actualBufferMs + 50);
                    
                    _wavePlayer.Init(_sampleChannel);
                }
                
                // Pre-buffer before starting playback to avoid initial stutter
                PrefillBuffer();
                
                // Start playback
                _wavePlayer.Play();
            }
            
            private void PrefillBuffer()
            {
                // Pre-fill with silence to prevent initial stutter - 100ms of audio
                byte[] initialBuffer = new byte[_format.AverageBytesPerSecond / 10];
                _waveProvider.AddSamples(initialBuffer, 0, initialBuffer.Length);
            }
            
            public void ProcessAudio(byte[] buffer, int bytesRecorded)
            {
                lock (_bufferLock)
                {
                    try
                    {
                        // Add to circular buffer first for smoothing
                        _circularBuffer.Write(buffer, 0, bytesRecorded);
                        
                        long now = Environment.TickCount64;
                        
                        // During startup, use more conservative buffer management
                        if (_isStarting && now - _lastBufferClearTime > 500) // 500ms after start
                        {
                            _isStarting = false;
                        }
                        
                        // Monitor buffer health
                        double bufferedMs = (double)_waveProvider.BufferedBytes / _format.AverageBytesPerSecond * 1000;
                        
                        if (bufferedMs < 30) // Critical underrun threshold (30ms)
                        {
                            _underrunCounter++;
                            
                            if (_underrunCounter >= 3) // Persistent underrun detected
                            {
                                // Add additional data from circular buffer to recover
                                byte[] recoveryBuffer = new byte[_format.AverageBytesPerSecond / 20]; // 50ms of data
                                int bytesRead = _circularBuffer.Read(recoveryBuffer, 0, recoveryBuffer.Length);
                                
                                if (bytesRead > 0)
                                {
                                    _waveProvider.AddSamples(recoveryBuffer, 0, bytesRead);
                                }
                                
                                _underrunCounter = 0;
                            }
                        }
                        else
                        {
                            _underrunCounter = 0;
                        }
                        
                        // Manage overall buffer size to prevent drift
                        if (bufferedMs > 250 && now - _lastBufferClearTime > 5000) // Clear if over 250ms buffered
                        {
                            int bytesToKeep = _format.AverageBytesPerSecond / 10; // Keep last 100ms of audio
                            byte[] recentAudio = new byte[bytesToKeep];
                            
                            // Extract the most recent audio data before clearing
                            // This is a simplified approach since BufferedWaveProvider doesn't support partial clearing
                            _waveProvider.ClearBuffer();
                            _lastBufferClearTime = now;
                            
                            // Add the recent audio data back from circular buffer
                            int bytesRead = _circularBuffer.Read(recentAudio, 0, bytesToKeep);
                            if (bytesRead > 0)
                            {
                                _waveProvider.AddSamples(recentAudio, 0, bytesRead);
                            }
                            
                            // Also add the current buffer
                            _waveProvider.AddSamples(buffer, 0, bytesRecorded);
                        }
                        else
                        {
                            // Regular processing - add samples directly from the source buffer
                            _waveProvider.AddSamples(buffer, 0, bytesRecorded);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error processing audio: {ex.Message}");
                    }
                }
            }
            
            public void SetVolume(float volume)
            {
                _volume = Math.Clamp(volume, 0f, 1f);
                _sampleChannel.Volume = _volume;
            }
            
            public void Dispose()
            {
                _wavePlayer?.Stop();
                _wavePlayer?.Dispose();
            }
        }
        
        // Implement a circular buffer for audio smoothing
        private class CircularBuffer
        {
            private readonly byte[] _buffer;
            private int _writePosition;
            private int _readPosition;
            private int _byteCount;
            private readonly object _lockObject = new object();
            
            public CircularBuffer(int capacity)
            {
                _buffer = new byte[capacity];
                _writePosition = 0;
                _readPosition = 0;
                _byteCount = 0;
            }
            
            public int Write(byte[] data, int offset, int count)
            {
                lock (_lockObject)
                {
                    int totalBytesWritten = 0;
                    
                    if (count > _buffer.Length - _byteCount)
                    {
                        count = _buffer.Length - _byteCount; // Limit to available space
                    }
                    
                    // Write to buffer
                    while (totalBytesWritten < count)
                    {
                        int bytesToWrite = Math.Min(count - totalBytesWritten, _buffer.Length - _writePosition);
                        Array.Copy(data, offset + totalBytesWritten, _buffer, _writePosition, bytesToWrite);
                        
                        _writePosition = (_writePosition + bytesToWrite) % _buffer.Length;
                        totalBytesWritten += bytesToWrite;
                        _byteCount += bytesToWrite;
                    }
                    
                    return totalBytesWritten;
                }
            }
            
            public int Read(byte[] data, int offset, int count)
            {
                lock (_lockObject)
                {
                    int totalBytesRead = 0;
                    
                    count = Math.Min(count, _byteCount); // Limit to available data
                    
                    // Read from buffer
                    while (totalBytesRead < count)
                    {
                        int bytesToRead = Math.Min(count - totalBytesRead, _buffer.Length - _readPosition);
                        Array.Copy(_buffer, _readPosition, data, offset + totalBytesRead, bytesToRead);
                        
                        _readPosition = (_readPosition + bytesToRead) % _buffer.Length;
                        totalBytesRead += bytesToRead;
                        _byteCount -= bytesToRead;
                    }
                    
                    return totalBytesRead;
                }
            }
            
            public void Clear()
            {
                lock (_lockObject)
                {
                    _readPosition = 0;
                    _writePosition = 0;
                    _byteCount = 0;
                }
            }
            
            public int Count => _byteCount;
        }
    }
}
