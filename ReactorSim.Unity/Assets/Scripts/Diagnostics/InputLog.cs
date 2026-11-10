using System.IO;
using System.Text;
using UnityEngine;
using ReactorSim;

/// <summary>
/// Input telemetry for playtest review (R2): dial hover, press, rejected press (with reason), drag start/move/commit/cancel and
/// walking samples. Each event is one JSON line (InputLogFormat) written to Player.log with an "[Input]" tag and appended to
/// input.jsonl in the current session folder, so the owner's logs show the real input path end to end.
/// </summary>
public static class InputLog
{
    private static StreamWriter writer;
    private static string writerDir;

    public static void Event(string name, params string[] keyValues)
    {
        string line = InputLogFormat.Format(Time.unscaledTime, name, keyValues);
        Debug.Log("[Input] " + line);
        Append(line);
    }

    private static void Append(string line)
    {
        var log = SessionLog.Current;
        string dir = log != null ? log.Directory : null;
        if (string.IsNullOrEmpty(dir)) return;
        try
        {
            if (writer == null || writerDir != dir)
            {
                Close();
                writer = new StreamWriter(Path.Combine(dir, "input.jsonl"), true, new UTF8Encoding(false)) { AutoFlush = true };
                writerDir = dir;
            }
            writer.WriteLine(line);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[InputLog] could not write input.jsonl: " + ex.Message);
            Close();
        }
    }

    public static void Close()
    {
        if (writer != null)
        {
            try { writer.Dispose(); } catch (System.Exception) { }
        }
        writer = null;
        writerDir = null;
    }
}
