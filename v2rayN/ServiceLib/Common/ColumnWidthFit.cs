namespace ServiceLib.Common;

/// <summary>
/// Column widths for the profile grid. WPF's Auto sizing measures cell content only,
/// so a header such as "Delay (ms)" sitting over a one-character cell was clipped to
/// "Dela", while the columns together stopped short of the window and left a dead
/// band on the right. Fitting the header as well as the content, then spending the
/// slack on the widest column, keeps every header readable and the grid flush.
/// </summary>
public static class ColumnWidthFit
{
    /// <summary>Nobody can read a header in less than this.</summary>
    public const double MinWidth = 48;

    public static double[] Fit(double[] contentWidths, double[] headerWidths, double availableWidth)
    {
        ArgumentNullException.ThrowIfNull(contentWidths);
        ArgumentNullException.ThrowIfNull(headerWidths);
        if (headerWidths.Length != contentWidths.Length)
        {
            throw new ArgumentException("One header width per column is required.", nameof(headerWidths));
        }

        var count = contentWidths.Length;
        var widths = new double[count];
        for (var i = 0; i < count; i++)
        {
            widths[i] = Math.Ceiling(Math.Max(contentWidths[i], headerWidths[i]));
            if (widths[i] < MinWidth)
            {
                widths[i] = MinWidth;
            }
        }

        // No usable width (window not laid out yet): keep what the columns asked for.
        if (count == 0 || availableWidth <= 0)
        {
            return widths;
        }

        var total = widths.Sum();
        if (total < availableWidth)
        {
            // Cells only need their content, so a whole column of empty space beside
            // them reads as a broken layout. The widest column is the one that can use
            // the room - in this grid that is the remarks text.
            var widest = 0;
            for (var i = 1; i < count; i++)
            {
                if (widths[i] > widths[widest])
                {
                    widest = i;
                }
            }
            widths[widest] += Math.Floor(availableWidth - total);
            return widths;
        }

        // Wider than the window: take the room back from the widest columns first, so
        // the narrow header-bound ones (Score, Jitter) keep their header intact.
        var excess = total - availableWidth;
        while (excess > 0.5)
        {
            var largest = -1;
            for (var i = 0; i < count; i++)
            {
                if (widths[i] > MinWidth && (largest < 0 || widths[i] > widths[largest]))
                {
                    largest = i;
                }
            }
            if (largest < 0)
            {
                break;
            }

            var take = Math.Min(excess, widths[largest] - MinWidth);
            widths[largest] -= take;
            excess -= take;
        }
        return widths;
    }
}