using AudioDual.Core.Diagnostics;
using AudioDual.Core.Platform;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Threading;

namespace AudioDual.Core.Capture
{
    public sealed class AudioCaptureEventArgs : EventArgs
    {
        public AudioCaptureEventArgs(byte[] buffer, int bytesRecorded)
        {
            Buffer = buffer;
            BytesRecorded = bytesRecorded;
        }

        public byte[] Buffer { get; }

        public int BytesRecorded { get; }
    }

    /// <summary>
    /// Owns exactly one WASAPI loopback capture instance for the process. Every output
    /// channel receives the same captured stream — see <see cref="Routing.AudioRouter"/>
    /// for the fan-out. Loopback capture can only run in shared mode; that's a Windows
    /// platform constraint, not a limitation of this class.
    /// </summary>
    public sealed class LoopbackCaptureService : IDisposable
    {
        private readonly CaptureFormatNegotiator _negotiator;
        private readonly MmcssThreadBooster _threadBooster;
        private readonly IAppLogger _logger;
        private WasapiLoopbackCapture? _capture;
        private int _threadBoostAttempted;

        public event EventHandler<AudioCaptureEventArgs>? DataAvailable;

        public LoopbackCaptureService(CaptureFormatNegotiator negotiator, MmcssThreadBooster threadBooster, IAppLogger logger)
        {
            _negotiator = negotiator;
            _threadBooster = threadBooster;
            _logger = logger;
        }

        public WaveFormat WaveFormat { get; private set; } = new WaveFormat(48000, 32, 2);

        /// <summary>
        /// The device ID of the render endpoint currently being loopback-captured.
        /// Null when capture is not active. Used by AudioRouter to prevent enabling
        /// output on the same device (which would create a feedback loop).
        /// </summary>
        public string? CaptureDeviceId { get; private set; }

        public bool IsCapturing => _capture is not null;

        public void Start()
        {
            if (_capture is not null)
            {
                return;
            }

            var device = _negotiator.ResolveDefaultRenderEndpoint();
            CaptureDeviceId = device.ID;

            _capture = new WasapiLoopbackCapture(device)
            {
                ShareMode = AudioClientShareMode.Shared
            };

            WaveFormat = _capture.WaveFormat;
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            _capture.StartRecording();
        }

        public void Stop()
        {
            _capture?.StopRecording();
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded <= 0)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _threadBoostAttempted, 1, 0) == 0)
            {
                _threadBooster.TryBoostCurrentThread();
            }

            try
            {
                DataAvailable?.Invoke(this, new AudioCaptureEventArgs(e.Buffer, e.BytesRecorded));
            }
            catch (Exception ex)
            {
                _logger.LogError("LoopbackCaptureService", "Error dispatching captured audio to subscribers.", ex);
            }
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            if (e.Exception is not null)
            {
                _logger.LogError("LoopbackCaptureService", "Capture stopped unexpectedly.", e.Exception);
            }

            _capture?.Dispose();
            _capture = null;
            CaptureDeviceId = null;
            _threadBoostAttempted = 0;
        }

        public void Dispose()
        {
            if (_capture is null)
            {
                return;
            }

            var capture = _capture;
            capture.DataAvailable -= OnDataAvailable;
            capture.RecordingStopped -= OnRecordingStopped;
            _capture = null;

            try
            {
                capture.StopRecording();
            }
            catch (Exception ex)
            {
                _logger.LogError("LoopbackCaptureService", "Error stopping capture during dispose.", ex);
            }

            capture.Dispose();
        }
    }
}
