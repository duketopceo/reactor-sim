using ReactorSim.Ui;
using Xunit;

namespace ReactorSim.Core.Tests
{
    public class TooltipClampTests
    {
        private const float W = 1920f, H = 1080f, Gap = 16f, Edge = 4f;

        private static void Place(float cx, float cy, float bw, float bh, out float left, out float top)
        {
            TooltipClamp.Place(cx, cy, bw, bh, W, H, Gap, Edge, out left, out top);
        }

        [Fact]
        public void NearTheCentre_HangsBelowAndRightOfTheCursor()
        {
            Place(500f, 600f, 300f, 60f, out float left, out float top);
            Assert.Equal(516f, left);
            Assert.Equal(584f, top);
        }

        [Fact]
        public void NearTheRightEdge_FlipsLeftOfTheCursorAndStaysOnScreen()
        {
            Place(1800f, 600f, 420f, 60f, out float left, out float top);
            Assert.True(left + 420f <= W - Edge);
            Assert.True(left < 1800f);
        }

        [Fact]
        public void BoxWiderThanTheScreen_PinsToTheLeftEdge()
        {
            Place(100f, 500f, 3000f, 60f, out float left, out float top);
            Assert.Equal(Edge, left);
        }

        [Fact]
        public void NearTheBottomAndTop_StaysFullyInside()
        {
            Place(300f, 10f, 300f, 80f, out float left, out float top);
            Assert.True(top - 80f >= Edge - 1e-3f);
            Place(300f, 1075f, 300f, 80f, out left, out top);
            Assert.True(top <= H - Edge + 1e-3f);
        }

        [Fact]
        public void EveryCursorPosition_KeepsABoxThatFitsFullyOnScreen()
        {
            for (float x = 0f; x <= W; x += 97f)
                for (float y = 0f; y <= H; y += 61f)
                {
                    Place(x, y, 460f, 120f, out float left, out float top);
                    Assert.True(left >= Edge - 1e-3f && left + 460f <= W - Edge + 1e-3f, "x " + x + " left " + left);
                    Assert.True(top <= H - Edge + 1e-3f && top - 120f >= Edge - 1e-3f, "y " + y + " top " + top);
                }
        }
    }
}
