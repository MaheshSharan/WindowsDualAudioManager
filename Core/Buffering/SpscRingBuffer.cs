namespace AudioDual.Core.Buffering
{
    /// <summary>
    /// A fixed-capacity byte ring buffer sized for exactly one producer thread (the
    /// WASAPI capture callback) and one consumer thread (a WASAPI render callback)
    /// per instance. This is the entire buffer between capture and playback for one
    /// output device — it replaces the old pipeline's queue, "smoothing" circular
    /// buffer, and BufferedWaveProvider, which stacked three independently-sized
    /// buffers on top of each other for no benefit over one correctly-sized one.
    ///
    /// Design note: this uses a single lock rather than being truly lock-free.
    /// A genuinely lock-free SPSC ring buffer is possible, but its correctness
    /// depends on subtle memory-ordering guarantees that are difficult to verify
    /// without a test environment that can actually run and stress the code under
    /// contention. A short-held lock around a plain array is trivially correct,
    /// and at the sizes and call frequencies involved here (tens of thousands of
    /// calls per second, each touching a lock for a few dozen nanoseconds) it does
    /// not meaningfully affect latency. If profiling on real hardware later shows
    /// this lock is actually a bottleneck, it can be replaced with a verified
    /// lock-free implementation then.
    /// </summary>
    public sealed class SpscRingBuffer
    {
        private readonly byte[] _buffer;
        private readonly object _gate = new();
        private readonly RingBufferOverflowPolicy _overflowPolicy;
        private int _writePosition;
        private int _readPosition;
        private int _availableBytes;

        public SpscRingBuffer(int capacityInBytes, RingBufferOverflowPolicy overflowPolicy)
        {
            if (capacityInBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacityInBytes), capacityInBytes, "Ring buffer capacity must be positive.");
            }

            _buffer = new byte[capacityInBytes];
            _overflowPolicy = overflowPolicy;
        }

        public int CapacityInBytes => _buffer.Length;

        /// <summary>
        /// Bytes currently buffered and available to read. Read outside the lock for
        /// telemetry/monitoring purposes; the value may be a few bytes stale by the
        /// time it's acted on, which is expected and harmless for reporting latency.
        /// </summary>
        public int AvailableBytes
        {
            get
            {
                lock (_gate)
                {
                    return _availableBytes;
                }
            }
        }

        /// <summary>
        /// Writes as much of <paramref name="data"/> as the overflow policy allows.
        /// Returns the number of bytes actually written, which is always
        /// <paramref name="count"/> unless the buffer was full and the policy is
        /// DropNewest, in which case it may be less.
        /// </summary>
        public int Write(byte[] data, int offset, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            lock (_gate)
            {
                int freeBytes = _buffer.Length - _availableBytes;

                if (count > freeBytes)
                {
                    if (_overflowPolicy == RingBufferOverflowPolicy.DropNewest)
                    {
                        count = freeBytes;
                    }
                    else
                    {
                        int bytesToDrop = count - freeBytes;
                        AdvanceReadPositionForOverflow(bytesToDrop);
                    }
                }

                WriteInternal(data, offset, count);
                return count;
            }
        }

        /// <summary>
        /// Reads up to <paramref name="count"/> bytes into <paramref name="destination"/>.
        /// Returns the number of bytes actually read, which is less than
        /// <paramref name="count"/> when the buffer holds less data than requested —
        /// callers are expected to fill the remainder with silence themselves, since
        /// only the caller knows whether that shortfall is worth reporting as an
        /// underrun.
        /// </summary>
        public int Read(byte[] destination, int offset, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            lock (_gate)
            {
                int bytesToRead = Math.Min(count, _availableBytes);
                int totalBytesRead = 0;

                while (totalBytesRead < bytesToRead)
                {
                    int contiguousBytes = Math.Min(bytesToRead - totalBytesRead, _buffer.Length - _readPosition);
                    Array.Copy(_buffer, _readPosition, destination, offset + totalBytesRead, contiguousBytes);

                    _readPosition = (_readPosition + contiguousBytes) % _buffer.Length;
                    totalBytesRead += contiguousBytes;
                }

                _availableBytes -= totalBytesRead;
                return totalBytesRead;
            }
        }

        /// <summary>
        /// Discards all buffered data. Used when an output channel is (re)started so
        /// stale audio from before a pause doesn't play back unexpectedly.
        /// </summary>
        public void Clear()
        {
            lock (_gate)
            {
                _writePosition = 0;
                _readPosition = 0;
                _availableBytes = 0;
            }
        }

        private void WriteInternal(byte[] data, int offset, int count)
        {
            int totalBytesWritten = 0;

            while (totalBytesWritten < count)
            {
                int contiguousBytes = Math.Min(count - totalBytesWritten, _buffer.Length - _writePosition);
                Array.Copy(data, offset + totalBytesWritten, _buffer, _writePosition, contiguousBytes);

                _writePosition = (_writePosition + contiguousBytes) % _buffer.Length;
                totalBytesWritten += contiguousBytes;
            }

            _availableBytes += totalBytesWritten;
        }

        private void AdvanceReadPositionForOverflow(int bytesToDrop)
        {
            bytesToDrop = Math.Min(bytesToDrop, _availableBytes);
            _readPosition = (_readPosition + bytesToDrop) % _buffer.Length;
            _availableBytes -= bytesToDrop;
        }
    }
}
