using System;
using System.Collections.Generic;
using Xunit;

namespace ReactorSim.Core.Tests
{
    // Pure maths behind the capture mode, the input telemetry line format and the build-version argument (playtest-2 U1).
    public class CaptureMathTests
    {
        [Fact]
        public void GlyphHeight_ForA04mScreen2mAway70Deg900px40Columns_MatchesTheHandComputedValue()
        {
            // 900 / (2 * 2 m * tan 35 deg) = 321.3 px per metre; 0.4 m -> 128.5 px; 40 columns -> 3.21 px per cell;
            // cap height is 0.7 / 0.6 of the cell width -> 3.75 px.
            Assert.Equal(321.3, CaptureMath.PixelsPerMetre(900, 70, 2.0), 1);
            Assert.Equal(3.21, CaptureMath.CellWidthPx(0.4, 40, 900, 70, 2.0), 2);
            Assert.True(Math.Abs(CaptureMath.GlyphHeightPx(0.4, 40, 900, 70, 2.0) - 3.75) < 0.05);
        }

        [Fact]
        public void GlyphHeight_DegenerateInputs_AreZeroNotNaN()
        {
            Assert.Equal(0.0, CaptureMath.GlyphHeightPx(0.4, 0, 900, 70, 2.0));
            Assert.Equal(0.0, CaptureMath.PixelsPerMetre(900, 70, 0.0));
            Assert.Equal(0.0, CaptureMath.PixelsPerMetre(0, 70, 2.0));
        }

        private static byte[] TwoColourImage(int w, int h, byte[] a, byte[] b)
        {
            var px = new byte[w * h * 4];
            for (int i = 0; i < w * h; i++)
            {
                byte[] c = i % 2 == 0 ? a : b;
                px[i * 4] = c[0]; px[i * 4 + 1] = c[1]; px[i * 4 + 2] = c[2]; px[i * 4 + 3] = 255;
            }
            return px;
        }

        [Fact]
        public void CropContrast_BlackAndWhite_Is21()
        {
            var img = TwoColourImage(20, 20, new byte[] { 0, 0, 0 }, new byte[] { 255, 255, 255 });
            Assert.Equal(21.0, CaptureMath.CropContrast(img, 20, 0, 0, 20, 20), 3);
        }

        [Fact]
        public void CropContrast_KnownGreyOnWhite_MatchesTheWcagValue()
        {
            // #767676 on white is the well-known 4.54:1 pair.
            var img = TwoColourImage(20, 20, new byte[] { 0x76, 0x76, 0x76 }, new byte[] { 255, 255, 255 });
            Assert.Equal(4.54, CaptureMath.CropContrast(img, 20, 0, 0, 20, 20), 2);
        }

        [Fact]
        public void CropContrast_OneColourCrop_IsExactlyOne()
        {
            var img = TwoColourImage(16, 16, new byte[] { 10, 200, 90 }, new byte[] { 10, 200, 90 });
            Assert.Equal(1.0, CaptureMath.CropContrast(img, 16, 2, 2, 8, 8), 6);
        }

        [Fact]
        public void CropContrast_CropOutsideTheImage_ReportsFlat()
        {
            var img = TwoColourImage(8, 8, new byte[] { 0, 0, 0 }, new byte[] { 255, 255, 255 });
            Assert.Equal(1.0, CaptureMath.CropContrast(img, 8, 4, 4, 8, 8));
            Assert.Equal(1.0, CaptureMath.CropContrast(null, 8, 0, 0, 2, 2));
        }

        [Fact]
        public void Percentile_UsesNearestRank_AndEmptyIsZero()
        {
            var v = new List<double>();
            for (int i = 1; i <= 100; i++) v.Add(i);
            Assert.Equal(50.0, CaptureMath.Percentile(v, 50));
            Assert.Equal(95.0, CaptureMath.Percentile(v, 95));
            Assert.Equal(99.0, CaptureMath.Percentile(v, 99));
            Assert.Equal(100.0, CaptureMath.Percentile(v, 100));
            Assert.Equal(1.0, CaptureMath.Percentile(v, 0));
            Assert.Equal(0.0, CaptureMath.Percentile(new List<double>(), 95));
        }

        [Fact]
        public void ParseSize_AcceptsTheCaptureSizes()
        {
            int w, h;
            CaptureMath.ParseSize("1600x900", out w, out h);
            Assert.Equal(1600, w); Assert.Equal(900, h);
            CaptureMath.ParseSize("1920x1080", out w, out h);
            Assert.Equal(1920, w); Assert.Equal(1080, h);
        }

        [Theory]
        [InlineData("1600")]
        [InlineData("1600x")]
        [InlineData("x900")]
        [InlineData("1600X900")]
        [InlineData("-1600x900")]
        [InlineData("axb")]
        [InlineData("10x10")]
        [InlineData("1600x900x2")]
        [InlineData("")]
        [InlineData(null)]
        public void ParseSize_RejectsMalformedSizesWithTheOffendingText(string text)
        {
            int w, h;
            var ex = Assert.Throws<FormatException>(() => CaptureMath.ParseSize(text, out w, out h));
            Assert.Contains("1600x900", ex.Message);
        }

        [Fact]
        public void ParsePoses_AcceptsKnownPosesInOrder_CaseAndSpaceTolerant()
        {
            Assert.Equal(new[] { "seat", "stand", "crt", "pause" }, CaptureMath.ParsePoses("seat, Stand ,crt,pause").ToArray());
        }

        [Fact]
        public void ParsePoses_RejectsAnUnknownPoseNamingItAndTheKnownOnes()
        {
            var ex = Assert.Throws<FormatException>(() => CaptureMath.ParsePoses("seat,bogus"));
            Assert.Contains("bogus", ex.Message);
            Assert.Contains("tablet", ex.Message);
            Assert.Throws<FormatException>(() => CaptureMath.ParsePoses(" , "));
        }

        // ---- input log lines ----

        [Fact]
        public void EveryDragLifecycleLine_RoundTripsThroughTheLogFormat()
        {
            var lines = new[]
            {
                InputLogFormat.Format(1.5, InputLogFormat.Hover, "row", "rod_bank_0_ctl"),
                InputLogFormat.Format(2.0, InputLogFormat.Press, "row", "rod_bank_0_ctl", "kind", "dial"),
                InputLogFormat.Format(2.25, InputLogFormat.DragStart, "row", "rod_bank_0_ctl", "start", InputLogFormat.Num(0.4)),
                InputLogFormat.Format(2.5, InputLogFormat.DragMove, "row", "rod_bank_0_ctl", "value", InputLogFormat.Num(0.5231), "fine", "0"),
                InputLogFormat.Format(3.0, InputLogFormat.DragCommit, "row", "rod_bank_0_ctl", "value", InputLogFormat.Num(0.55), "delta", InputLogFormat.Num(0.15)),
                InputLogFormat.Format(4.0, InputLogFormat.DragCancel, "row", "rod_bank_0_ctl", "reason", "escape"),
                InputLogFormat.Format(5.0, InputLogFormat.PressRejected, "row", "CTL_pzr_level", "reason", "indicator"),
            };
            var names = new[] { "hover", "press", "drag_start", "drag_move", "drag_commit", "drag_cancel", "press_rejected" };
            for (int i = 0; i < lines.Length; i++)
            {
                InputLogEvent e;
                Assert.True(InputLogFormat.TryParse(lines[i], out e), lines[i]);
                Assert.Equal(names[i], e.Name);
            }
            InputLogEvent rej;
            InputLogFormat.TryParse(lines[6], out rej);
            Assert.Equal(5.0, rej.Time, 3);
            Assert.Equal("CTL_pzr_level", rej.Fields["row"]);
            Assert.Equal("indicator", rej.Fields["reason"]);
            InputLogEvent mv;
            InputLogFormat.TryParse(lines[3], out mv);
            Assert.Equal("0.5231", mv.Fields["value"]);
        }

        [Fact]
        public void LogFields_WithQuotesBackslashesAndNewlines_SurviveTheRoundTrip()
        {
            string nasty = "a \"quoted\" path C:\\x\nsecond line\tend";
            InputLogEvent e;
            Assert.True(InputLogFormat.TryParse(InputLogFormat.Format(0.0, "press_rejected", "reason", nasty), out e));
            Assert.Equal(nasty, e.Fields["reason"]);
        }

        [Theory]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("{\"ev\":\"press\"}")]
        [InlineData("{\"t\":1.0}")]
        [InlineData("{\"t\":abc,\"ev\":\"press\"}")]
        [InlineData("{\"t\":1.0,\"ev\":\"press\"")]
        public void MalformedLogLines_AreRejected(string line)
        {
            InputLogEvent e;
            Assert.False(InputLogFormat.TryParse(line, out e));
            Assert.Null(e);
        }

        [Fact]
        public void Num_WritesNaNAsAWord_NotAnInvalidNumber()
        {
            Assert.Equal("nan", InputLogFormat.Num(double.NaN));
            Assert.Equal("0.1235", InputLogFormat.Num(0.12345));
        }

        // ---- build version argument ----

        [Fact]
        public void BuildVersion_TakesTheArgumentWhenPresent()
        {
            string source;
            string v = BuildVersionArgs.Resolve(new[] { "Unity.exe", "-batchmode", "-buildVersion", "1.2.3", "-quit" }, "1.0.0", out source);
            Assert.Equal("1.2.3", v);
            Assert.Equal("arg", source);
        }

        [Fact]
        public void BuildVersion_FallsBackWhenTheArgumentIsMissing()
        {
            string source;
            Assert.Equal("1.0.0", BuildVersionArgs.Resolve(new[] { "Unity.exe", "-batchmode" }, "1.0.0", out source));
            Assert.Equal("fallback", source);
            Assert.Equal("1.0.0", BuildVersionArgs.Resolve(null, "1.0.0", out source));
            Assert.Equal("fallback", source);
        }

        [Theory]
        [InlineData("v1.2.3")]
        [InlineData("1.2")]
        [InlineData("1.2.3.4")]
        [InlineData("1.2.x")]
        [InlineData("1.2.3\n")]
        [InlineData("1.2.3-rc1")]
        [InlineData("")]
        [InlineData("-quit")]
        public void BuildVersion_RejectsAMalformedValue(string value)
        {
            string source;
            var ex = Assert.Throws<FormatException>(() => BuildVersionArgs.Resolve(new[] { "-buildVersion", value }, "1.0.0", out source));
            Assert.Contains("-buildVersion", ex.Message);
        }

        [Fact]
        public void BuildVersion_FlagWithNoValue_IsRejected()
        {
            string source;
            Assert.Throws<FormatException>(() => BuildVersionArgs.Resolve(new[] { "-quit", "-buildVersion" }, "1.0.0", out source));
        }

        [Theory]
        [InlineData("1.2", true)]
        [InlineData("1.2.3", true)]
        [InlineData("1.2.3\n", false)]
        [InlineData("1.2\n", false)]
        [InlineData("1", false)]
        [InlineData("1.2.3.4", false)]
        [InlineData("", false)]
        public void BuildVersion_FallbackBase_AcceptsXYOrXYZ(string value, bool ok)
        {
            string source;
            if (ok) Assert.Equal(value, BuildVersionArgs.Resolve(null, value, out source));
            else Assert.Throws<FormatException>(() => BuildVersionArgs.Resolve(null, value, out source));
        }

        [Fact]
        public void InputLogNum_IsInvariantUnderACommaDecimalCulture()
        {
            var saved = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = CommaCulture();
                Assert.Equal("0.5231", InputLogFormat.Num(0.5231));
            }
            finally { System.Threading.Thread.CurrentThread.CurrentCulture = saved; }
        }

        internal static System.Globalization.CultureInfo CommaCulture()
        {
            var c = (System.Globalization.CultureInfo)System.Globalization.CultureInfo.InvariantCulture.Clone();
            c.NumberFormat.NumberDecimalSeparator = ",";
            c.NumberFormat.NumberGroupSeparator = ".";
            return c;
        }
    }
}
