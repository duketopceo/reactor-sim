using System;

// Pure (no UnityEngine): keeps a cursor-following tooltip box fully inside the viewport. Screen pixels, origin at the
// bottom-left (Input.mousePosition). The box hangs below and right of the cursor; when it would run off the right edge it
// flips to the left of the cursor, and it is clamped on every side so it can never be cut off.
namespace ReactorSim.Ui
{
    public static class TooltipClamp
    {
        /// <summary>Left edge and top edge of the box, in pixels from the bottom-left of the screen.</summary>
        public static void Place(float cursorX, float cursorY, float boxW, float boxH, float screenW, float screenH,
            float gap, float edge, out float left, out float top)
        {
            left = cursorX + gap;
            if (left + boxW > screenW - edge) left = cursorX - gap - boxW;
            left = Math.Max(edge, Math.Min(left, Math.Max(edge, screenW - boxW - edge)));

            top = cursorY - gap;
            float lowestTop = Math.Min(boxH + edge, screenH - edge);   // keeps the bottom edge on screen when the box fits
            top = Math.Max(lowestTop, Math.Min(top, screenH - edge));
        }
    }
}
