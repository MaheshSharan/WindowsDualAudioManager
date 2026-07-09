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
            _telemetry = new LatencyTelemetry(_logger);

            var negotiator = new CaptureFormatNegotiator(_deviceRepository.DeviceEnumerator);
            _captureService = new LoopbackCaptureService(negotiator, _threadBooster, _logger);

            var options = new AudioRouterOptions
            {
                TargetLatencyMs = configuration.AudioBufferMs
            };

            _router = new AudioRouter(_captureService, _deviceRepository, options, _telemetry, _threadBooster, _logger);
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
            return _router.SetDeviceVolume(deviceId, volume);
        }

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
