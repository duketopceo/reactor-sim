// ReactorSim.Core — gameplay tuning profile (KTD8).
// Physics truths stay constants in Physics.cs; this profile only holds gameplay pacing.
// Loaded from a string or path (Core never touches Unity); unknown keys are an error,
// missing keys fall back to the in-code defaults, a missing file falls back to defaults.
// Keep the in-code defaults and ReactorSim.Unity/Assets/StreamingAssets/tuning.json identical
// (TuningTests.ShippedTuningJson_ParsesAndMatchesInCodeDefaults enforces it).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace ReactorSim
{
    public class TuningException : Exception
    {
        public TuningException(string message) : base(message) { }
    }

    public class Tuning
    {
        // Sim-seconds per real second at the start of a session ("somewhat fast", KTD1).
        public double DefaultScale = 10.0;
        // Player-selectable range; scale intents beyond it are clamped (Dev intents may reach DevMaxScale).
        public double MinScale = 0.5;
        public double MaxScale = 60.0;
        public double DevMaxScale = 600.0;

        // Auto-slow: scale used while engaged, the positive reactor period (s) under which it
        // engages, and how many real seconds it holds after the last trigger.
        public double AutoSlowScale = 3.0;
        public double AutoSlowPeriodSeconds = 60.0;
        public double AutoSlowHoldSeconds = 20.0;

        // Sim tick length for interactive sessions (applied by the host via Simulation.ApplyInteractiveTick).
        // Runner scenarios and missions keep the default 5 s tick.
        public double InteractiveTickSeconds = 1.0;

        // Ticks one Advance call may run; carried time beyond this is dropped so a stalled frame cannot spiral.
        public int MaxTicksPerAdvance = 240;

        public static Tuning LoadFromFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new Tuning();
            return Parse(File.ReadAllText(path));
        }

        public static Tuning Parse(string json)
        {
            var t = new Tuning();
            var values = ScanFlatObject(json);
            foreach (var kv in values)
            {
                switch (kv.Key)
                {
                    case "defaultScale": t.DefaultScale = kv.Value; break;
                    case "minScale": t.MinScale = kv.Value; break;
                    case "maxScale": t.MaxScale = kv.Value; break;
                    case "devMaxScale": t.DevMaxScale = kv.Value; break;
                    case "autoSlowScale": t.AutoSlowScale = kv.Value; break;
                    case "autoSlowPeriodSeconds": t.AutoSlowPeriodSeconds = kv.Value; break;
                    case "autoSlowHoldSeconds": t.AutoSlowHoldSeconds = kv.Value; break;
                    case "interactiveTickSeconds": t.InteractiveTickSeconds = kv.Value; break;
                    case "maxTicksPerAdvance": t.MaxTicksPerAdvance = (int)kv.Value; break;
                    default: throw new TuningException("unknown tuning key \"" + kv.Key + "\"");
                }
            }
            t.Validate();
            return t;
        }

        public void Validate()
        {
            if (!(MinScale > 0.0)) throw new TuningException("minScale must be > 0");
            if (!(MaxScale >= MinScale)) throw new TuningException("maxScale must be >= minScale");
            if (!(DevMaxScale >= MaxScale)) throw new TuningException("devMaxScale must be >= maxScale");
            if (!(DefaultScale >= MinScale && DefaultScale <= MaxScale)) throw new TuningException("defaultScale must be within minScale..maxScale");
            if (!(AutoSlowScale > 0.0 && AutoSlowScale <= MaxScale)) throw new TuningException("autoSlowScale must be > 0 and <= maxScale");
            if (!(AutoSlowPeriodSeconds > 0.0)) throw new TuningException("autoSlowPeriodSeconds must be > 0");
            if (!(AutoSlowHoldSeconds >= 0.0)) throw new TuningException("autoSlowHoldSeconds must be >= 0");
            if (!(InteractiveTickSeconds > 0.0)) throw new TuningException("interactiveTickSeconds must be > 0");
            if (MaxTicksPerAdvance < 1) throw new TuningException("maxTicksPerAdvance must be >= 1");
        }

        // Minimal reader for a flat {"key": number, ...} object. Nested documents are U3's reader.
        private static List<KeyValuePair<string, double>> ScanFlatObject(string json)
        {
            var result = new List<KeyValuePair<string, double>>();
            var seen = new HashSet<string>();
            if (json == null) throw new TuningException("tuning profile is empty");
            int i = 0;
            SkipWs(json, ref i);
            if (i >= json.Length || json[i] != '{') throw new TuningException("tuning profile must be a JSON object");
            i++;
            SkipWs(json, ref i);
            if (i < json.Length && json[i] == '}') { i++; return Finish(json, i, result); }
            while (true)
            {
                SkipWs(json, ref i);
                if (i >= json.Length || json[i] != '"') throw new TuningException("expected a quoted key at offset " + i);
                int end = json.IndexOf('"', i + 1);
                if (end < 0) throw new TuningException("unterminated key at offset " + i);
                string key = json.Substring(i + 1, end - i - 1);
                i = end + 1;
                SkipWs(json, ref i);
                if (i >= json.Length || json[i] != ':') throw new TuningException("expected ':' after key \"" + key + "\"");
                i++;
                SkipWs(json, ref i);
                int start = i;
                while (i < json.Length && json[i] != ',' && json[i] != '}') i++;
                if (i >= json.Length) throw new TuningException("unterminated object after key \"" + key + "\"");
                string raw = json.Substring(start, i - start).Trim();
                double v;
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out v) || double.IsNaN(v) || double.IsInfinity(v))
                    throw new TuningException("value for \"" + key + "\" is not a finite number: " + raw);
                if (!seen.Add(key)) throw new TuningException("duplicate tuning key \"" + key + "\"");
                result.Add(new KeyValuePair<string, double>(key, v));
                if (json[i] == '}') { i++; break; }
                i++; // ','
            }
            return Finish(json, i, result);
        }

        private static List<KeyValuePair<string, double>> Finish(string json, int i, List<KeyValuePair<string, double>> result)
        {
            SkipWs(json, ref i);
            if (i != json.Length) throw new TuningException("unexpected text after the tuning object");
            return result;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }
    }
}
