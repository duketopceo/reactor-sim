// ReactorSim.Core - strict, bounded JSON value tree, parser and writer for the session wire format (Phase 3, U1, KTD3).
// The parser never throws on bad input: it returns a result carrying either a value or an error message.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ReactorSim.Session
{
    public enum JsonKind
    {
        Null,
        Bool,
        Number,
        String,
        Array,
        Object
    }

    public sealed class JsonValue
    {
        public JsonKind Kind;
        public bool Bool;
        public double Number;
        // Set by the parser when the number token carried a fraction or exponent; such a token is never a wire integer, even if it rounds to one.
        public bool NotIntegerLiteral;
        public string Text;
        public List<JsonValue> Items;
        public List<KeyValuePair<string, JsonValue>> Members;

        public static JsonValue NewNull() { return new JsonValue { Kind = JsonKind.Null }; }
        public static JsonValue NewBool(bool v) { return new JsonValue { Kind = JsonKind.Bool, Bool = v }; }
        public static JsonValue NewNumber(double v) { return new JsonValue { Kind = JsonKind.Number, Number = v }; }
        public static JsonValue NewString(string v) { return new JsonValue { Kind = JsonKind.String, Text = v ?? "" }; }

        public static JsonValue NewArray(params JsonValue[] items)
        {
            return new JsonValue { Kind = JsonKind.Array, Items = new List<JsonValue>(items) };
        }

        public static JsonValue NewObject(params KeyValuePair<string, JsonValue>[] members)
        {
            return new JsonValue { Kind = JsonKind.Object, Members = new List<KeyValuePair<string, JsonValue>>(members) };
        }

        // Member lookup on an object; null when this is not an object or the key is absent.
        public JsonValue Get(string key)
        {
            if (Kind != JsonKind.Object)
                return null;
            for (int i = 0; i < Members.Count; i++)
            {
                if (string.Equals(Members[i].Key, key, StringComparison.Ordinal))
                    return Members[i].Value;
            }
            return null;
        }
    }

    public sealed class JsonParseResult
    {
        public bool Ok;
        public JsonValue Value;
        public string Error;
    }

    public static class SessionJson
    {
        internal static bool HasUnpairedSurrogate(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    i++;
                    continue;
                }
                if (char.IsSurrogate(c))
                    return true;
            }
            return false;
        }

        internal static bool TryWholeNumber(JsonValue v, out long result)
        {
            result = 0;
            if (v == null || v.Kind != JsonKind.Number || v.NotIntegerLiteral)
                return false;
            double d = v.Number;
            if (d != Math.Floor(d) || Math.Abs(d) > SessionProtocol.MaxWireInteger)
                return false;
            result = (long)d;
            return true;
        }

        public static JsonParseResult Parse(string text)
        {
            if (text == null)
                return Fail("input is null");
            var p = new Parser(text);
            JsonValue v = p.ParseDocument();
            if (v == null)
                return Fail(p.Error ?? "invalid JSON");
            return new JsonParseResult { Ok = true, Value = v };
        }

        private static JsonParseResult Fail(string error)
        {
            return new JsonParseResult { Ok = false, Value = null, Error = error };
        }

        // ---- writer ----

        public static string Write(JsonValue value)
        {
            var sb = new StringBuilder();
            AppendValue(sb, value);
            return sb.ToString();
        }

        public static void AppendValue(StringBuilder sb, JsonValue value)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }
            switch (value.Kind)
            {
                case JsonKind.Null:
                    sb.Append("null");
                    break;
                case JsonKind.Bool:
                    sb.Append(value.Bool ? "true" : "false");
                    break;
                case JsonKind.Number:
                    AppendNumber(sb, value.Number);
                    break;
                case JsonKind.String:
                    AppendString(sb, value.Text);
                    break;
                case JsonKind.Array:
                    sb.Append('[');
                    for (int i = 0; i < value.Items.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        AppendValue(sb, value.Items[i]);
                    }
                    sb.Append(']');
                    break;
                default:
                    sb.Append('{');
                    for (int i = 0; i < value.Members.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        AppendString(sb, value.Members[i].Key);
                        sb.Append(':');
                        AppendValue(sb, value.Members[i].Value);
                    }
                    sb.Append('}');
                    break;
            }
        }

        // Quotes, backslash and control characters below 0x20 are escaped (controls as \u00XX); everything else is written as is.
        public static void AppendString(StringBuilder sb, string s)
        {
            sb.Append('"');
            if (s != null)
            {
                for (int i = 0; i < s.Length; i++)
                {
                    char c = s[i];
                    if (c == '"') sb.Append("\\\"");
                    else if (c == '\\') sb.Append("\\\\");
                    else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                }
            }
            sb.Append('"');
        }

        // G17 with the invariant culture, matching PlantSnapshot.ToJson; it round-trips every finite double.
        // JSON has no NaN or Infinity, so a non-finite number is a caller bug and throws.
        public static void AppendNumber(StringBuilder sb, double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v))
                throw new ArgumentException("JSON cannot carry a non-finite number", nameof(v));
            sb.Append(v.ToString("G17", CultureInfo.InvariantCulture));
        }

        public static void AppendInteger(StringBuilder sb, long v)
        {
            sb.Append(v.ToString(CultureInfo.InvariantCulture));
        }

        // ---- parser ----

        private sealed class Parser
        {
            private readonly string _s;
            private int _pos;
            public string Error;

            public Parser(string s) { _s = s; }

            public JsonValue ParseDocument()
            {
                SkipWhitespace();
                JsonValue v = ParseValue(0);
                if (v == null)
                    return null;
                SkipWhitespace();
                if (_pos != _s.Length)
                    return Fail("trailing data");
                return v;
            }

            private JsonValue Fail(string message)
            {
                if (Error == null)
                    Error = message + " at " + _pos.ToString(CultureInfo.InvariantCulture);
                return null;
            }

            private void SkipWhitespace()
            {
                while (_pos < _s.Length)
                {
                    char c = _s[_pos];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r') _pos++;
                    else break;
                }
            }

            private JsonValue ParseValue(int depth)
            {
                if (_pos >= _s.Length)
                    return Fail("unexpected end");
                char c = _s[_pos];
                switch (c)
                {
                    case '{': return ParseObject(depth + 1);
                    case '[': return ParseArray(depth + 1);
                    case '"':
                        string str = ParseString();
                        return str == null ? null : JsonValue.NewString(str);
                    case 't': return ParseLiteral("true", JsonValue.NewBool(true));
                    case 'f': return ParseLiteral("false", JsonValue.NewBool(false));
                    case 'n': return ParseLiteral("null", JsonValue.NewNull());
                    default:
                        if (c == '-' || (c >= '0' && c <= '9'))
                            return ParseNumber();
                        return Fail("unexpected character");
                }
            }

            private JsonValue ParseLiteral(string word, JsonValue result)
            {
                if (string.CompareOrdinal(_s, _pos, word, 0, word.Length) != 0)
                    return Fail("bad literal");
                _pos += word.Length;
                return result;
            }

            private JsonValue ParseObject(int depth)
            {
                if (depth > SessionProtocol.MaxJsonDepth)
                    return Fail("nesting too deep");
                _pos++; // {
                var members = new List<KeyValuePair<string, JsonValue>>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                SkipWhitespace();
                if (_pos < _s.Length && _s[_pos] == '}')
                {
                    _pos++;
                    return new JsonValue { Kind = JsonKind.Object, Members = members };
                }
                while (true)
                {
                    SkipWhitespace();
                    if (_pos >= _s.Length || _s[_pos] != '"')
                        return Fail("expected object key");
                    string key = ParseString();
                    if (key == null)
                        return null;
                    if (!seen.Add(key))
                        return Fail("duplicate key");
                    SkipWhitespace();
                    if (_pos >= _s.Length || _s[_pos] != ':')
                        return Fail("expected ':'");
                    _pos++;
                    SkipWhitespace();
                    JsonValue v = ParseValue(depth);
                    if (v == null)
                        return null;
                    members.Add(new KeyValuePair<string, JsonValue>(key, v));
                    SkipWhitespace();
                    if (_pos >= _s.Length)
                        return Fail("unexpected end");
                    char c = _s[_pos++];
                    if (c == '}')
                        return new JsonValue { Kind = JsonKind.Object, Members = members };
                    if (c != ',')
                        return Fail("expected ',' or '}'");
                }
            }

            private JsonValue ParseArray(int depth)
            {
                if (depth > SessionProtocol.MaxJsonDepth)
                    return Fail("nesting too deep");
                _pos++; // [
                var items = new List<JsonValue>();
                SkipWhitespace();
                if (_pos < _s.Length && _s[_pos] == ']')
                {
                    _pos++;
                    return new JsonValue { Kind = JsonKind.Array, Items = items };
                }
                while (true)
                {
                    SkipWhitespace();
                    JsonValue v = ParseValue(depth);
                    if (v == null)
                        return null;
                    items.Add(v);
                    SkipWhitespace();
                    if (_pos >= _s.Length)
                        return Fail("unexpected end");
                    char c = _s[_pos++];
                    if (c == ']')
                        return new JsonValue { Kind = JsonKind.Array, Items = items };
                    if (c != ',')
                        return Fail("expected ',' or ']'");
                }
            }

            private string ParseString()
            {
                _pos++; // opening quote
                var sb = new StringBuilder();
                while (true)
                {
                    if (_pos >= _s.Length)
                    {
                        Fail("unterminated string");
                        return null;
                    }
                    char c = _s[_pos++];
                    if (c == '"')
                        break;
                    if (c < 0x20)
                    {
                        Fail("control character in string");
                        return null;
                    }
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    if (_pos >= _s.Length)
                    {
                        Fail("unterminated escape");
                        return null;
                    }
                    char e = _s[_pos++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_pos + 4 > _s.Length)
                            {
                                Fail("short unicode escape");
                                return null;
                            }
                            int code = 0;
                            for (int i = 0; i < 4; i++)
                            {
                                int h = HexValue(_s[_pos + i]);
                                if (h < 0)
                                {
                                    Fail("bad unicode escape");
                                    return null;
                                }
                                code = code * 16 + h;
                            }
                            _pos += 4;
                            sb.Append((char)code);
                            break;
                        default:
                            Fail("bad escape");
                            return null;
                    }
                }
                string result = sb.ToString();
                if (HasUnpairedSurrogate(result))
                {
                    Fail("unpaired surrogate");
                    return null;
                }
                return result;
            }

            private static int HexValue(char c)
            {
                if (c >= '0' && c <= '9') return c - '0';
                if (c >= 'a' && c <= 'f') return c - 'a' + 10;
                if (c >= 'A' && c <= 'F') return c - 'A' + 10;
                return -1;
            }

            // Grammar: -?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?
            private JsonValue ParseNumber()
            {
                int start = _pos;
                bool integerLiteral = true;
                if (_s[_pos] == '-') _pos++;
                if (_pos >= _s.Length)
                    return Fail("bad number");
                if (_s[_pos] == '0')
                {
                    _pos++;
                    if (_pos < _s.Length && _s[_pos] >= '0' && _s[_pos] <= '9')
                        return Fail("leading zero");
                }
                else if (_s[_pos] >= '1' && _s[_pos] <= '9')
                {
                    while (_pos < _s.Length && _s[_pos] >= '0' && _s[_pos] <= '9') _pos++;
                }
                else
                {
                    return Fail("bad number");
                }
                if (_pos < _s.Length && _s[_pos] == '.')
                {
                    _pos++;
                    integerLiteral = false;
                    if (!SkipDigits())
                        return Fail("bad fraction");
                }
                if (_pos < _s.Length && (_s[_pos] == 'e' || _s[_pos] == 'E'))
                {
                    _pos++;
                    integerLiteral = false;
                    if (_pos < _s.Length && (_s[_pos] == '+' || _s[_pos] == '-')) _pos++;
                    if (!SkipDigits())
                        return Fail("bad exponent");
                }
                double d;
                if (!double.TryParse(_s.Substring(start, _pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out d)
                    || double.IsNaN(d) || double.IsInfinity(d))
                    return Fail("number out of range");
                JsonValue number = JsonValue.NewNumber(d);
                number.NotIntegerLiteral = !integerLiteral;
                return number;
            }

            private bool SkipDigits()
            {
                int begin = _pos;
                while (_pos < _s.Length && _s[_pos] >= '0' && _s[_pos] <= '9') _pos++;
                return _pos > begin;
            }
        }
    }
}
