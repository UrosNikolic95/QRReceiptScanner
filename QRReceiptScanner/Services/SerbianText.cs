using System.Globalization;

namespace QRReceiptScanner.Services;

/// <summary>
/// The Serbian the app writes around its own numbers. Serbian picks a noun's form from
/// the last digits of the count, so "1 рачун", "2 рачуна" and "5 рачуна" are three
/// different decisions rather than English's one plural.
/// </summary>
public static class SerbianText
{
    /// <summary>
    /// The culture the whole app formats in: amounts as 1.234,56 and month names in
    /// Cyrillic, the way the fiscal receipt itself prints them.
    /// </summary>
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("sr-Cyrl-RS");

    public static string Receipts(int count) => Form(count, "рачун", "рачуна", "рачуна");

    public static string Items(int count) => Form(count, "ставка", "ставке", "ставки");

    public static string Characters(int count) => Form(count, "знак", "знака", "знакова");

    public static string Months(int count) => Form(count, "месец", "месеца", "месеци");

    public static string Years(int count) => Form(count, "година", "године", "година");

    /// <summary>
    /// Counts ending in 1 (but not 11) take the singular; those ending in 2, 3 or 4
    /// (but not 12, 13, 14) take the paucal; everything else takes the genitive plural.
    /// </summary>
    private static string Form(int count, string one, string few, string many)
    {
        var lastTwo = Math.Abs(count) % 100;
        var last = lastTwo % 10;

        if (last == 1 && lastTwo != 11)
            return one;

        if (last is >= 2 and <= 4 && lastTwo is < 12 or > 14)
            return few;

        return many;
    }
}
