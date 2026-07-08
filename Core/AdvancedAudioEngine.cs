using AudioDual.Core.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Collections.Concurrent;

namespace AudioDual.Core
{
    public class AdvancedAudioEngine : IDisposable
    {
        private const int IdlePollIntervalMs = 1;
        private const int ErrorBackoffMs = 5;
        private const int DirectProcessingDeviceThreshold = 2;

        private readonly MMDeviceEnumerator _deviceEnumerator;
        private readonly ConcurrentDictionary<string, AdvancedAudioOutput> _activeOutputs;
        private readonly ConcurrentQueue<(byte[] Buffer, int BytesRecorded)> _audioQueue;
        private readonly CancellationTokenSource _cancellation;
        private readonly IAppLogger _logger;
        private readonly int _configuredBufferMilliseconds;
        private WasapiCapture? _loopbackCapture;
        private WaveFormat _captureFormat;
        private bool _isRunning;
        private Thread? _processingThread;

        public event EventHandler<AudioDataEventArgs>? AudioDataAvailable;

        public AdvancedAudioEngine(AppConfiguration configuration, IAppLogger? logger = null)
        {
            _logger = logger ?? new FileAppLogger();
            _configuredBufferMilliseconds = configuration.AudioBufferMs;

            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            _deviceEnumerator = new MMDeviceEnumerator();
            _activeOutputs = new ConcurrentDictionary<string, AdvancedAudioOutput>();
            _captureFormat = new WaveFormat(48000, 16, 2);
            _audioQueue = new ConcurrentQueue<(byte[] Buffer, int BytesRecorded)>();
            _cancellation = new CancellationTokenSource();

            _isRunning = true;
            _processingThread = new Thread(ProcessAudioQueueLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.Highest,
                Name = "AudioDual.RoutingThread"
            };
            _processingThread.Start();
        }

        private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs args)
        {
            try
            {
                var exception = args.ExceptionObject as Exception;
                _logger.LogError("AdvancedAudioEngine", "Unhandled exception in application domain.", exception);

                if (args.IsTerminating)
                {
                    StopAudioCapture();
                    foreach (var output in _activeOutputs.Values)
                    {
                        try
                        {
                            output.Dispose();
                        }
                        catch (Exception disposeException)
                        {
                            _logger.LogError("AdvancedAudioEngine", "Error disposing output during termination.", disposeException);
                        }
                    }
                }
            }
            catch
            {
                // Last-resort handler must never throw, or the process terminates without
                // any record of the original failure.
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
                if (device == null)
                {
                    return false;
                }

                if (_activeOutputs.ContainsKey(deviceId))
                {
                    _activeOutputs[deviceId].SetVolume(volume);
                    return true;
                }

                if (_activeOutputs.IsEmpty)
                {
                    StartAudioCapture();
                }

                var outputDevice = new AdvancedAudioOutput(device, volume, _captureFormat, _configuredBufferMilliseconds, _logger);
                _activeOutputs[deviceId] = outputDevice;

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("AdvancedAudioEngine", $"Error enabling device '{deviceId}'.", ex);
                return false;
            }
        }

        public bool DisableDevice(string deviceId)
        {
            if (_activeOutputs.TryRemove(deviceId, out var output))
            {
                output.Dispose();

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
                var defaultDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

                _loopbackCapture = new WasapiLoopbackCapture(defaultDevice)
                {
                    ShareMode = AudioClientShareMode.Shared
                };
                _captureFormat = _loopbackCapture.WaveFormat;

                _loopbackCapture.DataAvailable += OnAudioDataAvailable;
                _loopbackCapture.RecordingStopped += (s, e) =>
                {
                    _loopbackCapture?.Dispose();
                    _loopbackCapture = null;
                };

                _loopbackCapture.StartRecording();
            }
            catch (Exception ex)
            {
                _logger.LogError("AdvancedAudioEngine", "Error starting audio capture.", ex);
            }
        }

        private void StopAudioCapture()
        {
            _loopbackCapture?.StopRecording();
        }

        private void OnAudioDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded <= 0)
            {
                return;
            }

            try
            {
                if (_activeOutputs.Count <= DirectProcessingDeviceThreshold)
                {
                    foreach (var output in _activeOutputs.Values)
                    {
                        output.ProcessAudio(e.Buffer, e.BytesRecorded);
                    }

                    AudioDataAvailable?.Invoke(this, new AudioDataEventArgs(e.Buffer, e.BytesRecorded));
                    return;
                }

                byte[] bufferCopy = new byte[e.BytesRecorded];
                Buffer.BlockCopy(e.Buffer, 0, bufferCopy, 0, e.BytesRecorded);
                _audioQueue.Enqueue((bufferCopy, e.BytesRecorded));
                AudioDataAvailable?.Invoke(this, new AudioDataEventArgs(bufferCopy, e.BytesRecorded));
            }
            catch (Exception ex)
            {
                _logger.LogError("AdvancedAudioEngine", "Error in audio data handling.", ex);
            }
        }

        /// <summary>
        /// Drains queued audio for the 3+ device fan-out path (see OnAudioDataAvailable).
        /// Runs on a dedicated, highest-priority Thread rather than a Task so that thread
        /// priority is a supported public API call, not a reflection reach into Task's
        /// private implementation details.
        ///
        /// NOTE: this dual-path (direct vs. queued) processing model, and the queue itself,
        /// are known technical debt — the v1.2 pipeline rework (Phase 1: AudioRouter +
        /// SpscRingBuffer) replaces both with a single event-driven path. Left as-is here
        /// deliberately rather than half-refactored, since Phase 1 removes this method entirely.
        /// </summary>
        private void ProcessAudioQueueLoop()
        {
            var cancellationWaitHandle = _cancellation.Token.WaitHandle;

            while (_isRunning && !_cancellation.IsCancellationRequested)
            {
                try
                {
                    bool processedAny = false;

                    while (_audioQueue.TryDequeue(out var audioData))
                    {
                        foreach (var output in _activeOutputs.Values)
                        {
                            output.ProcessAudio(audioData.Buffer, audioData.BytesRecorded);
                        }

                        processedAny = true;

                        if (_cancellation.IsCancellationRequested)
                        {
                            break;
                        }
                    }

                    if (!processedAny)
                    {
                        cancellationWaitHandle.WaitOne(IdlePollIntervalMs);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError("AdvancedAudioEngine", "Error in audio processing loop.", ex);
                    cancellationWaitHandle.WaitOne(ErrorBackoffMs);
                }
            }
        }

        public void Dispose()
        {
            _isRunning = false;
            _cancellation.Cancel();

            try
            {
                _processingThread?.Join(TimeSpan.FromSeconds(1));
            }
            catch (Exception ex)
            {
                _logger.LogError("AdvancedAudioEngine", "Error joining processing thread during dispose.", ex);
            }

            StopAudioCapture();

            foreach (var output in _activeOutputs.Values)
            {
                output.Dispose();
            }

            _activeOutputs.Clear();
            _cancellation.Dispose();

            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        }

        // Inner class for output device management
        private class AdvancedAudioOutput : IDisposable
        {
            private const int MinimumBufferMilliseconds = 15;
            private const int MaximumBufferMilliseconds = 500;
            private const int FallbackBufferPaddingMilliseconds = 50;
            private const int CircularBufferCapacityDivisor = 4; // 250ms circular buffer at typical 48kHz/16-bit/stereo
            private const int PrefillDivisor = 10; // 100ms of silence pre-fill
            private const int UnderrunThresholdMs = 30;
            private const int PersistentUnderrunCount = 3;
            private const int RecoveryBufferDivisor = 20; // 50ms of recovery data
            private const int OverflowThresholdMs = 250;
            private const int OverflowClearCooldownMs = 5000;
            private const int OverflowRetainedAudioDivisor = 10; // keep last 100ms on clear

            private readonly IWavePlayer _wavePlayer;
            private readonly BufferedWaveProvider _waveProvider;
            private readonly SampleChannel _sampleChannel;
            private readonly object _bufferLock = new();
            private readonly WaveFormat _format;
            private readonly CircularBuffer _circularBuffer;
            private readonly IAppLogger _logger;
            private float _volume;
            private long _lastBufferClearTime;
            private bool _isStarting = true;
            private int _underrunCounter;

            public float Volume => _volume;

            public AdvancedAudioOutput(MMDevice device, float initialVolume, WaveFormat format, int bufferMilliseconds, IAppLogger logger)
            {
                _logger = logger;
                _volume = initialVolume;
                _lastBufferClearTime = Environment.TickCount64;
                _format = format;

                int actualBufferMs = Math.Clamp(bufferMilliseconds, MinimumBufferMilliseconds, MaximumBufferMilliseconds);

                _circularBuffer = new CircularBuffer(format.AverageBytesPerSecond / CircularBufferCapacityDivisor);

                _waveProvider = new BufferedWaveProvider(format)
                {
                    DiscardOnBufferOverflow = false
                };

                int bufferSize = format.AverageBytesPerSecond * actualBufferMs / 1000;
                _waveProvider.BufferLength = bufferSize * 2;

                _sampleChannel = new SampleChannel(_waveProvider);
                _sampleChannel.Volume = _volume;

                try
                {
                    _wavePlayer = new WasapiOut(
                        device,
                        AudioClientShareMode.Shared,
                        true,
                        actualBufferMs);

                    _wavePlayer.Init(_sampleChannel);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("AdvancedAudioOutput", $"Event-driven WASAPI init failed for '{device.FriendlyName}', falling back to timer-driven mode: {ex.Message}");

                    _wavePlayer = new WasapiOut(
                        device,
                        AudioClientShareMode.Shared,
                        false,
                        actualBufferMs + FallbackBufferPaddingMilliseconds);

                    _wavePlayer.Init(_sampleChannel);
                }

                PrefillBuffer();
                _wavePlayer.Play();
            }

            private void PrefillBuffer()
            {
                byte[] initialBuffer = new byte[_format.AverageBytesPerSecond / PrefillDivisor];
                _waveProvider.AddSamples(initialBuffer, 0, initialBuffer.Length);
            }

            public void ProcessAudio(byte[] buffer, int bytesRecorded)
            {
                lock (_bufferLock)
                {
                    try
                    {
                        _circularBuffer.Write(buffer, 0, bytesRecorded);

                        long now = Environment.TickCount64;

                        if (_isStarting && now - _lastBufferClearTime > 500)
                        {
                            _isStarting = false;
                        }

                        double bufferedMs = (double)_waveProvider.BufferedBytes / _format.AverageBytesPerSecond * 1000;

                        if (bufferedMs < UnderrunThresholdMs)
                        {
                            _underrunCounter++;

                            if (_underrunCounter >= PersistentUnderrunCount)
                            {
                                byte[] recoveryBuffer = new byte[_format.AverageBytesPerSecond / RecoveryBufferDivisor];
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

                        if (bufferedMs > OverflowThresholdMs && now - _lastBufferClearTime > OverflowClearCooldownMs)
                        {
                            int bytesToKeep = _format.AverageBytesPerSecond / OverflowRetainedAudioDivisor;
                            byte[] recentAudio = new byte[bytesToKeep];

                            _waveProvider.ClearBuffer();
                            _lastBufferClearTime = now;

                            int bytesRead = _circularBuffer.Read(recentAudio, 0, bytesToKeep);
                            if (bytesRead > 0)
                            {
                                _waveProvider.AddSamples(recentAudio, 0, bytesRead);
                            }

                            _waveProvider.AddSamples(buffer, 0, bytesRecorded);
                        }
                        else
                        {
                            _waveProvider.AddSamples(buffer, 0, bytesRecorded);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("AdvancedAudioOutput", "Error processing audio.", ex);
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
                try
                {
                    _wavePlayer.Stop();
                    _wavePlayer.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError("AdvancedAudioOutput", "Error disposing WASAPI output.", ex);
                }
            }
        }

        // Circular buffer used for underrun/overflow recovery smoothing.
        // NOTE: replaced entirely by SpscRingBuffer in the Phase 1 pipeline rework.
        private class CircularBuffer
        {
            private readonly byte[] _buffer;
            private readonly object _lockObject = new();
            private int _writePosition;
            private int _readPosition;
            private int _byteCount;

            public CircularBuffer(int capacity)
            {
                _buffer = new byte[capacity];
            }

            public int Write(byte[] data, int offset, int count)
            {
                lock (_lockObject)
                {
                    int totalBytesWritten = 0;

                    if (count > _buffer.Length - _byteCount)
                    {
                        count = _buffer.Length - _byteCount;
                    }

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
                    count = Math.Min(count, _byteCount);

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
        }
    }
}
