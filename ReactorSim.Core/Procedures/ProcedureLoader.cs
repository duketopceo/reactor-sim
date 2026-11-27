// ReactorSim.Core — loads procedures.json (U16). Core never touches Unity: parse from a string or path.
// Shape: { "procedures": [ { "id", "title", "steps": [ { "id", "text", "phase", "control", "target", "rows": [..], "when", "value", "value2" (optional) } ] } ] }
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
            object doc;
            try { doc = new MiniJson(json, "procedures.json").ParseDocument(); }
            catch (JsonParseException e) { throw new ProcedureException(e.Message); }
            var root = doc as Dictionary<string, object>;
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
                    case "value2":
                        if (!(kv.Value is double)) throw new ProcedureException("step value2 must be a number");
                        s.Value2 = (double)kv.Value;
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
    }
}
