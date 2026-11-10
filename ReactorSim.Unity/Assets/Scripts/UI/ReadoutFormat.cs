using System;
using System.Collections.Generic;
using System.Globalization;

// Pure (no UnityEngine): the one place a number becomes "value unit" text. CRT pages, the operator tablet, the
// tooltip, the drag readout and the HUD call this instead of ad-hoc ToString("F1"), so every readout has a unit and
// a fixed number of decimals. Linked into ReactorSim.Core.Tests.
namespace ReactorSim.Ui
{
    public static class ReadoutFormat
    {
        /// <summary>Shown in place of a value that is not a finite number.</summary>
        public const string Dash = "--";
        public const string NoReadingNote = "no reading";
        public const string OffScaleNote = "off scale";

        public const string Bar = "bar";
        public const string Percent = "%";
        public const string DegreesC = "°C";
        public const string Ppm = "ppm";
        public const string Rpm = "rpm";
        public const string MegawattThermal = "MWt";
        public const string MegawattElectric = "MWe";
        public const string Hertz = "Hz";
        public const string Pcm = "pcm";
        public const string Seconds = "s";

        // Default decimals per unit; a caller can still pass its own.
        private static readonly Dictionary<string, int> DecimalsByUnit = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            { Bar, 1 }, { Percent, 0 }, { DegreesC, 1 }, { Ppm, 0 }, { Rpm, 0 },
            { MegawattThermal, 0 }, { MegawattElectric, 1 }, { Hertz, 2 }, { Pcm, 0 }, { Seconds, 0 },
        };

        public static int DecimalsFor(string unit)
        {
            int d;
            return unit != null && DecimalsByUnit.TryGetValue(unit, out d) ? d : 1;
        }

        public static bool IsFinite(double v)
        {
            return !double.IsNaN(v) && !double.IsInfinity(v);
        }

        /// <summary>"" for a finite value, otherwise a short reason to show beside the dash.</summary>
        public static string Note(double v)
        {
            if (double.IsNaN(v)) return NoReadingNote;
            if (double.IsInfinity(v)) return OffScaleNote;
            return "";
        }

        /// <summary>The number alone, fixed decimals, "-" sign kept, never "-0". Non-finite gives the dash.</summary>
        public static string Number(double v, int decimals)
        {
            if (!IsFinite(v)) return Dash;
            decimals = Math.Max(0, Math.Min(6, decimals));
            string s = v.ToString("F" + decimals, CultureInfo.InvariantCulture);
            return IsAllZero(s) ? s.TrimStart('-') : s;
        }

        /// <summary>"12.3 bar". Non-finite gives just the dash (no unit, nothing to attach it to).</summary>
        public static string Value(double v, int decimals, string unit)
        {
            if (!IsFinite(v)) return Dash;
            string n = Number(v, decimals);
            if (string.IsNullOrEmpty(unit)) return n;
            return unit == Percent ? n + Percent : n + " " + unit;
        }

        /// <summary>Value with the unit's default decimals.</summary>
        public static string Value(double v, string unit)
        {
            return Value(v, DecimalsFor(unit), unit);
        }

        /// <summary>Like Value, but a non-finite value reads "-- (no reading)" so the cause is on screen.</summary>
        public static string ValueNoted(double v, int decimals, string unit)
        {
            return IsFinite(v) ? Value(v, decimals, unit) : Dash + " (" + Note(v) + ")";
        }

        /// <summary>A change since a press: signed ("+3.2 bar", "-1.0 bar"); a change that rounds to zero is unsigned.</summary>
        public static string Delta(double v, int decimals, string unit)
        {
            if (!IsFinite(v)) return Dash;
            string body = Value(v, decimals, unit);
            return v > 0.0 && !IsAllZero(Number(v, decimals)) ? "+" + body : body;
        }

        /// <summary>A 0..1 fraction shown as a percentage ("42%").</summary>
        public static string Fraction(double frac, int decimals = 0)
        {
            return Value(frac * 100.0, decimals, Percent);
        }

        public static string PressureBar(double v) { return Value(v, 1, Bar); }
        public static string TemperatureC(double v, int decimals = 1) { return Value(v, decimals, DegreesC); }
        public static string BoronPpm(double v) { return Value(v, 0, Ppm); }
        public static string TurbineRpm(double v) { return Value(v, 0, Rpm); }
        public static string PowerMWt(double v) { return Value(v, 0, MegawattThermal); }
        public static string PowerMWe(double v) { return Value(v, 1, MegawattElectric); }
        public static string FrequencyHz(double v) { return Value(v, 2, Hertz); }
        public static string ReactivityPcm(double v) { return Value(v, 0, Pcm); }

        private static bool IsAllZero(string number)
        {
            foreach (char c in number)
                if (c >= '1' && c <= '9') return false;
            return true;
        }
    }
}
