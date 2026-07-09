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

            try
            {
                var device = _deviceRepository.GetDevice(deviceId);

                if (!_captureService.IsCapturing)
                {
                    _captureService.Start();
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
