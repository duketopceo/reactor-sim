using System.Collections.Generic;
using System.Text;

// Pure logic (no UnityEngine): the one minimal RFC 4180 reader behind design/control-room/inventory.csv. Used by the
// editor's DesignData and by InventoryTests (linked into ReactorSim.Core.Tests).
namespace ReactorSim.Csv
{
    public static class CsvReader
    {
        /// <summary>
        /// Quoted fields, doubled quotes, CRLF or LF. Blank lines produce no record; the header is record 0.
        /// </summary>
        public static List<List<string>> Parse(string text)
        {
            var records = new List<List<string>>();
            var cur = new List<string>();
            var sb = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (quoted)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; }
                        else quoted = false;
                    }
                    else sb.Append(ch);
                }
                else if (ch == '"') quoted = true;
                else if (ch == ',') { cur.Add(sb.ToString()); sb.Clear(); }
                else if (ch == '\n' || ch == '\r')
                {
                    if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    cur.Add(sb.ToString()); sb.Clear();
                    if (!(cur.Count == 1 && cur[0].Length == 0)) records.Add(cur);
                    cur = new List<string>();
                }
                else sb.Append(ch);
            }
            if (sb.Length > 0 || cur.Count > 0) { cur.Add(sb.ToString()); records.Add(cur); }
            return records;
        }
    }
}
