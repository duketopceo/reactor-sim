using ReactorSim.Session;
using Xunit;

namespace ReactorSim.Tests
{
    public class TokenBucketTests
    {
        [Fact]
        public void StartsFullAndSpendsOneTokenPerTake()
        {
            var b = new TokenBucket(10, 20, 0);
            int taken = 0;
            while (b.TryTake(0)) taken++;
            Assert.Equal(20, taken);
        }

        [Fact]
        public void RefillsAtTheConfiguredRate()
        {
            var b = new TokenBucket(10, 20, 0);
            while (b.TryTake(0)) { }
            Assert.False(b.TryTake(0.05));
            Assert.True(b.TryTake(0.1));
            Assert.False(b.TryTake(0.1));
            Assert.True(b.TryTake(0.2));
        }

        [Fact]
        public void RefillIsCappedAtTheBurstAfterALargeClockJump()
        {
            var b = new TokenBucket(10, 20, 0);
            while (b.TryTake(0)) { }
            int taken = 0;
            while (b.TryTake(1000)) taken++;
            Assert.Equal(20, taken);
        }

        [Fact]
        public void EarlierTimeGivesNoRefillAndDoesNotRewindTheClock()
        {
            var b = new TokenBucket(10, 20, 10);
            while (b.TryTake(10)) { }
            Assert.False(b.TryTake(5));
            Assert.False(b.TryTake(10.05));
            Assert.True(b.TryTake(10.1));
        }
    }
}
