using System;
using System.IO;
using System.Runtime.CompilerServices;
using Xunit;

// The one repo-file lookup for tests: REACTORSIM_REPO_ROOT when set (an isolated --artifacts-path build cannot walk
// into the repo), else walk up from the test binary, else walk up from this source file's folder (which is inside
// the repo whatever the build output layout is). Global namespace so both test namespaces
// (ReactorSim.Tests and ReactorSim.Core.Tests) see it.
internal static class TestRepo
{
    public static string Find(params string[] relativeParts)
    {
        string rel = Path.Combine(relativeParts);
        string root = Environment.GetEnvironmentVariable("REACTORSIM_REPO_ROOT");
        if (!string.IsNullOrEmpty(root))
        {
            string c = Path.Combine(root, rel);
            Assert.True(File.Exists(c), "REACTORSIM_REPO_ROOT set but missing: " + c);
            return c;
        }
        string found = WalkUp(AppContext.BaseDirectory, rel) ?? WalkUp(Path.GetDirectoryName(ThisFile()), rel);
        if (found == null) Assert.Fail("could not find " + rel + " (set REACTORSIM_REPO_ROOT)");
        return found;
    }

    private static string ThisFile([CallerFilePath] string path = "") { return path; }

    private static string WalkUp(string dir, string rel)
    {
        while (!string.IsNullOrEmpty(dir))
        {
            string c = Path.Combine(dir, rel);
            if (File.Exists(c)) return c;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
