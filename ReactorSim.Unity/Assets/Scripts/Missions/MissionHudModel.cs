using System;

// Pure (no UnityEngine): what the mission panel says. Linked into ReactorSim.Core.Tests. The panel shows in the
// missions mode only; the sandbox has the startup checklist and the assistant screens instead.
namespace ReactorSim.Ui
{
    public sealed class MissionHudModel
    {
        public const int LineBudget = 56;      // characters per row at the panel width

        public bool Visible;
        public string Title = "";
        public string Step = "";               // "Step 2 of 5: Raise boron"
        public string Advisory = "";
        public bool Complete;

        public static MissionHudModel Build(bool missionsMode, string title, int stepIndex, int stepCount,
            string stepTitle, bool completed, string advisoryLine)
        {
            var m = new MissionHudModel();
            if (!missionsMode || string.IsNullOrEmpty(title)) return m;
            m.Visible = true;
            m.Title = Fit(title, LineBudget);
            m.Complete = completed;
            if (completed) m.Step = "Mission complete";
            else if (stepIndex >= 0 && stepIndex < stepCount && !string.IsNullOrEmpty(stepTitle))
                m.Step = Fit("Step " + (stepIndex + 1) + " of " + stepCount + ": " + stepTitle, LineBudget);
            m.Advisory = Fit(advisoryLine, LineBudget);
            return m;
        }

        /// <summary>Single-line, tag-free text cut to width characters with a trailing "..".</summary>
        public static string Fit(string text, int width)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace('\n', ' ').Replace('\r', ' ').Replace('<', '(').Replace('>', ')').Trim();
            if (text.Length <= width) return text;
            return text.Substring(0, Math.Max(0, width - 2)).TrimEnd() + "..";
        }
    }
}
