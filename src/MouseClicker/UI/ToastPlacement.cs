using System.Drawing;

namespace MouseClicker;

/// <summary>Pure placement math for the overlay toast: top-centre of a work area, clamped inside it. Unit tested.</summary>
static class ToastPlacement
{
    public const int Margin = 16;

    /// <summary>Top-centre of the work area.</summary>
    public static Point TopCenter(Rectangle workArea, Size toast, int margin = Margin)
    {
        int x = Math.Max(workArea.Left, workArea.Left + (workArea.Width - toast.Width) / 2);
        int y = workArea.Top + margin;
        if (y + toast.Height > workArea.Bottom) y = workArea.Top;
        return new Point(x, y);
    }

    /// <summary>
    /// Top-centre slot that does not overlap any toast already on screen (from any app): while the candidate,
    /// inflated by <paramref name="gap"/>, hits an existing rect, move down to sit just below it. Clamped to the work area bottom.
    /// </summary>
    public static Point Place(Rectangle workArea, Size toast, int margin, int gap, IReadOnlyList<Rectangle> existing)
    {
        var p = TopCenter(workArea, toast, margin);
        var relevant = existing.Where(r => r.IntersectsWith(workArea)).ToList();
        int bottom = Math.Max(workArea.Top, workArea.Bottom - toast.Height);
        int y = p.Y;
        while (y < bottom)
        {
            var cand = new Rectangle(p.X, y, toast.Width, toast.Height);
            cand.Inflate(gap, gap);
            var hits = relevant.Where(r => r.IntersectsWith(cand)).ToList();
            if (hits.Count == 0) break;
            y = hits.Max(r => r.Bottom + gap);
        }
        return new Point(p.X, Math.Min(bottom, y));
    }
}
