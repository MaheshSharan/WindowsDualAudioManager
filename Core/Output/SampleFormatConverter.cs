using AudioDual.Core.Routing;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AudioDual.Core.Output
{
    /// <summary>
    /// Wraps a sample provider with resampling and/or channel conversion, but only
    /// when the source format actually differs from what the output device wants.
    /// The old pipeline never checked this at all — every output device received
    /// audio without any format negotiation, meaning a device whose mix format
    /// differed from the capture format was left to WASAPI to fail on or silently
    /// mishandle. This makes format matching an explicit, deliberate step instead.
    /// </summary>
    public static class SampleFormatConverter
    {
        /// <summary>
        /// Returns a sample provider producing audio in <paramref name="targetFormat"/>.
        /// If <paramref name="source"/> already matches, the same instance is returned
        /// unchanged — no conversion stage is inserted into the pipeline for the common
        /// case where formats already agree.
        /// </summary>
        /// <remarks>
        /// <paramref name="quality"/> is accepted and threaded through from configuration,
        /// but NAudio's WdlResamplingSampleProvider does not expose filter-length or
        /// quality tuning in its public constructor — it always uses the WDL resampler's
        /// own internal quality settings. The parameter is kept here rather than removed
        /// so the call site and AudioRouterOptions don't need to change if a
        /// quality-tunable resampler implementation replaces this one later; today it has
        /// no effect on the resampling path itself.
        /// </remarks>
        public static ISampleProvider Convert(ISampleProvider source, WaveFormat targetFormat, ResampleQuality quality)
        {
            ISampleProvider result = source;

            if (result.WaveFormat.Channels != targetFormat.Channels)
            {
                result = ConvertChannelCount(result, targetFormat.Channels);
            }

            if (result.WaveFormat.SampleRate != targetFormat.SampleRate)
            {
                result = new WdlResamplingSampleProvider(result, targetFormat.SampleRate);
            }

            return result;
        }

        private static ISampleProvider ConvertChannelCount(ISampleProvider source, int targetChannels)
        {
            return (source.WaveFormat.Channels, targetChannels) switch
            {
                (1, 2) => new MonoToStereoSampleProvider(source),
                (2, 1) => new StereoToMonoSampleProvider(source),
                _ => source // Exotic channel layouts (e.g. 5.1) are left as-is; NAudio has
                            // no general N-to-M channel matrix, and down/up-mixing those
                            // correctly is a distinct feature, not a silent approximation.
            };
        }
    }
}
