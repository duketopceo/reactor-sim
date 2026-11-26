using System;
using System.Globalization;
using System.Text;
using System.Threading;
using ReactorSim.Session;
using Xunit;

namespace ReactorSim.Tests
{
    public class SessionJsonTests
    {
        [Fact]
        public void ParsesEveryValueKind()
        {
            var r = SessionJson.Parse(" {\"a\":[1,2.5,-3e2],\"b\":\"x\",\"c\":true,\"d\":false,\"e\":null,\"f\":{}} ");
            Assert.True(r.Ok);
            var o = r.Value;
            Assert.Equal(JsonKind.Object, o.Kind);
            Assert.Equal(6, o.Members.Count);
            Assert.Equal("a", o.Members[0].Key); // ordered keys
            var a = o.Get("a");
            Assert.Equal(JsonKind.Array, a.Kind);
            Assert.Equal(3, a.Items.Count);
            Assert.Equal(1.0, a.Items[0].Number);
            Assert.Equal(2.5, a.Items[1].Number);
            Assert.Equal(-300.0, a.Items[2].Number);
            Assert.Equal("x", o.Get("b").Text);
            Assert.True(o.Get("c").Bool);
            Assert.False(o.Get("d").Bool);
            Assert.Equal(JsonKind.Null, o.Get("e").Kind);
            Assert.Equal(JsonKind.Object, o.Get("f").Kind);
            Assert.Null(o.Get("missing"));
        }

        [Fact]
        public void ParsesEscapesAndSurrogatePairs()
        {
            var r = SessionJson.Parse("\"a\\\"b\\\\c\\/d\\n\\t\\u0041\\ud83d\\ude00\"");
            Assert.True(r.Ok);
            Assert.Equal("a\"b\\c/d\n\tA\U0001F600", r.Value.Text);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("{")]
        [InlineData("{\"a\":1")]
        [InlineData("{\"a\":}")]
        [InlineData("{\"a\" 1}")]
        [InlineData("{a:1}")]
        [InlineData("{\"a\":1,}")]
        [InlineData("[1,]")]
        [InlineData("[1 2]")]
        [InlineData("{\"a\":1} x")]
        [InlineData("{\"a\":1}{\"b\":2}")]
        [InlineData("{\"a\":1,\"a\":2}")]
        [InlineData("{\"a\":NaN}")]
        [InlineData("{\"a\":Infinity}")]
        [InlineData("{\"a\":-Infinity}")]
        [InlineData("{\"a\":1e999}")]
        [InlineData("{\"a\":01}")]
        [InlineData("{\"a\":-}")]
        [InlineData("{\"a\":+1}")]
        [InlineData("{\"a\":.5}")]
        [InlineData("{\"a\":1.}")]
        [InlineData("{\"a\":1e}")]
        [InlineData("{\"a\":0x10}")]
        [InlineData("{\"a\":tru}")]
        [InlineData("{\"a\":True}")]
        [InlineData("{\"a\":'x'}")]
        [InlineData("{\"a\":\"bad\\q\"}")]
        [InlineData("{\"a\":\"bad\\u12\"}")]
        [InlineData("{\"a\":\"bad\\uZZZZ\"}")]
        [InlineData("{\"a\":\"unterminated}")]
        [InlineData("{\"a\":\"\\ud83d\"}")]
        [InlineData("{\"a\":\"\\ude00\"}")]
        public void MalformedInputIsRejectedWithoutThrowing(string text)
        {
            var r = SessionJson.Parse(text);
            Assert.False(r.Ok);
            Assert.Null(r.Value);
            Assert.False(string.IsNullOrEmpty(r.Error));
        }

        [Fact]
        public void NullInputIsRejected()
        {
            Assert.False(SessionJson.Parse(null).Ok);
        }

        [Fact]
        public void UnescapedControlCharacterInStringIsRejected()
        {
            Assert.False(SessionJson.Parse("\"a\u0001b\"").Ok);
            Assert.False(SessionJson.Parse("\"a\nb\"").Ok);
            Assert.False(SessionJson.Parse("\"a\tb\"").Ok);
        }

        [Fact]
        public void DuplicateKeysAreRejectedButSameKeyInSeparateObjectsIsFine()
        {
            Assert.False(SessionJson.Parse("{\"a\":1,\"a\":1}").Ok);
            Assert.False(SessionJson.Parse("{\"x\":{\"a\":1,\"a\":2}}").Ok);
            Assert.True(SessionJson.Parse("{\"x\":{\"a\":1},\"y\":{\"a\":2}}").Ok);
        }

        [Fact]
        public void DepthEightIsAcceptedAndDepthNineIsRejected()
        {
            string eight = new string('[', 8) + new string(']', 8);
            string nine = new string('[', 9) + new string(']', 9);
            Assert.True(SessionJson.Parse(eight).Ok);
            Assert.False(SessionJson.Parse(nine).Ok);
        }

        [Fact]
        public void ObjectDepthCountsTheRoot()
        {
            string o8 = "{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{\"a\":{}}}}}}}}"; // 8 nested objects
            string o9 = "{\"a\":" + o8 + "}";
            Assert.True(SessionJson.Parse(o8).Ok);
            Assert.False(SessionJson.Parse(o9).Ok);
        }

        [Fact]
        public void HugeNestingDoesNotOverflowTheStack()
        {
            var r = SessionJson.Parse(new string('[', 200000));
            Assert.False(r.Ok);
        }

        [Fact]
        public void NumbersAreFinite()
        {
            var r = SessionJson.Parse("[1.7976931348623157e308,-0,0.0,1E2,1e-2,5e-324]");
            Assert.True(r.Ok);
            foreach (var v in r.Value.Items) Assert.False(double.IsInfinity(v.Number) || double.IsNaN(v.Number));
        }

        [Fact]
        public void WriterEscapesQuotesBackslashAndControlCharacters()
        {
            var sb = new StringBuilder();
            SessionJson.AppendString(sb, "q\"b\\n\n\r\t\u0001\u001f ok \u00e9 \U0001F600");
            string s = sb.ToString();
            Assert.Equal("\"q\\\"b\\\\n\\u000a\\u000d\\u0009\\u0001\\u001f ok \u00e9 \U0001F600\"", s);
            foreach (char c in s) Assert.True(c >= 0x20);
        }

        [Fact]
        public void StringsRoundTripThroughWriterAndParser()
        {
            string original = "quote\" back\\slash ctrl\u0000\u0007\u001f tab\t nl\n cr\r unicode \u00e9\u4e2d \U0001F600 end";
            var sb = new StringBuilder();
            SessionJson.AppendString(sb, original);
            var r = SessionJson.Parse(sb.ToString());
            Assert.True(r.Ok);
            Assert.Equal(original, r.Value.Text);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(1.0)]
        [InlineData(-1.5)]
        [InlineData(0.1)]
        [InlineData(0.123456789012345678)]
        [InlineData(1.0 / 3.0)]
        [InlineData(1e-300)]
        [InlineData(1.7976931348623157e308)]
        [InlineData(5e-324)]
        [InlineData(123456789012345.67)]
        public void NumbersRoundTripExactlyUnderANonInvariantCulture(double value)
        {
            var saved = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                var sb = new StringBuilder();
                SessionJson.AppendNumber(sb, value);
                string text = sb.ToString();
                Assert.DoesNotContain(",", text);
                var r = SessionJson.Parse(text);
                Assert.True(r.Ok);
                Assert.Equal(value, r.Value.Number);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = saved;
            }
        }

        [Fact]
        public void WriterRefusesNonFiniteNumbers()
        {
            Assert.Throws<ArgumentException>(() => SessionJson.AppendNumber(new StringBuilder(), double.NaN));
            Assert.Throws<ArgumentException>(() => SessionJson.AppendNumber(new StringBuilder(), double.PositiveInfinity));
        }

        [Fact]
        public void ValueTreeWritesAndReparsesEqual()
        {
            var tree = JsonValue.NewObject(
                new System.Collections.Generic.KeyValuePair<string, JsonValue>("n", JsonValue.NewNumber(2.5)),
                new System.Collections.Generic.KeyValuePair<string, JsonValue>("s", JsonValue.NewString("a\"b")),
                new System.Collections.Generic.KeyValuePair<string, JsonValue>("b", JsonValue.NewBool(true)),
                new System.Collections.Generic.KeyValuePair<string, JsonValue>("z", JsonValue.NewNull()),
                new System.Collections.Generic.KeyValuePair<string, JsonValue>("a", JsonValue.NewArray(JsonValue.NewNumber(1), JsonValue.NewString("x"))));
            string text = SessionJson.Write(tree);
            Assert.Equal("{\"n\":2.5,\"s\":\"a\\\"b\",\"b\":true,\"z\":null,\"a\":[1,\"x\"]}", text);
            var back = SessionJson.Parse(text);
            Assert.True(back.Ok);
            Assert.Equal(text, SessionJson.Write(back.Value));
        }

        [Theory]
        [InlineData("1.0000000000000001")]
        [InlineData("1.0")]
        [InlineData("1e2")]
        [InlineData("1E0")]
        [InlineData("-0.0")]
        [InlineData("9007199254740991.0")]
        public void TryWholeNumber_RejectsFractionAndExponentTokens(string token)
        {
            var r = SessionJson.Parse(token);
            Assert.True(r.Ok, r.Error);
            long n;
            Assert.False(SessionJson.TryWholeNumber(r.Value, out n));
        }

        [Theory]
        [InlineData("0", 0L)]
        [InlineData("-0", 0L)]
        [InlineData("1", 1L)]
        [InlineData("9007199254740991", 9007199254740991L)]
        [InlineData("-9007199254740991", -9007199254740991L)]
        public void TryWholeNumber_AcceptsPlainIntegerTokensWithinBound(string token, long expected)
        {
            var r = SessionJson.Parse(token);
            Assert.True(r.Ok, r.Error);
            long n;
            Assert.True(SessionJson.TryWholeNumber(r.Value, out n));
            Assert.Equal(expected, n);
        }

        [Fact]
        public void TryWholeNumber_RejectsIntegerTokenBeyondBound()
        {
            long n;
            Assert.False(SessionJson.TryWholeNumber(SessionJson.Parse("9007199254740993").Value, out n));
        }

        [Fact]
        public void TryWholeNumber_AcceptsProgrammaticIntegerNumbers()
        {
            long n;
            Assert.True(SessionJson.TryWholeNumber(JsonValue.NewNumber(7), out n));
            Assert.Equal(7L, n);
        }
    }
}
