using AudioDual.Core.Capture;
using AudioDual.Core.Devices;
using AudioDual.Core.Diagnostics;
using AudioDual.Core.Output;
using AudioDual.Core.Platform;
using System.Collections.Concurrent;
using System.Linq;

namespace AudioDual.Core.Routing
{
    /// <summary>
    /// Fans the loopback capture stream out to every active <see cref="AudioOutputChannel"/>.
    /// Data moves directly from the WASAPI capture callback into each channel's ring
    /// buffer with no intermediate queue and no polling task — the property that made
    /// the old pipeline's delay and jitter worse than the sum of its buffer sizes.
    /// </summary>
    public sealed class AudioRouter : IDisposable
    {
        private readonly LoopbackCaptureService _captureService;
        private readonly AudioDeviceRepository _deviceRepository;
        private readonly AudioRouterOptions _options;
        private readonly LatencyTelemetry _telemetry;
        private readonly MmcssThreadBooster _threadBooster;
        private readonly IAppLogger _logger;
        private readonly ConcurrentDictionary<string, AudioOutputChannel> _activeChannels = new();

        public AudioRouter(
            LoopbackCaptureService captureService,
            AudioDeviceRepository deviceRepository,
            AudioRouterOptions options,
            LatencyTelemetry telemetry,
            MmcssThreadBooster threadBooster,
            IAppLogger logger)
        {
            _captureService = captureService;
            _deviceRepository = deviceRepository;
            _options = options;
            _telemetry = telemetry;
            _threadBooster = threadBooster;
            _logger = logger;

            _captureService.DataAvailable += OnCaptureDataAvailable;
            _deviceRepository.DeviceRemoved += OnDeviceRemoved;
        }

        public IReadOnlyDictionary<string, float> ActiveDeviceVolumes =>
            _activeChannels.ToDictionary(pair => pair.Key, pair => pair.Value.Volume);

        public bool EnableDevice(string deviceId, float volume)
        {
            if (_activeChannels.TryGetValue(deviceId, out var existingChannel))
            {
                existingChannel.Volume = volume;
                return true;
            }

            // Guard: prevent routing captured audio back to the same device it's
            // being captured from — this would create an infinite feedback loop
            // (audio plays → gets loopback-captured → plays again → echo builds).
            if (_captureService.CaptureDeviceId == deviceId)
            {
                _logger.LogWarning("AudioRouter",
                    $"Blocked enable for device '{deviceId}' — it is the active loopback capture source. " +
                    "Routing output to the capture source device would create an audio feedback loop.");
                return false;
            }

            try
            {
                var device = _deviceRepository.GetDevice(deviceId);

                if (!_captureService.IsCapturing)
                {
                    _captureService.Start();

                    // Re-check after starting capture — the device we're about to
                    // enable may have become the capture source if it's the system default
                    if (_captureService.CaptureDeviceId == deviceId)
                    {
                        _captureService.Stop();
                        _logger.LogWarning("AudioRouter",
                            $"Blocked enable for device '{deviceId}' — it became the capture source when capture started.");
                        return false;
                    }
                }

                var channel = new AudioOutputChannel(
                    device,
                    _captureService.WaveFormat,
                    volume,
                    _options,
                    _telemetry,
                    _threadBooster,
                    _logger);

                _activeChannels[deviceId] = channel;
                try
                {
                    channel.Start();
                }
                catch
                {
                    ((ICollection<KeyValuePair<string, AudioOutputChannel>>)_activeChannels)
                        .Remove(new KeyValuePair<string, AudioOutputChannel>(deviceId, channel));
                    channel.Dispose();
                    throw;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("AudioRouter", $"Error enabling device '{deviceId}'.", ex);
                return false;
            }
        }

        public bool DisableDevice(string deviceId)
        {
            if (!_activeChannels.TryRemove(deviceId, out var channel))
            {
                return false;
            }

            channel.Dispose();

            if (_activeChannels.IsEmpty)
            {
                _captureService.Stop();
            }

            return true;
        }

        public bool SetDeviceVolume(string deviceId, float volume)
        {
            if (_activeChannels.TryGetValue(deviceId, out var channel))
            {
                channel.Volume = volume;
                return true;
            }

            return false;
        }

        public bool UpdateTargetLatency(int targetLatencyMs)
        {
            int previousLatencyMs = _options.TargetLatencyMs;
            _options.TargetLatencyMs = targetLatencyMs;
            int appliedLatencyMs = _options.TargetLatencyMs;
            if (_options.TargetLatencyMs == previousLatencyMs)
            {
                return true;
            }

            bool succeeded = true;

            foreach (var pair in _activeChannels.ToArray())
            {
                if (!_activeChannels.TryGetValue(pair.Key, out var currentChannel) ||
                    !ReferenceEquals(currentChannel, pair.Value))
                {
                    continue;
                }

                try
                {
                    var device = _deviceRepository.GetDevice(pair.Key);
                    var replacement = new AudioOutputChannel(
                        device,
                        _captureService.WaveFormat,
                        currentChannel.Volume,
                        _options,
                        _telemetry,
                        _threadBooster,
                        _logger);

                    if (!_activeChannels.TryUpdate(pair.Key, replacement, currentChannel))
                    {
                        replacement.DisposeWithoutTelemetryRemoval();
                        continue;
                    }

                    try
                    {
                        replacement.Start();
                        currentChannel.DisposeWithoutTelemetryRemoval();
                    }
                    catch
                    {
                        _activeChannels.TryUpdate(pair.Key, currentChannel, replacement);
                        replacement.Dispose();
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    succeeded = false;
                    _logger.LogError("AudioRouter",
                        $"Error applying {appliedLatencyMs}ms latency to device '{pair.Key}'.", ex);
                }
            }

            return succeeded;
        }

        private void OnCaptureDataAvailable(object? sender, AudioCaptureEventArgs e)
        {
            foreach (var channel in _activeChannels.Values)
            {
                channel.Write(e.Buffer, e.BytesRecorded);
            }
        }

        private void OnDeviceRemoved(object? sender, string deviceId)
        {
            if (DisableDevice(deviceId))
            {
                _logger.LogWarning("AudioRouter", $"Device '{deviceId}' was disconnected and its output channel was stopped.");
            }
        }

        public void Dispose()
        {
            _captureService.DataAvailable -= OnCaptureDataAvailable;
            _deviceRepository.DeviceRemoved -= OnDeviceRemoved;

            _captureService.Stop();

            foreach (var channel in _activeChannels.Values)
            {
                channel.Dispose();
            }

            _activeChannels.Clear();
        }
    }
}
