using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using ReactorSim.Session;
using Xunit;

namespace ReactorSim.Tests
{
    public class SessionEnvelopeTests
    {
        private static readonly string[] AllTypes =
        {
            SessionProtocol.TypeHello, SessionProtocol.TypeWelcome, SessionProtocol.TypeReject,
            SessionProtocol.TypeWorld, SessionProtocol.TypeIntent, SessionProtocol.TypeIntentResult,
            SessionProtocol.TypeCrewUpdate, SessionProtocol.TypeHeartbeat, SessionProtocol.TypeGoodbye
        };

        public static TheoryData<string> Types()
        {
            var d = new TheoryData<string>();
            foreach (var t in AllTypes) d.Add(t);
            return d;
        }

        private static JsonValue SampleBody()
        {
            return JsonValue.NewObject(
                new KeyValuePair<string, JsonValue>("name", JsonValue.NewString("Op \"A\" é")),
                new KeyValuePair<string, JsonValue>("x", JsonValue.NewNumber(0.1)));
        }

        private static SessionEnvelopeResult Decode(string line) { return SessionEnvelope.Decode(line); }

        private static void AssertError(string line, SessionErrorCode expected)
        {
            var r = Decode(line);
            Assert.False(r.Ok);
            Assert.Null(r.Envelope);
            Assert.Equal(expected, r.Error);
        }

        [Fact]
        public void ErrorCodeValuesArePinned()
        {
            var expected = new Dictionary<SessionErrorCode, int>
            {
                { SessionErrorCode.None, 0 }, { SessionErrorCode.UnsupportedProtocol, 1 },
                { SessionErrorCode.UnsupportedSchema, 2 }, { SessionErrorCode.BadJoinCode, 3 },
                { SessionErrorCode.SessionFull, 4 }, { SessionErrorCode.HandshakeTimeout, 5 },
                { SessionErrorCode.TokenInvalid, 6 }, { SessionErrorCode.TokenExpired, 7 },
                { SessionErrorCode.SessionUnknown, 8 }, { SessionErrorCode.Superseded, 9 },
                { SessionErrorCode.ProtocolViolation, 10 }, { SessionErrorCode.FrameTooLarge, 11 },
                { SessionErrorCode.RateLimited, 12 }, { SessionErrorCode.TooManyConnections, 13 },
                { SessionErrorCode.NotJoined, 14 }, { SessionErrorCode.Duplicate, 15 },
                { SessionErrorCode.InvalidIntent, 16 }, { SessionErrorCode.NotPermitted, 17 },
                { SessionErrorCode.NonFiniteValue, 18 }, { SessionErrorCode.InvalidCrewState, 19 },
                { SessionErrorCode.ConnectionLost, 20 }
            };
            var all = (SessionErrorCode[])Enum.GetValues(typeof(SessionErrorCode));
            Assert.Equal(expected.Count, all.Length);
            foreach (var kv in expected) Assert.Equal(kv.Value, (int)kv.Key);
        }

        [Fact]
        public void ProtocolConstantsMatchThePlan()
        {
            Assert.Equal(1, SessionProtocol.ProtocolVersion);
            Assert.Equal(1, SessionProtocol.WorldSchemaVersion);
            Assert.Equal(4096, SessionProtocol.ControlFrameMaxBytes);
            Assert.Equal(262144, SessionProtocol.StateFrameMaxBytes);
            Assert.Equal(65536, SessionProtocol.WorldSnapshotBudgetBytes);
            Assert.Equal(SessionProtocol.StateFrameMaxBytes / 4, SessionProtocol.WorldSnapshotBudgetBytes);
            Assert.Equal(new double[] { 1, 2, 4, 8, 8 }, SessionProtocol.ReconnectBackoffSeconds);
            Assert.Equal(0.25, SessionProtocol.WorldIntervalSeconds);
            Assert.Equal(6, SessionProtocol.JoinCodeDigits);
            Assert.Equal(16, SessionProtocol.TokenBytes);
            Assert.Equal(8, SessionProtocol.MaxJsonDepth);
            Assert.Equal(9, AllTypes.Length);
        }

        [Theory]
        [MemberData(nameof(Types))]
        public void EnvelopeOfEachTypeRoundTrips(string type)
        {
            var env = new SessionEnvelope { V = 1, T = type, Seq = 42, Sid = type == SessionProtocol.TypeHello ? null : "s-abc", Body = SampleBody() };
            string line = env.Encode();
            Assert.DoesNotContain("\n", line);
            var r = Decode(line);
            Assert.True(r.Ok);
            Assert.Equal(SessionErrorCode.None, r.Error);
            Assert.Equal(1, r.Envelope.V);
            Assert.Equal(type, r.Envelope.T);
            Assert.Equal(42L, r.Envelope.Seq);
            Assert.Equal(env.Sid, r.Envelope.Sid);
            Assert.Equal(SessionJson.Write(env.Body), SessionJson.Write(r.Envelope.Body));
            Assert.Equal(line, r.Envelope.Encode());
        }

        [Fact]
        public void RawBodyEncodeMatchesTreeEncode()
        {
            var env = new SessionEnvelope { V = 1, T = SessionProtocol.TypeWorld, Seq = 7, Sid = "s", Body = SampleBody() };
            string raw = SessionEnvelope.Encode(SessionProtocol.TypeWorld, 7, "s", SessionJson.Write(env.Body));
            Assert.Equal(env.Encode(), raw);
        }

        [Fact]
        public void ControlFrameFitsLimitWhenEncodedFromLargeSeq()
        {
            var env = new SessionEnvelope { V = 1, T = SessionProtocol.TypeHeartbeat, Seq = 9007199254740991L, Sid = "s", Body = JsonValue.NewObject() };
            var r = Decode(env.Encode());
            Assert.True(r.Ok);
            Assert.Equal(9007199254740991L, r.Envelope.Seq);
        }

        [Fact]
        public void HelloWithoutSidDecodes()
        {
            var r = Decode("{\"v\":1,\"t\":\"hello\",\"seq\":1,\"body\":{}}");
            Assert.True(r.Ok);
            Assert.Null(r.Envelope.Sid);
        }

        [Fact]
        public void NonHelloWithoutSidIsProtocolViolation()
        {
            AssertError("{\"v\":1,\"t\":\"heartbeat\",\"seq\":1,\"body\":{}}", SessionErrorCode.ProtocolViolation);
        }

        [Fact]
        public void MissingBodyIsTreatedAsEmptyObject()
        {
            var r = Decode("{\"v\":1,\"t\":\"heartbeat\",\"seq\":1,\"sid\":\"s\"}");
            Assert.True(r.Ok);
            Assert.Equal(JsonKind.Object, r.Envelope.Body.Kind);
            Assert.Empty(r.Envelope.Body.Members);
        }

        [Theory]
        [InlineData("{\"v\":1,\"t\":\"heartbeat\",\"seq\":1,\"sid\":\"s\",\"body\":[]}")]
        [InlineData("{\"v\":1,\"t\":\"heartbeat\",\"seq\":1,\"sid\":\"s\",\"body\":\"x\"}")]
        [InlineData("{\"v\":1,\"t\":\"heartbeat\",\"seq\":1,\"sid\":5,\"body\":{}}")]
        [InlineData("{\"v\":1,\"t\":\"heartbeat\",\"seq\":1,\"sid\":null,\"body\":{}}")]
        public void WrongTypedBodyOrSidIsProtocolViolation(string line)
        {
            AssertError(line, SessionErrorCode.ProtocolViolation);
        }

        [Theory]
        [InlineData("{\"v\":1,\"seq\":1,\"sid\":\"s\",\"body\":{}}")]
        [InlineData("{\"t\":\"heartbeat\",\"seq\":1,\"sid\":\"s\",\"body\":{}}")]
        [InlineData("{\"v\":\"1\",\"t\":\"heartbeat\",\"seq\":1,\"sid\":\"s\",\"body\":{}}")]
        [InlineData("{\"v\":1.5,\"t\":\"heartbeat\",\"seq\":1,\"sid\":\"s\",\"body\":{}}")]
        [InlineData("{\"v\":1,\"t\":7,\"seq\":1,\"sid\":\"s\",\"body\":{}}")]
        [InlineData("{\"v\":1,\"t\":null,\"seq\":1,\"sid\":\"s\",\"body\":{}}")]
        [InlineData("{\"v\":1,\"t\":\"heartbeat\",\"sid\":\"s\",\"body\":{}}")]
        [InlineData("{\"v\":1,\"t\":\"heartbeat\",\"seq\":0,\"sid\":\"s\",\"body\":{}}")]
        [InlineData("{\"v\":1,\"t\":\"heartbeat\",\"seq\":-3,\"sid\":\"s\",\"body\":{}}")]
        [InlineData("{\"v\":1,\"t\":\"heartbeat\",\"seq\":1.5,\"sid\":\"s\",\"body\":{}}")]
        [InlineData("{\"v\":1,\"t\":\"heartbeat\",\"seq\":\"2\",\"sid\":\"s\",\"body\":{}}")]
        [InlineData("{\"v\":1,\"t\":\"heartbeat\",\"seq\":1e300,\"sid\":\"s\",\"body\":{}}")]
        [InlineData("{\"v\":1,\"t\":\"heartbeat\",\"seq\":9223372036854775807,\"sid\":\"s\",\"body\":{}}")]
        public void MissingOrWrongTypedEnvelopeFieldsAreProtocolViolation(string line)
        {
            AssertError(line, SessionErrorCode.ProtocolViolation);
        }

        [Theory]
        [InlineData("[]")]
        [InlineData("\"hello\"")]
        [InlineData("42")]
        [InlineData("null")]
        [InlineData("")]
        [InlineData("{\"v\":1,\"t\":\"hello\",\"seq\":1")]
        [InlineData("{\"v\":1,\"t\":\"hello\",\"seq\":1,\"body\":{}} trailing")]
        [InlineData("{\"v\":1,\"v\":1,\"t\":\"hello\",\"seq\":1,\"body\":{}}")]
        [InlineData("{\"v\":1,\"t\":\"hello\",\"seq\":NaN,\"body\":{}}")]
        [InlineData("{\"v\":1,\"t\":\"hello\",\"seq\":1,\"body\":{\"a\":Infinity}}")]
        [InlineData("INTENT:{\"kind\":\"Scram\",\"target\":\"Core\",\"value\":1,\"issuer\":\"remote\"}")]
        [InlineData("STATE:{\"simSeconds\":1}")]
        public void MalformedLinesAreProtocolViolation(string line)
        {
            AssertError(line, SessionErrorCode.ProtocolViolation);
        }

        [Fact]
        public void NullLineIsProtocolViolation()
        {
            AssertError(null, SessionErrorCode.ProtocolViolation);
        }

        [Fact]
        public void NestingDepthNineIsProtocolViolation()
        {
            // The envelope root is depth 1 and the body object is depth 2.
            string Nest(int objects)
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < objects - 1; i++) sb.Append("{\"a\":");
                sb.Append("{}");
                for (int i = 0; i < objects - 1; i++) sb.Append('}');
                return sb.ToString();
            }
            string Frame(int bodyObjects) { return "{\"v\":1,\"t\":\"heartbeat\",\"seq\":1,\"sid\":\"s\",\"body\":" + Nest(bodyObjects) + "}"; }
            Assert.True(Decode(Frame(7)).Ok);   // total depth 8
            AssertError(Frame(8), SessionErrorCode.ProtocolViolation); // total depth 9
        }

        [Fact]
        public void WrongProtocolVersionIsUnsupportedProtocolWhenOtherwiseWellFormed()
        {
            AssertError("{\"v\":2,\"t\":\"hello\",\"seq\":1,\"body\":{}}", SessionErrorCode.UnsupportedProtocol);
            AssertError("{\"v\":0,\"t\":\"hello\",\"seq\":1,\"body\":{}}", SessionErrorCode.UnsupportedProtocol);
            AssertError("{\"v\":2,\"t\":\"future_type\",\"seq\":1,\"sid\":\"s\",\"body\":{}}", SessionErrorCode.UnsupportedProtocol);
        }

        [Fact]
        public void WrongVersionWithBrokenStructureIsProtocolViolation()
        {
            AssertError("{\"v\":2,\"t\":\"hello\",\"body\":{}}", SessionErrorCode.ProtocolViolation);
            AssertError("{\"v\":2,\"seq\":1,\"body\":{}}", SessionErrorCode.ProtocolViolation);
        }

        [Fact]
        public void UnknownTypeIsProtocolViolation()
        {
            AssertError("{\"v\":1,\"t\":\"explode\",\"seq\":1,\"sid\":\"s\",\"body\":{}}", SessionErrorCode.ProtocolViolation);
            AssertError("{\"v\":1,\"t\":\"HELLO\",\"seq\":1,\"body\":{}}", SessionErrorCode.ProtocolViolation);
        }

        [Fact]
        public void UnknownFieldsAreIgnoredAndKnownFieldsStillDecode()
        {
            var r = Decode("{\"extra\":[1,2],\"v\":1,\"t\":\"heartbeat\",\"seq\":3,\"sid\":\"s\",\"future\":{\"z\":1},\"body\":{\"k\":1}}");
            Assert.True(r.Ok);
            Assert.Equal(3L, r.Envelope.Seq);
            Assert.Equal("s", r.Envelope.Sid);
            Assert.Equal(1.0, r.Envelope.Body.Get("k").Number);
        }

        [Fact]
        public void EnvelopeNumbersRoundTripUnderANonInvariantCulture()
        {
            var saved = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                var env = new SessionEnvelope { V = 1, T = SessionProtocol.TypeWorld, Seq = 1234567, Sid = "s", Body = SampleBody() };
                string line = env.Encode();
                Assert.Contains("\"seq\":1234567", line);
                var r = Decode(line);
                Assert.True(r.Ok);
                Assert.Equal(0.1, r.Envelope.Body.Get("x").Number);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = saved;
            }
        }

        // ---- Intent body (KTD10 step 3) ----

        private static SessionIntentResult IntentFrom(string bodyJson)
        {
            var env = Decode("{\"v\":1,\"t\":\"intent\",\"seq\":1,\"sid\":\"s\",\"body\":" + bodyJson + "}");
            Assert.True(env.Ok);
            return SessionEnvelope.DecodeIntentBody(env.Envelope.Body);
        }

        [Fact]
        public void IntentBodyDecodesAllFields()
        {
            var r = IntentFrom("{\"kind\":\"SteamDemand\",\"target\":\"Turbine\",\"value\":0.75,\"atMinute\":12.5}");
            Assert.True(r.Ok);
            Assert.Equal(SessionErrorCode.None, r.Error);
            Assert.Equal(IntentKind.SteamDemand, r.Intent.Kind);
            Assert.Equal("Turbine", r.Intent.Target);
            Assert.Equal(0.75, r.Intent.Value);
            Assert.Equal(12.5, r.Intent.AtMinute);
        }

        [Fact]
        public void IntentBodyDefaultsTargetAndAtMinute()
        {
            var r = IntentFrom("{\"kind\":\"Scram\",\"value\":1}");
            Assert.True(r.Ok);
            Assert.Equal("", r.Intent.Target);
            Assert.Equal(-1.0, r.Intent.AtMinute);
        }

        [Fact]
        public void IntentBodyIgnoresClaimedIssuerAndLeavesAPlaceholder()
        {
            var r = IntentFrom("{\"kind\":\"Scram\",\"target\":\"Core\",\"value\":1,\"issuer\":\"host\"}");
            Assert.True(r.Ok);
            Assert.Equal(SessionProtocol.UnassignedIssuer, r.Intent.Issuer);
            Assert.NotEqual("host", r.Intent.Issuer);
        }

        [Fact]
        public void IntentBodyIgnoresUnknownFields()
        {
            var r = IntentFrom("{\"kind\":\"Scram\",\"value\":1,\"surprise\":[1,2,3]}");
            Assert.True(r.Ok);
        }

        [Fact]
        public void IntentBodyDecodesEveryDefinedKindByName()
        {
            foreach (IntentKind k in Enum.GetValues(typeof(IntentKind)))
            {
                var r = IntentFrom("{\"kind\":\"" + k + "\",\"value\":1}");
                Assert.True(r.Ok, k.ToString());
                Assert.Equal(k, r.Intent.Kind);
            }
        }

        [Theory]
        [InlineData("{\"kind\":999,\"value\":1}")]
        [InlineData("{\"kind\":0,\"value\":1}")]
        [InlineData("{\"kind\":\"999\",\"value\":1}")]
        [InlineData("{\"kind\":\"0\",\"value\":1}")]
        [InlineData("{\"kind\":\"-1\",\"value\":1}")]
        [InlineData("{\"kind\":\" 1\",\"value\":1}")]
        [InlineData("{\"kind\":\"NotAKind\",\"value\":1}")]
        [InlineData("{\"kind\":\"Scram,ResetScram\",\"value\":1}")]
        [InlineData("{\"kind\":\"scram\",\"value\":1}")]
        [InlineData("{\"kind\":\"Scram \",\"value\":1}")]
        [InlineData("{\"kind\":\"\",\"value\":1}")]
        [InlineData("{\"kind\":null,\"value\":1}")]
        [InlineData("{\"kind\":true,\"value\":1}")]
        [InlineData("{\"kind\":[\"Scram\"],\"value\":1}")]
        [InlineData("{\"value\":1}")]
        [InlineData("{\"kind\":\"Scram\"}")]
        [InlineData("{\"kind\":\"Scram\",\"value\":\"1\"}")]
        [InlineData("{\"kind\":\"Scram\",\"value\":null}")]
        [InlineData("{\"kind\":\"Scram\",\"value\":1,\"target\":5}")]
        [InlineData("{\"kind\":\"Scram\",\"value\":1,\"target\":null}")]
        [InlineData("{\"kind\":\"Scram\",\"value\":1,\"atMinute\":\"3\"}")]
        [InlineData("{\"kind\":\"Scram\",\"value\":1,\"atMinute\":null}")]
        [InlineData("{}")]
        public void BadIntentBodyIsInvalidIntent(string body)
        {
            var r = IntentFrom(body);
            Assert.False(r.Ok);
            Assert.Null(r.Intent);
            Assert.Equal(SessionErrorCode.InvalidIntent, r.Error);
        }

        [Fact]
        public void OversizedTargetIsInvalidIntent()
        {
            var r = IntentFrom("{\"kind\":\"Scram\",\"value\":1,\"target\":\"" + new string('x', 10000) + "\"}");
            Assert.Equal(SessionErrorCode.InvalidIntent, r.Error);
        }

        [Fact]
        public void TargetAtTheBoundIsAcceptedAndOneOverIsNot()
        {
            int max = SessionProtocol.IntentStringMaxChars;
            Assert.True(IntentFrom("{\"kind\":\"Scram\",\"value\":1,\"target\":\"" + new string('x', max) + "\"}").Ok);
            Assert.Equal(SessionErrorCode.InvalidIntent,
                IntentFrom("{\"kind\":\"Scram\",\"value\":1,\"target\":\"" + new string('x', max + 1) + "\"}").Error);
        }

        [Fact]
        public void NonObjectIntentBodyIsInvalidIntent()
        {
            Assert.Equal(SessionErrorCode.InvalidIntent, SessionEnvelope.DecodeIntentBody(null).Error);
            Assert.Equal(SessionErrorCode.InvalidIntent, SessionEnvelope.DecodeIntentBody(JsonValue.NewString("x")).Error);
            Assert.Equal(SessionErrorCode.InvalidIntent, SessionEnvelope.DecodeIntentBody(JsonValue.NewArray()).Error);
        }

        [Fact]
        public void IntentWithEscapedTargetSurvivesEncodeDecode()
        {
            var sent = new PlantIntent(IntentKind.LoadPreset, "cold \"shutdown\"\\ é", 0.1, "me", 3.25);
            var body = SessionJson.Parse(sent.ToJson());
            Assert.True(body.Ok);
            var r = SessionEnvelope.DecodeIntentBody(body.Value);
            Assert.True(r.Ok);
            Assert.Equal(sent.Kind, r.Intent.Kind);
            Assert.Equal(sent.Target, r.Intent.Target);
            Assert.Equal(sent.Value, r.Intent.Value);
            Assert.Equal(sent.AtMinute, r.Intent.AtMinute);
        }
    }
}
