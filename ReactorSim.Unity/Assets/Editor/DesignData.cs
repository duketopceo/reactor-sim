using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

// Editor-side reader for design/control-room/inventory.csv and layout.json (the single source of truth, KTD1).
// Used by the scene builder (row metadata), ControlMapCheck, InstrumentCheck and LayoutCheck.
public static class DesignData
{
    public sealed class Row
    {
        public readonly Dictionary<string, string> F = new Dictionary<string, string>();
        public string this[string k] { get { string v; return F.TryGetValue(k, out v) ? v : ""; } }
        public string Id { get { return this["id"]; } }
        public bool IsControl { get { return this["role"] == "control"; } }
        /// <summary>Mirrors Blender room_layout.plan_row: needs_core_model rows, and needs_mapping controls, are placed out of service.</summary>
        public bool IsOutOfService
        {
            get
            {
                string ready = this["readiness"];
                return ready == "needs_core_model" || (IsControl && ready == "needs_mapping");
            }
        }
        /// <summary>Full name for tooltips (the engraved plate uses short_label).</summary>
        public string Label { get { return this["label"]; } }
        public string BindingKind
        {
            get
            {
                string[] p = this["binding"].Split(':');
                return p.Length > 1 ? p[1] : "";
            }
        }
    }

    public static string RepoRoot
    {
        get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..")); }
    }

    public static string DesignDir { get { return Path.Combine(RepoRoot, "design", "control-room"); } }

    public static List<Row> LoadInventory()
    {
        string path = Path.Combine(DesignDir, "inventory.csv");
        var records = ReactorSim.Csv.CsvReader.Parse(File.ReadAllText(path, Encoding.UTF8));
        var rows = new List<Row>();
        if (records.Count < 2) return rows;
        var header = records[0];
        for (int i = 1; i < records.Count; i++)
        {
            var row = new Row();
            for (int c = 0; c < header.Count; c++)
                row.F[header[c]] = c < records[i].Count ? records[i][c] : "";
            rows.Add(row);
        }
        return rows;
    }

    public static Dictionary<string, object> LoadLayout()
    {
        string path = Path.Combine(DesignDir, "layout.json");
        var p = new JsonParser(File.ReadAllText(path, Encoding.UTF8));
        return p.ParseValue() as Dictionary<string, object>;
    }

    // ---- layout helpers ----------------------------------------------------------------------------------

    public static object Get(object node, params string[] path)
    {
        for (int i = 0; i < path.Length; i++)
        {
            var d = node as Dictionary<string, object>;
            object next;
            if (d == null || !d.TryGetValue(path[i], out next)) return null;
            node = next;
        }
        return node;
    }

    public static float Num(object node, float fallback, params string[] path)
    {
        object v = Get(node, path);
        return v is double ? (float)(double)v : fallback;
    }

    // ---- minimal JSON: objects, arrays, numbers (double), strings, true/false/null ------------------------

    private sealed class JsonParser
    {
        private readonly string s;
        private int i;

        public JsonParser(string text) { s = text; }

        private void Ws() { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        public object ParseValue()
        {
            Ws();
            char c = s[i];
            if (c == '{') return ParseObject();
            if (c == '[') return ParseArray();
            if (c == '"') return ParseString();
            if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
        }

        private Dictionary<string, object> ParseObject()
        {
            var d = new Dictionary<string, object>();
            i++; Ws();
            if (s[i] == '}') { i++; return d; }
            while (true)
            {
                Ws();
                string key = ParseString();
                Ws(); i++; // ':'
                d[key] = ParseValue();
                Ws();
                if (s[i] == ',') { i++; continue; }
                i++; // '}'
                return d;
            }
        }

        private List<object> ParseArray()
        {
            var l = new List<object>();
            i++; Ws();
            if (s[i] == ']') { i++; return l; }
            while (true)
            {
                l.Add(ParseValue());
                Ws();
                if (s[i] == ',') { i++; continue; }
                i++; // ']'
                return l;
            }
        }

        private string ParseString()
        {
            var sb = new StringBuilder();
            i++; // opening quote
            while (s[i] != '"')
            {
                char c = s[i++];
                if (c == '\\')
                {
                    char e = s[i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                        default: sb.Append(e); break;
                    }
                }
                else sb.Append(c);
            }
            i++; // closing quote
            return sb.ToString();
        }
    }
}
