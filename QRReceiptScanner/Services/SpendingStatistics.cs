using System.Globalization;
using QRReceiptScanner.Models;

namespace QRReceiptScanner.Services;

/// <summary>One period on a statistics chart: a month or a year of spending.</summary>
/// <param name="AxisLabel">Short label under the bar, e.g. "Sep" or "2026".</param>
/// <param name="FullLabel">Spelled-out period for the readout, e.g. "September 2026".</param>
public sealed record SpendingBucket(string AxisLabel, string FullLabel, decimal Total, int Count);

/// <summary>
/// Groups stored receipts into the periods the statistics page charts: the last
/// twelve months, and every year on record.
/// </summary>
public static class SpendingStatistics
{
    /// <summary>
    /// When a receipt counts as spent. The fiscalisation time is the real date;
    /// the scan time is the fallback for a receipt whose details never loaded.
    /// </summary>
    public static DateTimeOffset PeriodOf(Receipt receipt) => receipt.SdcDateTime ?? receipt.ScannedAt;

    /// <summary>
    /// The twelve months ending with the current one, oldest first. Months without a
    /// receipt are kept at zero so the axis stays a continuous run of time.
    /// </summary>
    public static List<SpendingBucket> LastTwelveMonths(IEnumerable<Receipt> receipts, DateTimeOffset now)
    {
        var totals = receipts
            .GroupBy(r => new DateTime(PeriodOf(r).Year, PeriodOf(r).Month, 1))
            .ToDictionary(g => g.Key, g => (Total: g.Sum(r => r.TotalAmount), Count: g.Count()));

        var first = new DateTime(now.Year, now.Month, 1).AddMonths(-11);
        var months = new List<SpendingBucket>(12);

        for (var i = 0; i < 12; i++)
        {
            var month = first.AddMonths(i);
            totals.TryGetValue(month, out var bucket);
            months.Add(new SpendingBucket(
                AxisLabel: month.ToString("MMM", CultureInfo.CurrentCulture),
                FullLabel: month.ToString("MMMM yyyy", CultureInfo.CurrentCulture),
                Total: bucket.Total,
                Count: bucket.Count));
        }

        return months;
    }

    /// <summary>
    /// Every year from the first receipt to the last, oldest first. Empty years in
    /// between are kept at zero, for the same reason months are.
    /// </summary>
    public static List<SpendingBucket> ByYear(IEnumerable<Receipt> receipts)
    {
        var totals = receipts
            .GroupBy(r => PeriodOf(r).Year)
            .ToDictionary(g => g.Key, g => (Total: g.Sum(r => r.TotalAmount), Count: g.Count()));

        if (totals.Count == 0)
            return new List<SpendingBucket>();

        var years = new List<SpendingBucket>();
        for (var year = totals.Keys.Min(); year <= totals.Keys.Max(); year++)
        {
            totals.TryGetValue(year, out var bucket);
            var label = year.ToString(CultureInfo.InvariantCulture);
            years.Add(new SpendingBucket(label, label, bucket.Total, bucket.Count));
        }

        return years;
    }
}
