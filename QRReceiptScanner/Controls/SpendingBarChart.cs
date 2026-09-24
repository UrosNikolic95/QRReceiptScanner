using QRReceiptScanner.Services;

namespace QRReceiptScanner.Controls;

/// <summary>
/// Draws one period-over-period column chart: a single series of totals on one
/// baseline, one colour for every bar, recessive grid and axis ink.
///
/// Deliberately hand-drawn rather than taken from a charting package: the app
/// already pays for one native barcode library, and a second drawing dependency
/// buys nothing a few hundred lines of <see cref="IDrawable"/> do not.
/// </summary>
public sealed class SpendingBarChart : IDrawable
{
    // Mark specs: bars are capped rather than filling their slot, the cap is
    // rounded while the baseline end stays square, and touching bars are held
    // apart by a gap in the surface colour instead of a stroke.
    private const float MaxBarWidth = 24f;
    private const float BarGap = 2f;
    private const float CapRadius = 4f;

    private const float AxisFontSize = 10f;
    private const float ValueFontSize = 11f;
    private const float TopPadding = 20f;
    private const float RightPadding = 6f;
    private const float LabelStripHeight = 16f;
    private const int GridLineCount = 4;

    /// <summary>Plot geometry from the last draw, so a tap can be mapped back to a bar.</summary>
    private RectF _plot;
    private float _bandWidth;

    public IReadOnlyList<SpendingBucket> Buckets { get; set; } = Array.Empty<SpendingBucket>();

    /// <summary>Index of the bar the reader is pointing at, or -1.</summary>
    public int SelectedIndex { get; set; } = -1;

    public Color Series { get; set; } = Color.FromArgb("#2a78d6");
    public Color Surface { get; set; } = Colors.White;
    public Color PrimaryInk { get; set; } = Color.FromArgb("#0b0b0b");
    public Color MutedInk { get; set; } = Color.FromArgb("#898781");
    public Color GridLine { get; set; } = Color.FromArgb("#e1e0d9");
    public Color Baseline { get; set; } = Color.FromArgb("#c3c2b7");

    public void Draw(ICanvas canvas, RectF bounds)
    {
        _plot = RectF.Zero;

        if (Buckets.Count == 0)
            return;

        var max = Buckets.Max(b => b.Total);
        var (axisMax, step) = NiceScale(max);

        // One unit for the whole axis, chosen from its top tick: mixing "5,000" and
        // "10k" down a single column makes the reader convert in their head.
        var unit = TickUnit.For(axisMax);

        // The gutter is as wide as the widest tick actually renders, so the ticks
        // never collide with the bars and never get clipped at the left edge.
        var gutter = 0f;
        for (var value = 0m; value <= axisMax; value += step)
            gutter = Math.Max(gutter, MeasureWidth(canvas, unit.Format(value), AxisFontSize));
        gutter += 8f;

        _plot = new RectF(
            bounds.X + gutter,
            bounds.Y + TopPadding,
            Math.Max(1f, bounds.Width - gutter - RightPadding),
            Math.Max(1f, bounds.Height - TopPadding - LabelStripHeight));

        DrawGrid(canvas, axisMax, step, unit);
        DrawBars(canvas, axisMax);
        DrawTooltip(canvas, bounds, axisMax);
    }

    private void DrawGrid(ICanvas canvas, decimal axisMax, decimal step, TickUnit unit)
    {
        canvas.FontSize = AxisFontSize;
        canvas.StrokeSize = 1f;

        for (var value = 0m; value <= axisMax; value += step)
        {
            var y = YFor(value, axisMax);

            // Hairline, solid, one step off the surface: present but never competing
            // with the bars. The baseline is the one line allowed to be darker.
            canvas.StrokeColor = value == 0m ? Baseline : GridLine;
            canvas.DrawLine(_plot.Left, y, _plot.Right, y);

            // Drawn to a point, not a box: a box wraps a tick that measures wider
            // than its gutter, and half a number is worse than a narrower chart.
            canvas.FontColor = MutedInk;
            canvas.DrawString(unit.Format(value), _plot.Left - 6f, y + AxisFontSize * 0.36f,
                HorizontalAlignment.Right);
        }
    }

    private void DrawBars(ICanvas canvas, decimal axisMax)
    {
        _bandWidth = _plot.Width / Buckets.Count;
        var barWidth = Math.Min(MaxBarWidth, Math.Max(1f, _bandWidth - BarGap));

        // Only the biggest bar is labelled outright, plus whichever bar is being
        // pointed at. A number over every column reads as noise and goes unread.
        var peak = IndexOfPeak();

        for (var i = 0; i < Buckets.Count; i++)
        {
            var bucket = Buckets[i];
            if (bucket.Total <= 0m)
                continue;

            var centre = _plot.Left + (i + 0.5f) * _bandWidth;
            var top = YFor(bucket.Total, axisMax);
            var height = Math.Max(1f, _plot.Bottom - top);
            var bar = new RectF(centre - barWidth / 2f, top, barWidth, height);

            canvas.FillColor = Series;
            var radius = Math.Min(CapRadius, height / 2f);
            canvas.FillRoundedRectangle(bar, radius, radius, 0f, 0f);

            // The peak carries a standing label; the pointed-at bar gets the tooltip
            // instead, so the two never print on top of each other.
            if (i == peak && i != SelectedIndex)
                DrawCapLabel(canvas, bucket.Total, bar);
        }

        canvas.FontSize = AxisFontSize;
        canvas.FontColor = MutedInk;
        for (var i = 0; i < Buckets.Count; i++)
        {
            var centre = _plot.Left + (i + 0.5f) * _bandWidth;
            canvas.DrawString(Buckets[i].AxisLabel, centre, _plot.Bottom + AxisFontSize + 2f,
                HorizontalAlignment.Center);
        }
    }

    /// <summary>
    /// Puts the value on the bar's cap, but only when it fits the band: a label wider
    /// than its slot would run into its neighbour, and a clipped number is worse than
    /// no number. The readout line under the chart carries it either way.
    /// </summary>
    private void DrawCapLabel(ICanvas canvas, decimal total, RectF bar)
    {
        var text = FormatValue(total);
        canvas.FontSize = ValueFontSize;

        if (MeasureWidth(canvas, text, ValueFontSize) > _bandWidth - 2f)
            return;

        // Text never wears the series colour; the bar underneath carries identity.
        canvas.FontColor = PrimaryInk;
        canvas.DrawString(text, bar.Center.X, bar.Top - 5f, HorizontalAlignment.Center);
    }

    /// <summary>
    /// The pointed-at period and its total, in a small card beside the bar. It is drawn
    /// on the canvas rather than written to a label under the chart: a label that grows
    /// on hover re-lays out the page and shifts the scroll position under the reader.
    /// </summary>
    private void DrawTooltip(ICanvas canvas, RectF bounds, decimal axisMax)
    {
        if (SelectedIndex < 0 || SelectedIndex >= Buckets.Count)
            return;

        var bucket = Buckets[SelectedIndex];
        var text = bucket.Count == 0
            ? $"{bucket.FullLabel}  ·  нема потрошње"
            : $"{bucket.FullLabel}  ·  {bucket.Count} {SerbianText.Receipts(bucket.Count)}  ·  {bucket.Total:N2} RSD";

        canvas.FontSize = ValueFontSize;
        var width = Math.Min(bounds.Width, MeasureWidth(canvas, text, ValueFontSize) + 16f);
        const float height = 22f;

        var centre = _plot.Left + (SelectedIndex + 0.5f) * _bandWidth;
        var x = Math.Clamp(centre - width / 2f, bounds.Left, Math.Max(bounds.Left, bounds.Right - width));

        // Above the bar by default, below it when the bar is tall enough to crowd the top.
        var barTop = bucket.Total > 0m ? YFor(bucket.Total, axisMax) : _plot.Bottom;
        var y = barTop - height - 6f;
        if (y < bounds.Top)
            y = barTop + 6f;

        var card = new RectF(x, y, width, height);
        canvas.FillColor = Surface;
        canvas.FillRoundedRectangle(card, 4f);
        canvas.StrokeColor = Baseline;
        canvas.StrokeSize = 1f;
        canvas.DrawRoundedRectangle(card, 4f);

        canvas.FontColor = PrimaryInk;
        canvas.DrawString(text, card.Center.X, card.Center.Y + ValueFontSize * 0.36f, HorizontalAlignment.Center);
    }

    /// <summary>The bar under a pointer or finger, or -1 when the point misses the plot.</summary>
    public int HitTest(Point point)
    {
        if (Buckets.Count == 0 || _plot.Width <= 0 || _bandWidth <= 0)
            return -1;

        if (point.X < _plot.Left || point.X > _plot.Right)
            return -1;

        // Vertically generous on purpose: the hit target is the whole column band,
        // not the drawn bar, so a short bar is no harder to point at than a tall one.
        if (point.Y < _plot.Top - TopPadding || point.Y > _plot.Bottom + LabelStripHeight)
            return -1;

        var index = (int)((point.X - _plot.Left) / _bandWidth);
        return index >= 0 && index < Buckets.Count ? index : -1;
    }

    private int IndexOfPeak()
    {
        var peak = -1;
        for (var i = 0; i < Buckets.Count; i++)
        {
            if (Buckets[i].Total > 0m && (peak < 0 || Buckets[i].Total > Buckets[peak].Total))
                peak = i;
        }

        return peak;
    }

    private float YFor(decimal value, decimal axisMax) =>
        _plot.Bottom - (float)(value / axisMax) * _plot.Height;

    /// <summary>
    /// Rounds the axis up to a clean maximum built from a 1 / 2 / 2.5 / 5 step, so the
    /// ticks read as whole numbers instead of whatever the largest receipt happened to be.
    /// </summary>
    private static (decimal Max, decimal Step) NiceScale(decimal max)
    {
        if (max <= 0m)
            return (1m, 1m);

        var rough = max / GridLineCount;
        var magnitude = (decimal)Math.Pow(10, Math.Floor(Math.Log10((double)rough)));
        var normalised = rough / magnitude;

        var step = normalised switch
        {
            <= 1m => 1m,
            <= 2m => 2m,
            <= 2.5m => 2.5m,
            <= 5m => 5m,
            _ => 10m,
        } * magnitude;

        return (Math.Ceiling(max / step) * step, step);
    }

    /// <summary>
    /// The scale every tick on one axis is written in, picked from the axis maximum
    /// so the whole column reads in the same unit.
    /// </summary>
    private readonly record struct TickUnit(decimal Divisor, string Suffix)
    {
        public static TickUnit For(decimal axisMax) => axisMax switch
        {
            >= 1_000_000m => new TickUnit(1_000_000m, " мил"),
            >= 10_000m => new TickUnit(1_000m, " хиљ"),
            _ => new TickUnit(1m, string.Empty),
        };

        public string Format(decimal value) =>
            Divisor == 1m
                ? value.ToString("#,##0")
                : (value / Divisor).ToString("0.##") + Suffix;
    }

    private static string FormatValue(decimal value) => value switch
    {
        >= 1_000_000m => (value / 1_000_000m).ToString("0.#") + " мил",
        >= 100_000m => (value / 1_000m).ToString("0.#") + " хиљ",
        _ => value.ToString("#,##0"),
    };

    private static float MeasureWidth(ICanvas canvas, string text, float fontSize)
    {
        canvas.FontSize = fontSize;
        return canvas.GetStringSize(text, Microsoft.Maui.Graphics.Font.Default, fontSize).Width;
    }
}
