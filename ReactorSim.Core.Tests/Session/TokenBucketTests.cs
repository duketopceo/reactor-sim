using ReactorSim.Session;
using Xunit;

namespace ReactorSim.Tests
{
    public class TokenBucketTests
    {
        private static void DrainAt(TokenBucket b, double now, int expected)
        {
            for (int i = 0; i < expected; i++)
                Assert.True(b.TryTake(now), "take " + i);
            Assert.False(b.TryTake(now));
        }

        [Fact]
        public void StartsFullAndSpendsOneTokenPerTake()
        {
            var b = new TokenBucket(10, 20, 0);
            for (int i = 0; i < 20; i++)
                Assert.True(b.TryTake(0), "take " + i);
            Assert.False(b.TryTake(0));
        }

        [Fact]
        public void RefillsAtTheConfiguredRate()
        {
            var b = new TokenBucket(10, 20, 0);
            DrainAt(b, 0, 20);
            Assert.False(b.TryTake(0.05));
            Assert.True(b.TryTake(0.1));
            Assert.False(b.TryTake(0.1));
            Assert.True(b.TryTake(0.2));
        }

        [Fact]
        public void RefillIsCappedAtTheBurstAfterALargeClockJump()
        {
            var b = new TokenBucket(10, 20, 0);
            DrainAt(b, 0, 20);
            DrainAt(b, 1000, 20);
        }

        [Fact]
        public void EarlierTimeGivesNoRefillAndDoesNotRewindTheClock()
        {
            var b = new TokenBucket(10, 20, 10);
            DrainAt(b, 10, 20);
            Assert.False(b.TryTake(5));
            Assert.False(b.TryTake(10.05));
            Assert.True(b.TryTake(10.1));
        }
    }
}
