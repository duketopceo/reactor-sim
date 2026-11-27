// ReactorSim.Core - session envelope codec and intent body decode (Phase 3, U1, KTD3, KTD10 step 3).
// Decode never throws on bad input: it returns a result carrying an envelope or a SessionErrorCode.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ReactorSim.Session
{
    public sealed class SessionEnvelopeResult
    {
        public bool Ok;
        public SessionEnvelope Envelope;
        public SessionErrorCode Error;
    }

    public sealed class SessionIntentResult
    {
        public bool Ok;
        public PlantIntent Intent;
        public SessionErrorCode Error;
    }

    public sealed class SessionEnvelope
    {
        public int V = SessionProtocol.ProtocolVersion;
        public string T;
        public long Seq;
        // Session ID; null (absent on the wire) only on Hello.
        public string Sid;
        // Always an object after Decode; null encodes as {}.
        public JsonValue Body;

        public string Encode()
        {
            return EncodeCore(V, T, Seq, Sid, Body == null ? "{}" : SessionJson.Write(Body));
        }

        // For large pre-serialized bodies (for example a world snapshot) so they are not parsed and rewritten.
        public static string Encode(string type, long seq, string sid, string bodyJson)
        {
            return EncodeCore(SessionProtocol.ProtocolVersion, type, seq, sid, string.IsNullOrEmpty(bodyJson) ? "{}" : bodyJson);
        }

        private static string EncodeCore(int v, string type, long seq, string sid, string bodyJson)
        {
            var sb = new StringBuilder(64 + bodyJson.Length);
            sb.Append("{\"v\":");
            SessionJson.AppendInteger(sb, v);
            sb.Append(",\"t\":");
            SessionJson.AppendString(sb, type);
            sb.Append(",\"seq\":");
            SessionJson.AppendInteger(sb, seq);
            if (sid != null)
            {
                sb.Append(",\"sid\":");
                SessionJson.AppendString(sb, sid);
            }
            sb.Append(",\"body\":").Append(bodyJson).Append('}');
            return sb.ToString();
        }

        // Check order: JSON and field shape first (ProtocolViolation), then the version (UnsupportedProtocol), then the
        // type name (ProtocolViolation). A future version may add types, so an unknown type under another version
        // reports the version. A missing body is treated as an empty object; a present non-object body is refused.
        public static SessionEnvelopeResult Decode(string line)
        {
            var parsed = SessionJson.Parse(line);
            if (!parsed.Ok || parsed.Value.Kind != JsonKind.Object)
                return Fail(SessionErrorCode.ProtocolViolation);
            JsonValue root = parsed.Value;

            long v;
            if (!SessionJson.TryWholeNumber(root.Get("v"), out v))
                return Fail(SessionErrorCode.ProtocolViolation);

            JsonValue t = root.Get("t");
            if (t == null || t.Kind != JsonKind.String)
                return Fail(SessionErrorCode.ProtocolViolation);

            long seq;
            if (!SessionJson.TryWholeNumber(root.Get("seq"), out seq) || seq < 1)
                return Fail(SessionErrorCode.ProtocolViolation);

            string sid = null;
            JsonValue sidValue = root.Get("sid");
            if (sidValue != null)
            {
                if (sidValue.Kind != JsonKind.String)
                    return Fail(SessionErrorCode.ProtocolViolation);
                sid = sidValue.Text;
            }
            else if (t.Text != SessionProtocol.TypeHello)
            {
                return Fail(SessionErrorCode.ProtocolViolation);
            }

            JsonValue body = root.Get("body");
            if (body == null)
                body = JsonValue.NewObject();
            else if (body.Kind != JsonKind.Object)
                return Fail(SessionErrorCode.ProtocolViolation);

            if (v != SessionProtocol.ProtocolVersion)
                return Fail(SessionErrorCode.UnsupportedProtocol);
            if (!SessionProtocol.IsKnownType(t.Text))
                return Fail(SessionErrorCode.ProtocolViolation);

            return new SessionEnvelopeResult
            {
                Ok = true,
                Error = SessionErrorCode.None,
                Envelope = new SessionEnvelope { V = (int)v, T = t.Text, Seq = seq, Sid = sid, Body = body }
            };
        }

        private static SessionEnvelopeResult Fail(SessionErrorCode code)
        {
            return new SessionEnvelopeResult { Ok = false, Envelope = null, Error = code };
        }

        private static readonly HashSet<string> KindNames = new HashSet<string>(Enum.GetNames(typeof(IntentKind)), StringComparer.Ordinal);

        // Decodes an intent body from a strict value tree. kind is an exact, case-sensitive IntentKind name; numbers,
        // lists and unknown names are refused. Any claimed issuer is ignored: Issuer is a placeholder the host overwrites.
        // JSON cannot carry NaN or Infinity, so value is always finite here; the NonFiniteValue code exists for the
        // host's EnqueueIntent mapping (KTD10 step 8), not for this decoder.
        public static SessionIntentResult DecodeIntentBody(JsonValue body)
        {
            if (body == null || body.Kind != JsonKind.Object)
                return BadIntent();

            JsonValue kindValue = body.Get("kind");
            if (kindValue == null || kindValue.Kind != JsonKind.String || !KindNames.Contains(kindValue.Text))
                return BadIntent();
            var kind = (IntentKind)Enum.Parse(typeof(IntentKind), kindValue.Text, false);

            string target = "";
            JsonValue targetValue = body.Get("target");
            if (targetValue != null)
            {
                if (targetValue.Kind != JsonKind.String || targetValue.Text.Length > SessionProtocol.IntentStringMaxChars)
                    return BadIntent();
                target = targetValue.Text;
            }

            JsonValue valueValue = body.Get("value");
            if (valueValue == null || valueValue.Kind != JsonKind.Number)
                return BadIntent();

            double atMinute = -1.0;
            JsonValue atValue = body.Get("atMinute");
            if (atValue != null)
            {
                if (atValue.Kind != JsonKind.Number)
                    return BadIntent();
                atMinute = atValue.Number;
            }

            return new SessionIntentResult
            {
                Ok = true,
                Error = SessionErrorCode.None,
                Intent = new PlantIntent(kind, target, valueValue.Number, SessionProtocol.UnassignedIssuer, atMinute)
            };
        }

        private static SessionIntentResult BadIntent()
        {
            return new SessionIntentResult { Ok = false, Intent = null, Error = SessionErrorCode.InvalidIntent };
        }
    }
}
