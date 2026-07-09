using AudioDual.Core.Buffering;
using AudioDual.Core.Diagnostics;
using AudioDual.Core.Platform;
using AudioDual.Core.Routing;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

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

            // Ring buffer holds raw capture-format bytes. Sized to the configured target
            // latency plus a small safety margin so a brief scheduling delay doesn't
            // immediately manifest as an audible underrun.
            int ringBufferMilliseconds = options.TargetLatencyMs + RingBufferSafetyMarginMs;
            int ringBufferCapacityBytes = captureFormat.AverageBytesPerSecond * ringBufferMilliseconds / 1000;
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
            try
            {
                _wavePlayer.Stop();
                _wavePlayer.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError("AudioOutputChannel", $"Error disposing output for device '{DeviceId}'.", ex);
            }

            _telemetry.RemoveChannel(DeviceId);
        }
    }
}
