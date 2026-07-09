using AudioDual.Core.Buffering;
using Xunit;

namespace AudioDual.Core.Tests.Buffering
{
    public class SpscRingBufferTests
    {
        [Fact]
        public void Read_AfterWrite_ReturnsSameBytesInOrder()
        {
            var buffer = new SpscRingBuffer(capacityInBytes: 16, RingBufferOverflowPolicy.DropOldest);
            byte[] written = { 1, 2, 3, 4, 5 };

            int bytesWritten = buffer.Write(written, 0, written.Length);

            var readBack = new byte[written.Length];
            int bytesRead = buffer.Read(readBack, 0, readBack.Length);

            Assert.Equal(written.Length, bytesWritten);
            Assert.Equal(written.Length, bytesRead);
            Assert.Equal(written, readBack);
        }

        [Fact]
        public void Read_WithMoreRequestedThanAvailable_ReturnsOnlyWhatIsBuffered()
        {
            var buffer = new SpscRingBuffer(capacityInBytes: 16, RingBufferOverflowPolicy.DropOldest);
            byte[] written = { 9, 8, 7 };
            buffer.Write(written, 0, written.Length);

            var readBack = new byte[10];
            int bytesRead = buffer.Read(readBack, 0, readBack.Length);

            Assert.Equal(written.Length, bytesRead);
            Assert.Equal(9, readBack[0]);
            Assert.Equal(8, readBack[1]);
            Assert.Equal(7, readBack[2]);
        }

        [Fact]
        public void Write_WrappingAroundCapacity_PreservesCorrectOrderOnRead()
        {
            var buffer = new SpscRingBuffer(capacityInBytes: 8, RingBufferOverflowPolicy.DropOldest);

            // Fill, drain most of it, then write again so the internal write position
            // wraps past the end of the backing array — this is the case a naive
            // non-circular implementation gets wrong.
            buffer.Write(new byte[] { 1, 2, 3, 4, 5, 6 }, 0, 6);

            var firstRead = new byte[4];
            buffer.Read(firstRead, 0, 4);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, firstRead);

            buffer.Write(new byte[] { 7, 8, 9, 10 }, 0, 4);

            var secondRead = new byte[6];
            int bytesRead = buffer.Read(secondRead, 0, 6);

            Assert.Equal(6, bytesRead);
            Assert.Equal(new byte[] { 5, 6, 7, 8, 9, 10 }, secondRead);
        }

        [Fact]
        public void Write_ExceedingCapacityWithDropOldestPolicy_KeepsMostRecentData()
        {
            var buffer = new SpscRingBuffer(capacityInBytes: 4, RingBufferOverflowPolicy.DropOldest);

            buffer.Write(new byte[] { 1, 2, 3, 4 }, 0, 4);
            // Buffer is now full. Writing 2 more bytes must drop the 2 oldest bytes
            // (1, 2) rather than reject the new data or overwrite the newest bytes.
            buffer.Write(new byte[] { 5, 6 }, 0, 2);

            var readBack = new byte[4];
            int bytesRead = buffer.Read(readBack, 0, 4);

            Assert.Equal(4, bytesRead);
            Assert.Equal(new byte[] { 3, 4, 5, 6 }, readBack);
        }

        [Fact]
        public void Write_ExceedingCapacityWithDropNewestPolicy_KeepsExistingDataAndTruncatesIncoming()
        {
            var buffer = new SpscRingBuffer(capacityInBytes: 4, RingBufferOverflowPolicy.DropNewest);

            buffer.Write(new byte[] { 1, 2, 3, 4 }, 0, 4);
            int bytesWritten = buffer.Write(new byte[] { 5, 6 }, 0, 2);

            var readBack = new byte[4];
            int bytesRead = buffer.Read(readBack, 0, 4);

            Assert.Equal(0, bytesWritten);
            Assert.Equal(4, bytesRead);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, readBack);
        }

        [Fact]
        public void AvailableBytes_ReflectsBufferedDataAfterWritesAndReads()
        {
            var buffer = new SpscRingBuffer(capacityInBytes: 16, RingBufferOverflowPolicy.DropOldest);

            buffer.Write(new byte[] { 1, 2, 3, 4, 5 }, 0, 5);
            Assert.Equal(5, buffer.AvailableBytes);

            buffer.Read(new byte[2], 0, 2);
            Assert.Equal(3, buffer.AvailableBytes);
        }

        [Fact]
        public void Clear_RemovesAllBufferedData()
        {
            var buffer = new SpscRingBuffer(capacityInBytes: 16, RingBufferOverflowPolicy.DropOldest);
            buffer.Write(new byte[] { 1, 2, 3 }, 0, 3);

            buffer.Clear();

            Assert.Equal(0, buffer.AvailableBytes);
            var readBack = new byte[3];
            Assert.Equal(0, buffer.Read(readBack, 0, 3));
        }

        [Fact]
        public void Constructor_WithNonPositiveCapacity_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpscRingBuffer(0, RingBufferOverflowPolicy.DropOldest));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpscRingBuffer(-1, RingBufferOverflowPolicy.DropOldest));
        }
    }
}
