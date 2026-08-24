using AudioDual.Core.Buffering;
using AudioDual.Core.Diagnostics;
using AudioDual.Core.Platform;
using AudioDual.Core.Routing;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Diagnostics;
using System.Threading;

namespace AudioDual.Core.Output
{
    /// <summary>
    /// Owns everything needed to play the routed audio stream out through a single
    /// device: the ring buffer that receives fanned-out capture data, the (usually
    /// empty) format-conversion chain, volume, telemetry, and the WasapiOut instance
    /// itself running in event-driven mode. This is the direct replacement for the
    /// old pipeline's AdvancedAudioOutput — no BufferedWaveProvider, no CircularBuffer
    /// "smoothing" layer, no periodic buffer-clearing.
    /// </summary>
    public sealed class AudioOutputChannel : IDisposable
    {
        private const int RingBufferSafetyMarginMs = 20;

        private readonly SpscRingBuffer _ringBuffer;
        private readonly IWavePlayer _wavePlayer;
        private readonly VolumeSampleProvider _volumeProvider;
        private readonly LatencyTelemetry _telemetry;
        private readonly IAppLogger _logger;
        private readonly int _prefillBytes;
        private readonly int _prefillTimeoutMs;
        private int _started;

        public string DeviceId { get; }

        public float Volume
        {
            get => _volumeProvider.Volume;
            set => _volumeProvider.Volume = Math.Clamp(value, 0f, 1f);
        }

        public AudioOutputChannel(
            MMDevice device,
            WaveFormat captureFormat,
            float initialVolume,
            AudioRouterOptions options,
            LatencyTelemetry telemetry,
            MmcssThreadBooster threadBooster,
            IAppLogger logger)
        {
            DeviceId = device.ID;
            _telemetry = telemetry;
            _logger = logger;

            // Ring buffer holds raw capture-format bytes. For wired devices, the
            // configured target latency (+safety margin) is sufficient. But Bluetooth
            // devices negotiate much larger WASAPI periods (100-300ms) — if the ring
            // buffer is smaller than what WASAPI reads per callback, every single read
            // underruns, fills with silence, and the perceived delay compounds rapidly.
            // Size the buffer to the larger of our configured target or what the device
            // actually needs.
            int devicePeriodMs = options.TargetLatencyMs;
            try
            {
                // The device's default period tells us how much data WASAPI will
                // request per callback — we need at least this much buffered
                var audioPeriod = device.AudioClient.DefaultDevicePeriod;
                int deviceDefaultPeriodMs = (int)(audioPeriod / 10000); // 100ns units to ms
                if (deviceDefaultPeriodMs > devicePeriodMs)
                {
                    devicePeriodMs = deviceDefaultPeriodMs;
                    logger.LogInformation("AudioOutputChannel",
                        $"Device '{device.FriendlyName}' has a default period of {deviceDefaultPeriodMs}ms " +
                        $"(higher than configured {options.TargetLatencyMs}ms). " +
                        "Ring buffer sized to device period to avoid cascading underruns.");
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning("AudioOutputChannel",
                    $"Could not query device period for '{device.FriendlyName}': {ex.Message}. " +
                    "Using configured target latency for ring buffer sizing.");
            }

            int ringBufferMilliseconds = devicePeriodMs + RingBufferSafetyMarginMs;
            // Ensure at least 3x the device period to have enough data across callbacks
            int minRingBufferMs = devicePeriodMs * 3;
            if (ringBufferMilliseconds < minRingBufferMs)
            {
                ringBufferMilliseconds = minRingBufferMs;
            }
            int ringBufferCapacityBytes = captureFormat.AverageBytesPerSecond * ringBufferMilliseconds / 1000;
            _prefillBytes = Math.Min(
                ringBufferCapacityBytes,
                Math.Max(captureFormat.BlockAlign, captureFormat.AverageBytesPerSecond * devicePeriodMs / 1000));
            _prefillTimeoutMs = Math.Clamp(devicePeriodMs * 2, 250, 1000);
            var overflowPolicy = options.OverflowPolicy == RingBufferOverflowPolicyOption.DropNewest
                ? RingBufferOverflowPolicy.DropNewest
                : RingBufferOverflowPolicy.DropOldest;
            _ringBuffer = new SpscRingBuffer(ringBufferCapacityBytes, overflowPolicy);

            var ringBufferProvider = new RingBufferWaveProvider(_ringBuffer, captureFormat, telemetry, DeviceId, threadBooster);

            ISampleProvider sampleChain = ringBufferProvider.ToSampleProvider();

            var shareMode = options.PreferExclusiveModeOutput ? AudioClientShareMode.Exclusive : AudioClientShareMode.Shared;
            var deviceMixFormat = shareMode == AudioClientShareMode.Shared ? device.AudioClient.MixFormat : captureFormat;

            sampleChain = SampleFormatConverter.Convert(sampleChain, deviceMixFormat, options.ResampleQuality);

            _volumeProvider = new VolumeSampleProvider(sampleChain)
            {
                Volume = Math.Clamp(initialVolume, 0f, 1f)
            };

            var telemetryTap = new TelemetrySampleProvider(_volumeProvider, telemetry, DeviceId);
            IWaveProvider finalWaveProvider = telemetryTap.ToWaveProvider();

            _wavePlayer = CreateWavePlayer(device, shareMode, options.TargetLatencyMs, finalWaveProvider);

            // Log the actual WASAPI buffer period — for Bluetooth devices, the driver
            // often negotiates a much larger period (100–300ms) than what we requested.
            // This is expected and not a bug, but it's critical diagnostic info.
            if (_wavePlayer is WasapiOut wasapiOut)
            {
                var actualLatency = wasapiOut.OutputWaveFormat;
                _logger.LogInformation("AudioOutputChannel",
                    $"Device '{device.FriendlyName}' initialized. " +
                    $"Requested latency: {options.TargetLatencyMs}ms, " +
                    $"Output format: {actualLatency}");
            }

        }

        public void Start()
        {
            if (Interlocked.Exchange(ref _started, 1) != 0)
            {
                return;
            }

            long deadline = Stopwatch.GetTimestamp() +
                (long)(_prefillTimeoutMs * (double)Stopwatch.Frequency / 1000.0);

            while (_ringBuffer.AvailableBytes < _prefillBytes && Stopwatch.GetTimestamp() < deadline)
            {
                Thread.Sleep(1);
            }

            if (_ringBuffer.AvailableBytes < _prefillBytes)
            {
                _logger.LogWarning(
                    "AudioOutputChannel",
                    $"Starting '{DeviceId}' before the prefill target was reached; continuing to avoid blocking startup.");
            }

            _wavePlayer.Play();
        }

        private IWavePlayer CreateWavePlayer(MMDevice device, AudioClientShareMode shareMode, int targetLatencyMs, IWaveProvider waveProvider)
        {
            try
            {
                var wavePlayer = new WasapiOut(device, shareMode, useEventSync: true, latency: targetLatencyMs);
                wavePlayer.Init(waveProvider);
                return wavePlayer;
            }
            catch (Exception ex) when (shareMode == AudioClientShareMode.Exclusive)
            {
                _logger.LogWarning(
                    "AudioOutputChannel",
                    $"Exclusive-mode init failed for '{device.FriendlyName}', falling back to shared mode: {ex.Message}");

                var fallbackPlayer = new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: true, latency: targetLatencyMs);
                fallbackPlayer.Init(waveProvider);
                return fallbackPlayer;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "AudioOutputChannel",
                    $"Event-driven WASAPI init failed for '{device.FriendlyName}', falling back to timer-driven mode: {ex.Message}");

                var fallbackPlayer = new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: false, latency: targetLatencyMs);
                fallbackPlayer.Init(waveProvider);
                return fallbackPlayer;
            }
        }

        /// <summary>
        /// Called by the router on the capture callback thread to feed this channel's
        /// ring buffer with raw capture-format bytes.
        /// </summary>
        public void Write(byte[] buffer, int bytesRecorded)
        {
            int bytesWritten = _ringBuffer.Write(buffer, 0, bytesRecorded);

            if (bytesWritten < bytesRecorded)
            {
                _telemetry.ReportOverrun(DeviceId, bytesRecorded - bytesWritten);
            }
        }

        public void Dispose()
        {
            DisposeWithoutTelemetryRemoval();
            _telemetry.RemoveChannel(DeviceId);
        }

        internal void DisposeWithoutTelemetryRemoval()
        {
            try
            {
                _wavePlayer.Stop();
                _wavePlayer.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError("AudioOutputChannel", $"Error disposing output for device '{DeviceId}'.", ex);
            }
        }
    }
}
