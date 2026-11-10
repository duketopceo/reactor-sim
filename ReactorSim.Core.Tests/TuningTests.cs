using System;
using System.IO;
using Xunit;

namespace ReactorSim.Tests
{
    public class TuningTests
    {
        [Fact]
        public void Parse_EmptyObject_GivesDefaults()
        {
            var t = Tuning.Parse("{}");
            var d = new Tuning();
            Assert.Equal(d.DefaultScale, t.DefaultScale);
            Assert.Equal(d.AutoSlowScale, t.AutoSlowScale);
            Assert.Equal(d.InteractiveTickSeconds, t.InteractiveTickSeconds);
        }

        [Fact]
        public void Parse_MissingKeysFallBackToDefaults_PresentKeysOverride()
        {
            var t = Tuning.Parse("{ \"defaultScale\": 7.5,\n \"autoSlowHoldSeconds\" : 12 }");
            Assert.Equal(7.5, t.DefaultScale);
            Assert.Equal(12.0, t.AutoSlowHoldSeconds);
            Assert.Equal(new Tuning().AutoSlowScale, t.AutoSlowScale);
        }

        [Fact]
        public void Parse_UnknownKey_ThrowsNamedError()
        {
            var ex = Assert.Throws<TuningException>(() => Tuning.Parse("{\"defaultScale\":5,\"defaultScael\":3}"));
            Assert.Contains("defaultScael", ex.Message);
        }

        [Theory]
        [InlineData("")]
        [InlineData("[1,2]")]
        [InlineData("{\"defaultScale\":\"fast\"}")]
        [InlineData("{\"defaultScale\":5")]
        [InlineData("{\"defaultScale\":5,\"defaultScale\":6}")]
        public void Parse_Malformed_Throws(string json)
        {
            Assert.Throws<TuningException>(() => Tuning.Parse(json));
        }

        [Theory]
        [InlineData("{\"minScale\":10,\"maxScale\":5}")]
        [InlineData("{\"defaultScale\":1000}")]
        [InlineData("{\"interactiveTickSeconds\":0}")]
        [InlineData("{\"autoSlowScale\":0}")]
        public void Parse_InconsistentValues_Throw(string json)
        {
            Assert.Throws<TuningException>(() => Tuning.Parse(json));
        }

        [Fact]
        public void LoadFromFile_MissingFile_FallsBackToDefaults()
        {
            var t = Tuning.LoadFromFile(Path.Combine(Path.GetTempPath(), "no-such-tuning-" + Guid.NewGuid() + ".json"));
            Assert.Equal(new Tuning().DefaultScale, t.DefaultScale);
        }

        [Fact]
        public void ShippedTuningJson_ParsesAndMatchesInCodeDefaults()
        {
            string path = TestRepo.Find("ReactorSim.Unity", "Assets", "StreamingAssets", "tuning.json");
            var shipped = Tuning.LoadFromFile(path);
            var d = new Tuning();
            Assert.Equal(d.DefaultScale, shipped.DefaultScale);
            Assert.Equal(d.MinScale, shipped.MinScale);
            Assert.Equal(d.MaxScale, shipped.MaxScale);
            Assert.Equal(d.DevMaxScale, shipped.DevMaxScale);
            Assert.Equal(d.AutoSlowScale, shipped.AutoSlowScale);
            Assert.Equal(d.AutoSlowPeriodSeconds, shipped.AutoSlowPeriodSeconds);
            Assert.Equal(d.AutoSlowHoldSeconds, shipped.AutoSlowHoldSeconds);
            Assert.Equal(d.InteractiveTickSeconds, shipped.InteractiveTickSeconds);
            Assert.Equal(d.MaxTicksPerAdvance, shipped.MaxTicksPerAdvance);
        }
    }
}
