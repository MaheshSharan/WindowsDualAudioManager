using AudioDual.Core.Routing;
using Xunit;

namespace AudioDual.Core.Tests.Routing
{
    public class AudioRouterOptionsTests
    {
        [Theory]
        [InlineData(1, 15)]
        [InlineData(15, 15)]
        [InlineData(25, 25)]
        [InlineData(500, 500)]
        [InlineData(10000, 500)]
        public void TargetLatencyMs_IsClampedToSupportedRange(int requested, int expected)
        {
            var options = new AudioRouterOptions
            {
                TargetLatencyMs = requested
            };

            Assert.Equal(expected, options.TargetLatencyMs);
        }

        [Fact]
        public void PreferExclusiveModeOutput_DefaultsToFalse()
        {
            var options = new AudioRouterOptions();

            Assert.False(options.PreferExclusiveModeOutput);
        }

        [Fact]
        public void OverflowPolicy_DefaultsToDropOldest()
        {
            var options = new AudioRouterOptions();

            Assert.Equal(RingBufferOverflowPolicyOption.DropOldest, options.OverflowPolicy);
        }
    }
}
