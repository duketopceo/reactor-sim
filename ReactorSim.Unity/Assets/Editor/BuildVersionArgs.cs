using System;
using System.Text.RegularExpressions;

// Pure logic (no UnityEngine): the version a player build is stamped with. A release build passes `-buildVersion <x.y.z>`
// (the tag); without the argument the current bundleVersion's base is kept. Linked into ReactorSim.Core.Tests.
public static class BuildVersionArgs
{
    public const string Flag = "-buildVersion";

    private static readonly Regex Version = new Regex("^[0-9]+\\.[0-9]+\\.[0-9]+\\z", RegexOptions.CultureInvariant);
    private static readonly Regex BaseVersion = new Regex("^[0-9]+\\.[0-9]+(\\.[0-9]+)?\\z", RegexOptions.CultureInvariant);

    public static bool IsValid(string version)
    {
        return !string.IsNullOrEmpty(version) && Version.IsMatch(version);
    }

    public static bool IsValidBase(string version)
    {
        return !string.IsNullOrEmpty(version) && BaseVersion.IsMatch(version);
    }

    /// <summary>
    /// Version to stamp: the value after -buildVersion when present, else fallbackBase. source is "arg" or "fallback".
    /// Throws FormatException when the flag has no value or the value is not x.y.z (digits only), so a mistyped tag fails the build.
    /// The fallback base is validated too, but is looser: x.y or x.y.z (a project's bundleVersion is often just x.y).
    /// The pattern ends with \z, not $, so a trailing newline is rejected.
    /// </summary>
    public static string Resolve(string[] args, string fallbackBase, out string source)
    {
        if (args != null)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] != Flag) continue;
                string value = i + 1 < args.Length ? args[i + 1] : null;
                if (!IsValid(value))
                    throw new FormatException(Flag + " needs a version like 1.2.3, got " + (value == null ? "nothing" : "\"" + value + "\""));
                source = "arg";
                return value;
            }
        }
        if (!IsValidBase(fallbackBase))
            throw new FormatException("bundleVersion base needs a version like 1.2 or 1.2.3, got " + (fallbackBase == null ? "nothing" : "\"" + fallbackBase + "\""));
        source = "fallback";
        return fallbackBase;
    }
}
