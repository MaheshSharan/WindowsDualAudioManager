using AudioDual.Core.Diagnostics;
using NAudio.Wave;

namespace AudioDual.Core.Output
{
    /// <summary>
    /// Sits in the sample provider chain purely to report real peak/RMS levels to
    /// <see cref="LatencyTelemetry"/> as audio actually flows through — it does not
    /// modify the samples it passes through.
    /// </summary>
    public sealed class TelemetrySampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly LatencyTelemetry _telemetry;
        private readonly string _channelId;

        public TelemetrySampleProvider(ISampleProvider source, LatencyTelemetry telemetry, string channelId)
        {
            _source = source;
            _telemetry = telemetry;
            _channelId = channelId;
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = _source.Read(buffer, offset, count);
            _telemetry.ReportSampleBlock(_channelId, new ReadOnlySpan<float>(buffer, offset, samplesRead));
            return samplesRead;
        }
    }
}
