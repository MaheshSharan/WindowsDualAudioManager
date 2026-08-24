using AudioDual.Core.Capture;
using AudioDual.Core.Devices;
using AudioDual.Core.Diagnostics;
using AudioDual.Core.Platform;
using AudioDual.Core.Routing;

namespace AudioDual.Core
{
    /// <summary>
    /// Composition root for the audio pipeline. Wires together device enumeration,
    /// loopback capture, and the router that fans captured audio out to each active
    /// output channel. This class intentionally contains no buffering, threading, or
    /// format-conversion logic of its own anymore -- all of that now lives in
    /// AudioRouter, AudioOutputChannel, and the modules under Core/Buffering,
    /// Core/Capture, Core/Output, and Core/Devices. Kept as the entry point MainForm
    /// talks to so the UI layer's interaction surface didn't need to change shape for
    /// this rework.
    /// </summary>
    public class AdvancedAudioEngine : IDisposable
    {
        private readonly AudioDeviceRepository _deviceRepository;
        private readonly LoopbackCaptureService _captureService;
        private readonly MmcssThreadBooster _threadBooster;
        private readonly AudioRouter _router;
        private readonly LatencyTelemetry _telemetry;
        private readonly IAppLogger _logger;

        public AdvancedAudioEngine(AppConfiguration configuration, IAppLogger? logger = null)
        {
            _logger = logger ?? new FileAppLogger();

            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            _deviceRepository = new AudioDeviceRepository(_logger);
            _threadBooster = new MmcssThreadBooster(_logger);
            _telemetry = new LatencyTelemetry();

            var negotiator = new CaptureFormatNegotiator(_deviceRepository.DeviceEnumerator);
            _captureService = new LoopbackCaptureService(negotiator, _threadBooster, _logger);

            var options = new AudioRouterOptions
            {
                TargetLatencyMs = configuration.AudioBufferMs,
                PreferExclusiveModeOutput = configuration.PreferExclusiveModeOutput
            };

            _router = new AudioRouter(_captureService, _deviceRepository, options, _telemetry, _threadBooster, _logger);
        }

        public event EventHandler<string> DeviceRemoved
        {
            add => _deviceRepository.DeviceRemoved += value;
            remove => _deviceRepository.DeviceRemoved -= value;
        }

        public event EventHandler<string> DeviceStateChanged
        {
            add => _deviceRepository.DeviceStateChanged += value;
            remove => _deviceRepository.DeviceStateChanged -= value;
        }

        public List<AudioDevice> GetAudioDevices()
        {
            return _deviceRepository.GetRenderDevices(_router.ActiveDeviceVolumes);
        }

        public bool EnableDevice(string deviceId, float volume = 1.0f)
        {
            return _router.EnableDevice(deviceId, volume);
        }

        public bool DisableDevice(string deviceId)
        {
            return _router.DisableDevice(deviceId);
        }

        public bool SetDeviceVolume(string deviceId, float volume)
        {
            if (_router.SetDeviceVolume(deviceId, volume))
            {
                return true;
            }

            try
            {
                var device = _deviceRepository.GetDevice(deviceId);
                device.AudioEndpointVolume.MasterVolumeLevelScalar = volume;
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("AdvancedAudioEngine", $"Error setting Windows system volume for device '{deviceId}'.", ex);
                return false;
            }
        }

        public bool UpdateTargetLatency(int targetLatencyMs)
        {
            return _router.UpdateTargetLatency(targetLatencyMs);
        }

        /// <summary>
        /// Returns the device ID of the endpoint currently being loopback-captured,
        /// or null if capture is not active. Used by the UI to identify the capture
        /// source and prevent the user from enabling it as an output (feedback loop).
        /// </summary>
        public string? GetCaptureDeviceId() => _captureService.CaptureDeviceId;

        /// <summary>
        /// Real per-channel buffering health and signal levels, computed from actual
        /// sample data as it flows through each output channel. Exposed for the level
        /// meter to read from once it's rebuilt against real data instead of drawing
        /// from a device's static Volume setting.
        /// </summary>
        public LatencyTelemetry Telemetry => _telemetry;

        private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs args)
        {
            try
            {
                var exception = args.ExceptionObject as Exception;
                _logger.LogError("AdvancedAudioEngine", "Unhandled exception in application domain.", exception);

                if (args.IsTerminating)
                {
                    _router.Dispose();
                }
            }
            catch
            {
                // Last-resort handler must never throw, or the process terminates without
                // any record of the original failure.
            }
        }

        public void Dispose()
        {
            _router.Dispose();
            _captureService.Dispose();
            _deviceRepository.Dispose();
            _threadBooster.Dispose();

            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        }
    }
}
