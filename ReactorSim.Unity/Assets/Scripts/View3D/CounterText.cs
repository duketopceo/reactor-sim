using System;
using System.Globalization;

// Pure logic (no UnityEngine): the five digits a K04 seven-segment counter shows. Linked into ReactorSim.Core.Tests.
// The kit has five digit windows, so the value is rounded to an integer and zero-padded to five digits ("01500"); a
// negative value takes the first window for its sign ("-1234"); a value past 99999 (flux in counts per second) shows
// mantissa and exponent in the same five windows ("3.2E8"); anything below -9999 holds at the display limit.
namespace ReactorSim.Instruments
{
    public static class CounterText
    {
        public const int Digits = 5;
        public const long MaxValue = 99999;
        public const long MinValue = -9999;

        public static string Format(double value)
        {
            if (double.IsNaN(value)) return "-----";
            double r = Math.Round(value);
            if (r > MaxValue) return Exponent(r);
            if (r < MinValue) r = MinValue;
            long n = (long)r;
            if (n >= 0) return n.ToString("D" + Digits, CultureInfo.InvariantCulture);
            return "-" + (-n).ToString("D" + (Digits - 1), CultureInfo.InvariantCulture);
        }

        private static string Exponent(double v)
        {
            int e = (int)Math.Floor(Math.Log10(v));
            double m = Math.Round(v / Math.Pow(10.0, e), 1);
            if (m >= 10.0) { m /= 10.0; e++; }
            if (e > 9) return "9.9E9";
            return m.ToString("0.0", CultureInfo.InvariantCulture) + "E" + e.ToString(CultureInfo.InvariantCulture);
        }
    }
}
