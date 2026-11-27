using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ReactorSim
{
    /// <summary>Raised by <see cref="MiniJson"/> for any malformed document. Callers map it to their own failure type.</summary>
    internal sealed class JsonParseException : Exception
    {
        public JsonParseException(string message) : base(message) { }
    }

    // Minimal JSON reader: objects, arrays, strings (with escapes), numbers, true/false/null.
    // Duplicate object keys are an error.
    internal sealed class MiniJson
    {
        private readonly string _s;
        private int _i;
        public const int MaxDepth = 16;
        private readonly string _prefix;
        private int _depth;

        public MiniJson(string s, string errorPrefix) { _s = s ?? ""; _prefix = errorPrefix; }

        public object ParseDocument()
        {
            var v = Value();
            Ws();
            if (_i != _s.Length) throw Fail("trailing text");
            return v;
        }

        private JsonParseException Fail(string msg) { return new JsonParseException(_prefix + " parse error at " + _i + ": " + msg); }
        private void Ws() { while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++; }

        private object Value()
        {
            Ws();
            if (_i >= _s.Length) throw Fail("unexpected end");
            char c = _s[_i];
            if (c == '{') return Obj();
            if (c == '[') return Arr();
            if (c == '"') return Text();
            if (Lit("true")) return true;
            if (Lit("false")) return false;
            if (Lit("null")) return null;
            return Num();
        }

        private bool Lit(string w)
        {
            if (string.CompareOrdinal(_s, _i, w, 0, w.Length) != 0) return false;
            _i += w.Length;
            return true;
        }

        private Dictionary<string, object> Obj()
        {
            if (++_depth > MaxDepth) throw Fail("nesting too deep");
            var d = new Dictionary<string, object>();
            _i++;
            Ws();
            if (_i < _s.Length && _s[_i] == '}') { _i++; _depth--; return d; }
            while (true)
            {
                Ws();
                if (_i >= _s.Length || _s[_i] != '"') throw Fail("expected key");
                string k = Text();
                Ws();
                if (_i >= _s.Length || _s[_i] != ':') throw Fail("expected ':'");
                _i++;
                if (d.ContainsKey(k)) throw Fail("duplicate key \"" + k + "\"");
                d[k] = Value();
                Ws();
                if (_i >= _s.Length) throw Fail("unterminated object");
                if (_s[_i] == ',') { _i++; continue; }
                if (_s[_i] == '}') { _i++; _depth--; return d; }
                throw Fail("expected ',' or '}'");
            }
        }

        private List<object> Arr()
        {
            if (++_depth > MaxDepth) throw Fail("nesting too deep");
            var l = new List<object>();
            _i++;
            Ws();
            if (_i < _s.Length && _s[_i] == ']') { _i++; _depth--; return l; }
            while (true)
            {
                l.Add(Value());
                Ws();
                if (_i >= _s.Length) throw Fail("unterminated array");
                if (_s[_i] == ',') { _i++; continue; }
                if (_s[_i] == ']') { _i++; _depth--; return l; }
                throw Fail("expected ',' or ']'");
            }
        }

        private string Text()
        {
            var sb = new StringBuilder();
            _i++;
            while (_i < _s.Length)
            {
                char c = _s[_i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (_i >= _s.Length) break;
                char e = _s[_i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'u':
                        if (_i + 4 > _s.Length) throw Fail("bad \\u escape");
                        int code = 0;
                        for (int k = 0; k < 4; k++)
                        {
                            int h = Hex(_s[_i + k]);
                            if (h < 0) throw Fail("bad \\u escape");
                            code = (code << 4) | h;
                        }
                        sb.Append((char)code);
                        _i += 4;
                        break;
                    default: throw Fail("bad escape");
                }
            }
            throw Fail("unterminated string");
        }

        private static int Hex(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        private double Num()
        {
            int start = _i;
            while (_i < _s.Length && "+-0123456789.eE".IndexOf(_s[_i]) >= 0) _i++;
            double d;
            string token = _s.Substring(start, _i - start);
            if (!IsJsonNumber(token) || !double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                throw Fail("expected a value");
            return d;
        }

        private static bool IsJsonNumber(string t)
        {
            int i = 0, n = t.Length;
            if (i < n && t[i] == '-') i++;
            if (i >= n) return false;
            if (t[i] == '0') i++;
            else if (t[i] >= '1' && t[i] <= '9') { while (i < n && IsDigit(t[i])) i++; }
            else return false;
            if (i < n && t[i] == '.')
            {
                i++;
                int f = i;
                while (i < n && IsDigit(t[i])) i++;
                if (i == f) return false;
            }
            if (i < n && (t[i] == 'e' || t[i] == 'E'))
            {
                i++;
                if (i < n && (t[i] == '+' || t[i] == '-')) i++;
                int e = i;
                while (i < n && IsDigit(t[i])) i++;
                if (i == e) return false;
            }
            return i == n;
        }

        private static bool IsDigit(char c) { return c >= '0' && c <= '9'; }
    }
}
