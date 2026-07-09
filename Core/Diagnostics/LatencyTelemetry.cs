using System.Collections.Concurrent;
using System.Threading;

namespace AudioDual.Core.Diagnostics
{
    /// <summary>
    /// Snapshot of one output channel's health at a point in time.
    /// </summary>
    public readonly record struct ChannelTelemetrySnapshot(
        double BufferedMilliseconds,
        long UnderrunCount,
        long OverrunCount,
        float PeakLevel,
        float RmsLevel);

    /// <summary>
    /// Tracks per-channel buffering health and real signal levels computed from actual
    /// sample data. This is what the Phase 3 level meter reads from — the old UI drew
    /// bars from each device's static Volume setting, which is not a real audio level;
    /// this class exists so that stops being true.
    /// </summary>
    public sealed class LatencyTelemetry
    {
        private sealed class ChannelState
        {
            public double BufferedMilliseconds;
            public long UnderrunCount;
            public long OverrunCount;
            public float PeakLevel;
            public float RmsLevel;
        }

        private readonly ConcurrentDictionary<string, ChannelState> _channels = new();
        private readonly IAppLogger _logger;

        public LatencyTelemetry(IAppLogger logger)
        {
            _logger = logger;
        }

        public void ReportBufferedMilliseconds(string channelId, double bufferedMilliseconds)
        {
            GetOrAddChannel(channelId).BufferedMilliseconds = bufferedMilliseconds;
        }

        public void ReportUnderrun(string channelId, double shortfallMilliseconds)
        {
            var channel = GetOrAddChannel(channelId);
            Interlocked.Increment(ref channel.UnderrunCount);
            _logger.LogWarning("LatencyTelemetry", $"Underrun on channel '{channelId}': {shortfallMilliseconds:F1}ms shortfall.");
        }

        public void ReportOverrun(string channelId, int discardedBytes)
        {
            var channel = GetOrAddChannel(channelId);
            Interlocked.Increment(ref channel.OverrunCount);
            _logger.LogWarning("LatencyTelemetry", $"Overrun on channel '{channelId}': discarded {discardedBytes} bytes.");
        }

        /// <summary>
        /// Computes and stores real peak/RMS levels from a block of float samples in
        /// the range [-1, 1]. Takes float samples rather than raw bytes because the
        /// pipeline's format is not guaranteed to be 16-bit PCM — WASAPI shared-mode
        /// mix formats are commonly IEEE float — and float is also the format
        /// ISampleProvider already works in throughout the rest of the pipeline, so no
        /// separate decoding assumption is needed here.
        /// </summary>
        public void ReportSampleBlock(string channelId, ReadOnlySpan<float> samples)
        {
            float peak = 0f;
            double sumOfSquares = 0.0;

            foreach (float sample in samples)
            {
                float absolute = Math.Abs(sample);
                if (absolute > peak)
                {
                    peak = absolute;
                }

                sumOfSquares += sample * (double)sample;
            }

            float rms = samples.Length > 0 ? (float)Math.Sqrt(sumOfSquares / samples.Length) : 0f;

            var channel = GetOrAddChannel(channelId);
            channel.PeakLevel = peak;
            channel.RmsLevel = rms;
        }

        public ChannelTelemetrySnapshot GetSnapshot(string channelId)
        {
            var channel = GetOrAddChannel(channelId);
            return new ChannelTelemetrySnapshot(
                channel.BufferedMilliseconds,
                Interlocked.Read(ref channel.UnderrunCount),
                Interlocked.Read(ref channel.OverrunCount),
                channel.PeakLevel,
                channel.RmsLevel);
        }

        public void RemoveChannel(string channelId)
        {
            _channels.TryRemove(channelId, out _);
        }

        private ChannelState GetOrAddChannel(string channelId)
        {
            return _channels.GetOrAdd(channelId, static _ => new ChannelState());
        }
    }
}
