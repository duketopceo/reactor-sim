using Xunit;

namespace ReactorSim.Core.Tests
{
    // The cursor hold count and deferred pointer restore behind CursorPin (playtest-2 review fixes).
    public class CursorLockModelTests
    {
        [Fact]
        public void LookThenDrag_KeepsTheCursorLockedUntilBothAreReleased()
        {
            var m = new CursorLockModel();
            Assert.True(m.Acquire());   // RMB look
            Assert.False(m.Acquire());  // LMB on a control: second hold, no second lock
            Assert.False(m.Release(10)); // controller yields the look because a drag is active
            Assert.True(m.Locked);
            Assert.True(m.Release(20)); // drag ends
            Assert.False(m.Locked);
        }

        [Fact]
        public void RestoreIsScheduledTwoFramesAfterTheLastRelease_AndFiresOnce()
        {
            var m = new CursorLockModel();
            m.Acquire();
            m.Release(5);
            Assert.False(m.RestoreDue(6));
            Assert.True(m.RestoreDue(7));
            Assert.False(m.RestoreDue(8));
        }

        [Fact]
        public void CancelRestore_DropsAPendingRestore()
        {
            var m = new CursorLockModel();
            m.Acquire();
            m.Release(5);
            Assert.True(m.RestorePending);
            m.CancelRestore();
            Assert.False(m.RestoreDue(100));
        }

        [Fact]
        public void AcquireBeforeTheRestoreFalls_CancelsIt()
        {
            var m = new CursorLockModel();
            m.Acquire();
            m.Release(5);
            m.Acquire();
            Assert.False(m.RestoreDue(100));
        }

        [Fact]
        public void ReleaseWithoutAHold_IsIgnored()
        {
            var m = new CursorLockModel();
            Assert.False(m.Release(1));
            Assert.False(m.RestorePending);
            Assert.True(m.Acquire());
        }
    }
}
