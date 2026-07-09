namespace AudioDual.Core.Buffering
{
    /// <summary>
    /// What a ring buffer does when a write arrives and there isn't room for it.
    /// The old pipeline padded gaps with silence and periodically cleared its buffer
    /// outright, both of which are audible. Neither policy here does that: both
    /// resolve an overflow by discarding audio data instead of manufacturing silence.
    /// </summary>
    public enum RingBufferOverflowPolicy
    {
        /// <summary>
        /// Discard the oldest buffered bytes to make room for the incoming write.
        /// Keeps latency bounded at the cost of a small, one-time glitch on overflow.
        /// This is the default: for live audio, the most recent data is normally the
        /// more useful data to keep.
        /// </summary>
        DropOldest,

        /// <summary>
        /// Discard the incoming write instead of touching what's already buffered.
        /// Useful when the consumer is a fixed, steady drain rate and a producer
        /// burst is the anomaly, rather than the buffered content being stale.
        /// </summary>
        DropNewest
    }
}
