using QRReceiptScanner.Models;

namespace QRReceiptScanner.Services;

/// <summary>One line of a receipt that bought a given article.</summary>
public sealed record ArticlePurchase(DateTimeOffset Date, string Shop, decimal Quantity, decimal UnitPrice, decimal Total)
{
    public string DisplayDate => Date.ToString("dd.MM.yyyy HH:mm");

    public string DisplayQuantity => $"{Quantity:0.###} x {UnitPrice:N2}";

    public string DisplayTotal => Total.ToString("N2") + " RSD";
}

/// <summary>Everything ever spent on one article, across all receipts.</summary>
/// <param name="Purchases">Every purchase of the article, newest first.</param>
public sealed record ArticleSummary(string Name, IReadOnlyList<ArticlePurchase> Purchases)
{
    public decimal Quantity { get; } = Purchases.Sum(p => p.Quantity);

    public decimal Total { get; } = Purchases.Sum(p => p.Total);

    public int Count => Purchases.Count;

    /// <summary>
    /// What one unit cost on average, weighted by quantity, so a kilo bought at once
    /// counts as much as a kilo bought in grams.
    /// </summary>
    public decimal AveragePrice => Quantity != 0m ? Total / Quantity : Total / Count;

    public string DisplayTotal => Total.ToString("N2") + " RSD";

    public string DisplaySubtitle =>
        $"Количина {Quantity:0.###}  ·  {Count} {SerbianText.Purchases(Count)}  ·  просек {AveragePrice:N2} RSD";
}

/// <summary>Gathers the items of every stored receipt into one list per article.</summary>
public static class ArticleStatistics
{
    /// <summary>
    /// All articles, most money spent first. Names are matched ignoring case and
    /// spacing, since the same product is not always typed the same way at the till.
    /// </summary>
    public static List<ArticleSummary> FromReceipts(IEnumerable<Receipt> receipts)
    {
        var purchases =
            from receipt in receipts
            from item in receipt.Items
            where !string.IsNullOrWhiteSpace(item.Name)
            let name = string.Join(' ', item.Name!.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            select (Name: name, Purchase: new ArticlePurchase(
                SpendingStatistics.PeriodOf(receipt),
                receipt.ShopFullName ?? receipt.DisplayTitle,
                item.Quantity,
                item.UnitPrice,
                item.Total));

        return purchases
            .GroupBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(g =>
            {
                var newestFirst = g.OrderByDescending(p => p.Purchase.Date).ToList();
                // The latest spelling names the article.
                return new ArticleSummary(newestFirst[0].Name, newestFirst.Select(p => p.Purchase).ToList());
            })
            .OrderByDescending(a => a.Total)
            .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
