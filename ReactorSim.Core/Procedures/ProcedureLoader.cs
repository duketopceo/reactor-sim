// ReactorSim.Core — loads procedures.json (U16). Core never touches Unity: parse from a string or path.
// Shape: { "procedures": [ { "id", "title", "steps": [ { "id", "text", "phase", "control", "target", "rows": [..], "when", "value" } ] } ] }
// Unknown keys, unknown predicates, empty ids and duplicate ids are errors.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ReactorSim
{
    public static class ProcedureLoader
    {
        public static List<Procedure> LoadFromFile(string path)
        {
            return Parse(File.ReadAllText(path));
        }

        public static List<Procedure> Parse(string json)
        {
            var root = new MiniJson(json).ParseDocument() as Dictionary<string, object>;
            if (root == null) throw new ProcedureException("procedures.json: top level must be an object");
            var result = new List<Procedure>();
            var seen = new HashSet<string>();
            foreach (var kv in root)
            {
                if (kv.Key != "procedures") throw new ProcedureException("unknown key \"" + kv.Key + "\"");
                var list = kv.Value as List<object>;
                if (list == null) throw new ProcedureException("\"procedures\" must be an array");
                foreach (var po in list)
                {
                    var p = ReadProcedure(po as Dictionary<string, object>);
                    if (!seen.Add(p.Id)) throw new ProcedureException("duplicate procedure id \"" + p.Id + "\"");
                    result.Add(p);
                }
            }
            if (result.Count == 0) throw new ProcedureException("no procedures");
            return result;
        }

        private static Procedure ReadProcedure(Dictionary<string, object> o)
        {
            if (o == null) throw new ProcedureException("procedure must be an object");
            var p = new Procedure();
            foreach (var kv in o)
            {
                switch (kv.Key)
                {
                    case "id": p.Id = Str(kv.Value, "procedure id"); break;
                    case "title": p.Title = Str(kv.Value, "procedure title"); break;
                    case "steps":
                        var steps = kv.Value as List<object>;
                        if (steps == null) throw new ProcedureException("\"steps\" must be an array");
                        foreach (var so in steps) p.Steps.Add(ReadStep(so as Dictionary<string, object>));
                        break;
                    default: throw new ProcedureException("unknown procedure key \"" + kv.Key + "\"");
                }
            }
            if (string.IsNullOrEmpty(p.Id)) throw new ProcedureException("procedure without id");
            if (p.Steps.Count == 0) throw new ProcedureException("procedure \"" + p.Id + "\" has no steps");
            var ids = new HashSet<string>();
            foreach (var s in p.Steps)
                if (!ids.Add(s.Id)) throw new ProcedureException("duplicate step id \"" + s.Id + "\" in \"" + p.Id + "\"");
            return p;
        }

        private static ProcedureStep ReadStep(Dictionary<string, object> o)
        {
            if (o == null) throw new ProcedureException("step must be an object");
            var s = new ProcedureStep();
            foreach (var kv in o)
            {
                switch (kv.Key)
                {
                    case "id": s.Id = Str(kv.Value, "step id"); break;
                    case "text": s.Text = Str(kv.Value, "step text"); break;
                    case "phase": s.Phase = Str(kv.Value, "step phase"); break;
                    case "control": s.Control = Str(kv.Value, "step control"); break;
                    case "target": s.Target = Str(kv.Value, "step target"); break;
                    case "when": s.Predicate = Str(kv.Value, "step when"); break;
                    case "value":
                        if (!(kv.Value is double)) throw new ProcedureException("step value must be a number");
                        s.Value = (double)kv.Value;
                        break;
                    case "rows":
                        var rows = kv.Value as List<object>;
                        if (rows == null) throw new ProcedureException("step rows must be an array");
                        var arr = new string[rows.Count];
                        for (int i = 0; i < arr.Length; i++) arr[i] = Str(rows[i], "row id");
                        s.Rows = arr;
                        break;
                    default: throw new ProcedureException("unknown step key \"" + kv.Key + "\"");
                }
            }
            if (string.IsNullOrEmpty(s.Id)) throw new ProcedureException("step without id");
            if (string.IsNullOrEmpty(s.Text)) throw new ProcedureException("step \"" + s.Id + "\" has no text");
            if (!ProcedurePredicates.Exists(s.Predicate))
                throw new ProcedureException("step \"" + s.Id + "\" uses unknown predicate \"" + s.Predicate + "\"");
            return s;
        }

        private static string Str(object v, string what)
        {
            var s = v as string;
            if (s == null) throw new ProcedureException(what + " must be a string");
            return s;
        }

        // Minimal JSON reader: objects, arrays, strings (with escapes), numbers, true/false/null.
        // Duplicate object keys are an error.
        private class MiniJson
        {
            private readonly string _s;
            private int _i;
            public MiniJson(string s) { _s = s ?? ""; }

            public object ParseDocument()
            {
                var v = Value();
                Ws();
                if (_i != _s.Length) throw Fail("trailing text");
                return v;
            }

            private ProcedureException Fail(string msg) { return new ProcedureException("procedures.json parse error at " + _i + ": " + msg); }
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
                var d = new Dictionary<string, object>();
                _i++;
                Ws();
                if (_i < _s.Length && _s[_i] == '}') { _i++; return d; }
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
                    if (_s[_i] == '}') { _i++; return d; }
                    throw Fail("expected ',' or '}'");
                }
            }

            private List<object> Arr()
            {
                var l = new List<object>();
                _i++;
                Ws();
                if (_i < _s.Length && _s[_i] == ']') { _i++; return l; }
                while (true)
                {
                    l.Add(Value());
                    Ws();
                    if (_i >= _s.Length) throw Fail("unterminated array");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == ']') { _i++; return l; }
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
                            sb.Append((char)int.Parse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _i += 4;
                            break;
                        default: throw Fail("bad escape");
                    }
                }
                throw Fail("unterminated string");
            }

            private double Num()
            {
                int start = _i;
                while (_i < _s.Length && "+-0123456789.eE".IndexOf(_s[_i]) >= 0) _i++;
                double d;
                if (_i == start || !double.TryParse(_s.Substring(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                    throw Fail("expected a value");
                return d;
            }
        }
    }
}
