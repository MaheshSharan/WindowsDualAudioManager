namespace AudioDual.Core.Routing
{
    public enum ResampleQuality
    {
        Fast,
        Balanced,
        High
    }

    /// <summary>
    /// Configuration for <see cref="AudioRouter"/> and the output channels it creates.
    /// Every field here is consumed somewhere in the pipeline — see AudioOutputChannel
    /// and SampleFormatConverter — rather than declared and left unread.
    /// </summary>
    public sealed class AudioRouterOptions
    {
        private const int MinimumTargetLatencyMs = 15;
        private const int MaximumTargetLatencyMs = 500;

        private int _targetLatencyMs = 25;

        /// <summary>
        /// The latency each output channel's ring buffer is sized to hold, in
        /// milliseconds. Lower values reduce delay but increase the risk of an
        /// audible underrun on slower hardware or under system load.
        /// </summary>
        public int TargetLatencyMs
        {
            get => _targetLatencyMs;
            set => _targetLatencyMs = Math.Clamp(value, MinimumTargetLatencyMs, MaximumTargetLatencyMs);
        }

        /// <summary>
        /// When true, an output channel requests WASAPI exclusive mode. This can shave
        /// a few more milliseconds off output latency, but locks the device from other
        /// applications while active. Off by default — this is an explicit, informed,
        /// per-device opt-in, never a default behavior change a user didn't ask for.
        /// </summary>
        public bool PreferExclusiveModeOutput { get; set; }

        /// <summary>
        /// Controls the filter length used when an output device's mix format doesn't
        /// match the capture format and resampling is actually necessary. Has no effect
        /// when formats already match, which is the common case and requires no
        /// resampling at all.
        /// </summary>
        public ResampleQuality ResampleQuality { get; set; } = ResampleQuality.Balanced;

        /// <summary>
        /// When true, an output channel that underruns writes silence for the measured
        /// shortfall and logs the event via telemetry. When false, underruns still
        /// occur but are not separately logged — used for testing against telemetry
        /// noise in constrained environments.
        /// </summary>
        public bool UnderrunRecoveryEnabled { get; set; } = true;

        public RingBufferOverflowPolicyOption OverflowPolicy { get; set; } = RingBufferOverflowPolicyOption.DropOldest;
    }

    /// <summary>
    /// Mirrors <see cref="Buffering.RingBufferOverflowPolicy"/> so options can be
    /// expressed without a direct dependency from the options type onto the
    /// buffering namespace; AudioOutputChannel maps this to the real enum.
    /// </summary>
    public enum RingBufferOverflowPolicyOption
    {
        DropOldest,
        DropNewest
    }
}
