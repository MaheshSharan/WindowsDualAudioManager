using AudioDual.Core.Buffering;
using AudioDual.Core.Diagnostics;
using AudioDual.Core.Platform;
using NAudio.Wave;
using System.Threading;

namespace AudioDual.Core.Output
{
    /// <summary>
    /// Adapts a <see cref="SpscRingBuffer"/> into the <see cref="IWaveProvider"/> shape
    /// WasapiOut reads from. This is the entire buffer between capture and playback for
    /// one output device — no BufferedWaveProvider, no intermediate queue.
    /// On underrun it fills only the measured shortfall with silence and reports it via
    /// telemetry, rather than the old pipeline's fixed-size silence padding.
    /// </summary>
    public sealed class RingBufferWaveProvider : IWaveProvider
    {
        private readonly SpscRingBuffer _ringBuffer;
        private readonly LatencyTelemetry _telemetry;
        private readonly string _channelId;
        private readonly MmcssThreadBooster _threadBooster;
        private int _threadBoostAttempted;

        public RingBufferWaveProvider(
            SpscRingBuffer ringBuffer,
            WaveFormat waveFormat,
            LatencyTelemetry telemetry,
            string channelId,
            MmcssThreadBooster threadBooster)
        {
            _ringBuffer = ringBuffer;
            WaveFormat = waveFormat;
            _telemetry = telemetry;
            _channelId = channelId;
            _threadBooster = threadBooster;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(byte[] buffer, int offset, int count)
        {
            // MMCSS registration must happen on the thread that will actually run the
            // audio callback. WasapiOut's render thread calls Read() repeatedly from
            // the same dedicated thread, so boosting once on first use is sufficient.
            if (Interlocked.CompareExchange(ref _threadBoostAttempted, 1, 0) == 0)
            {
                _threadBooster.TryBoostCurrentThread();
            }

            int bytesRead = _ringBuffer.Read(buffer, offset, count);

            double bufferedMilliseconds = (double)_ringBuffer.AvailableBytes / WaveFormat.AverageBytesPerSecond * 1000.0;
            _telemetry.ReportBufferedMilliseconds(_channelId, bufferedMilliseconds);

            if (bytesRead < count)
            {
                int shortfallBytes = count - bytesRead;
                Array.Clear(buffer, offset + bytesRead, shortfallBytes);

                double shortfallMilliseconds = (double)shortfallBytes / WaveFormat.AverageBytesPerSecond * 1000.0;
                _telemetry.ReportUnderrun(_channelId, shortfallMilliseconds);
            }

            return count;
        }
    }
}
