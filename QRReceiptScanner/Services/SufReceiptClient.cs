using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using QRReceiptScanner.Models;

namespace QRReceiptScanner.Services;

/// <summary>
/// Turns a receipt QR code into a fully populated <see cref="Receipt"/> by
/// fetching the tax administration verification page and its item endpoint.
/// </summary>
public sealed class SufReceiptClient
{
    private readonly HttpClient _http;

    /// <summary>Amounts on the page are formatted "1.234,56".</summary>
    private static readonly NumberFormatInfo SerbianNumbers = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = ".",
    };

    private static readonly string[] DateFormats =
    {
        "d.M.yyyy. H:mm:ss", "dd.MM.yyyy. HH:mm:ss",
        "d.M.yyyy. H:mm", "dd.MM.yyyy. HH:mm",
    };

    public SufReceiptClient(HttpClient http)
    {
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "QRReceiptScanner/1.0");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "sr-Cyrl-RS");
    }

    /// <summary>
    /// True when the text is a receipt verification link: an absolute http(s) URL
    /// carrying the <c>vl</c> payload that every fiscal receipt QR code encodes.
    /// Anything else (a typed receipt number, a 1D barcode) is not worth a lookup.
    /// </summary>
    public static bool IsVerificationLink(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme is not ("https" or "http"))
            return false;

        return uri.Query.Contains("vl=", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds a receipt from scanned QR text. The QR payload alone already yields
    /// the amount, timestamp and invoice number, so a receipt is always returned;
    /// network failures are recorded in <see cref="Receipt.LoadError"/> instead of
    /// throwing, and the receipt can be refreshed later.
    /// </summary>
    public async Task<Receipt> LoadAsync(string qrText, CancellationToken cancellationToken = default)
    {
        var receipt = new Receipt { VerificationUrl = qrText.Trim() };
        await RefreshAsync(receipt, cancellationToken);
        return receipt;
    }

    /// <summary>Re-reads an existing receipt from the verification service in place.</summary>
    public async Task RefreshAsync(Receipt receipt, CancellationToken cancellationToken = default)
    {
        receipt.LoadError = null;

        var payload = FiscalQrPayload.TryParse(receipt.VerificationUrl);
        if (payload is not null)
        {
            receipt.RequestedBy = payload.RequestedBy;
            receipt.SignedBy = payload.SignedBy;
            receipt.TotalCounter = payload.TotalCounter;
            receipt.TransactionTypeCounter = payload.TransactionTypeCounter;
            receipt.TotalAmount = payload.TotalAmount;
            receipt.SdcDateTime = payload.IssuedAt;
            receipt.InvoiceNumber = payload.InvoiceNumber;
        }
        else if (!IsVerificationLink(receipt.VerificationUrl))
        {
            receipt.LoadError = "Није линк за проверу рачуна.";
            return;
        }

        try
        {
            using var response = await _http.GetAsync(receipt.VerificationUrl, cancellationToken);
            response.EnsureSuccessStatusCode();
            var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(cancellationToken));

            ParsePage(html, receipt);

            var invoiceNumber = Capture(html, @"viewModel\.InvoiceNumber\('([^']+)'\)") ?? receipt.InvoiceNumber;
            var token = Capture(html, @"viewModel\.Token\('([^']+)'\)");

            if (!string.IsNullOrEmpty(invoiceNumber) && !string.IsNullOrEmpty(token))
                receipt.Items = await LoadItemsAsync(response.RequestMessage?.RequestUri, invoiceNumber, token, cancellationToken);

            if (receipt.TotalTaxAmount == 0 && receipt.Items.Count > 0)
                receipt.TotalTaxAmount = receipt.Items.Sum(i => i.VatAmount);

            receipt.DetailsLoaded = true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            receipt.DetailsLoaded = false;
            receipt.LoadError = ex.Message;
        }
    }

    private static void ParsePage(string html, Receipt receipt)
    {
        receipt.Tin = Element(html, "tinLabel") ?? receipt.Tin;
        receipt.ShopFullName = Element(html, "shopFullNameLabel") ?? receipt.ShopFullName;
        receipt.Address = Element(html, "addressLabel") ?? receipt.Address;
        receipt.City = Element(html, "cityLabel") ?? receipt.City;
        receipt.AdministrativeUnit = Element(html, "administrativeUnitLabel") ?? receipt.AdministrativeUnit;
        receipt.BuyerId = Element(html, "buyerIdLabel") ?? receipt.BuyerId;
        receipt.RequestedBy = Element(html, "requestedByLabel") ?? receipt.RequestedBy;
        receipt.SignedBy = Element(html, "signedByLabel") ?? receipt.SignedBy;
        receipt.InvoiceType = Element(html, "invoiceTypeId") ?? receipt.InvoiceType;
        receipt.TransactionType = Element(html, "transactionTypeId") ?? receipt.TransactionType;
        receipt.InvoiceStatus = Element(html, "invoiceStatusLabel") ?? receipt.InvoiceStatus;
        receipt.InvoiceNumber = Element(html, "invoiceNumberLabel") ?? receipt.InvoiceNumber;
        receipt.InvoiceCounterExtension = Element(html, "invoiceCounterExtensionLabel") ?? receipt.InvoiceCounterExtension;

        if (ParseAmount(Element(html, "totalAmountLabel")) is { } total)
            receipt.TotalAmount = total;
        if (ParseLong(Element(html, "totalCounterLabel")) is { } totalCounter)
            receipt.TotalCounter = totalCounter;
        if (ParseLong(Element(html, "transactionTypeCounterLabel")) is { } typeCounter)
            receipt.TransactionTypeCounter = typeCounter;
        if (ParseDate(Element(html, "sdcDateTimeLabel")) is { } issued)
            receipt.SdcDateTime = issued;

        // The print-preview block carries a few fields that have no id of their own.
        receipt.CompanyName = PrintField(html, "Предузеће") ?? receipt.CompanyName;
        receipt.ShopName = PrintField(html, "Место продаје") ?? receipt.ShopName;
        receipt.Cashier = PrintField(html, "Касир") ?? receipt.Cashier;
        receipt.EsirNumber = PrintField(html, "ЕСИР број") ?? receipt.EsirNumber;

        var journal = ParseJournal(html);
        if (journal is not null)
        {
            receipt.Journal = journal;
            ParseJournalDetails(journal, receipt);
        }
    }

    private async Task<List<ReceiptItem>> LoadItemsAsync(Uri? pageUri, string invoiceNumber, string token, CancellationToken cancellationToken)
    {
        // The page loads its item table over this endpoint, form-encoded (not JSON).
        var baseUri = pageUri ?? new Uri("https://suf.purs.gov.rs/");
        var endpoint = new Uri(baseUri, "/specifications");

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["invoiceNumber"] = invoiceNumber,
            ["token"] = token,
        });

        using var response = await _http.PostAsync(endpoint, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<SpecificationsResponse>(stream, cancellationToken: cancellationToken);

        if (payload is not { Success: true, Items: not null })
            return new List<ReceiptItem>();

        return payload.Items.Select(i => new ReceiptItem
        {
            Name = i.Name?.Trim(),
            Gtin = string.IsNullOrWhiteSpace(i.Gtin) ? null : i.Gtin,
            Quantity = i.Quantity,
            UnitPrice = i.UnitPrice,
            Total = i.Total,
            TaxLabel = i.Label,
            TaxRate = i.LabelRate,
            TaxBaseAmount = i.TaxBaseAmount,
            VatAmount = i.VatAmount,
        }).ToList();
    }

    /// <summary>
    /// Pulls the payment breakdown and the tax recap out of the printed receipt text,
    /// which is the only place on the page that carries them.
    /// </summary>
    private static void ParseJournalDetails(string journal, Receipt receipt)
    {
        var lines = journal.Split('\n').Select(l => l.TrimEnd()).ToArray();

        receipt.Cashier ??= Capture(journal, @"(?m)^Касир:\s*(\S.*?)\s*$");
        receipt.EsirNumber ??= Capture(journal, @"(?m)^ЕСИР број:\s*(\S.*?)\s*$");

        if (ParseAmount(Capture(journal, @"Укупан износ пореза:\s*([\d.,]+)")) is { } totalTax)
            receipt.TotalTaxAmount = totalTax;

        var payments = new List<PaymentItem>();
        var totalIndex = Array.FindIndex(lines, l => l.StartsWith("Укупан износ:", StringComparison.Ordinal));
        if (totalIndex >= 0)
        {
            for (var i = totalIndex + 1; i < lines.Length && !IsSeparator(lines[i]); i++)
            {
                var match = Regex.Match(lines[i], @"^(.+?):\s+([\d.,]+)$");
                if (match.Success && ParseAmount(match.Groups[2].Value) is { } amount)
                    payments.Add(new PaymentItem { Method = match.Groups[1].Value.Trim(), Amount = amount });
            }
        }
        receipt.Payments = payments;

        var taxes = new List<TaxItem>();
        var headerIndex = Array.FindIndex(lines, l =>
            l.Contains("Стопа", StringComparison.Ordinal) && l.Contains("Порез", StringComparison.Ordinal));
        if (headerIndex >= 0)
        {
            for (var i = headerIndex + 1; i < lines.Length && !IsSeparator(lines[i]); i++)
            {
                var match = Regex.Match(lines[i], @"^(\S+)\s+(.+?)\s+([\d.,]+)\s*%\s+([\d.,]+)$");
                if (!match.Success)
                    continue;

                taxes.Add(new TaxItem
                {
                    Label = match.Groups[1].Value,
                    Name = match.Groups[2].Value.Trim(),
                    Rate = ParseAmount(match.Groups[3].Value) ?? 0m,
                    Amount = ParseAmount(match.Groups[4].Value) ?? 0m,
                });
            }
        }
        receipt.Taxes = taxes;

        // The journal header is the only source for the trading name when the
        // print preview is missing.
        if (string.IsNullOrEmpty(receipt.CompanyName))
        {
            var header = lines.Select(l => l.Trim()).Where(l => l.Length > 0).Take(4).ToArray();
            if (header.Length >= 3 && header[1] == receipt.Tin)
                receipt.CompanyName = header[2];
        }
    }

    private static bool IsSeparator(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length == 0 || trimmed.All(c => c is '=' or '-');
    }

    private static string? ParseJournal(string html)
    {
        var match = Regex.Match(html, @"<pre[^>]*>([\s\S]*?)</pre>", RegexOptions.IgnoreCase);
        if (!match.Success)
            return null;

        // The journal embeds the verification image as an <img> tag; drop all markup.
        var text = Regex.Replace(match.Groups[1].Value, @"<[^>]*>", string.Empty);
        return text.Replace("\r\n", "\n").Trim('\n', ' ');
    }

    private static string? Element(string html, string id)
    {
        var match = Regex.Match(html, $@"id=""{Regex.Escape(id)}""[^>]*>([^<]*)", RegexOptions.IgnoreCase);
        return match.Success ? Clean(match.Groups[1].Value) : null;
    }

    private static string? PrintField(string html, string label)
    {
        var match = Regex.Match(html, $@"{Regex.Escape(label)}:</strong>[^<]*<span[^>]*>([^<]*)</span>", RegexOptions.IgnoreCase);
        return match.Success ? Clean(match.Groups[1].Value) : null;
    }

    private static string? Capture(string text, string pattern)
    {
        var match = Regex.Match(text, pattern);
        return match.Success ? Clean(match.Groups[1].Value) : null;
    }

    private static string? Clean(string? value)
    {
        if (value is null)
            return null;

        var collapsed = Regex.Replace(value.Replace(' ', ' '), @"\s+", " ").Trim();
        return collapsed.Length == 0 ? null : collapsed;
    }

    private static decimal? ParseAmount(string? text) =>
        text is not null && decimal.TryParse(text, NumberStyles.Number, SerbianNumbers, out var value) ? value : null;

    private static long? ParseLong(string? text) =>
        text is not null && long.TryParse(text, NumberStyles.Number, SerbianNumbers, out var value) ? value : null;

    private static DateTimeOffset? ParseDate(string? text)
    {
        if (text is null)
            return null;

        return DateTime.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? new DateTimeOffset(parsed, TimeZoneInfo.Local.GetUtcOffset(parsed))
            : null;
    }

    private sealed class SpecificationsResponse
    {
        [JsonPropertyName("success")] public bool Success { get; set; }
        [JsonPropertyName("items")] public List<SpecificationItem>? Items { get; set; }
    }

    private sealed class SpecificationItem
    {
        [JsonPropertyName("gtin")] public string? Gtin { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("quantity")] public decimal Quantity { get; set; }
        [JsonPropertyName("total")] public decimal Total { get; set; }
        [JsonPropertyName("unitPrice")] public decimal UnitPrice { get; set; }
        [JsonPropertyName("label")] public string? Label { get; set; }
        [JsonPropertyName("labelRate")] public decimal LabelRate { get; set; }
        [JsonPropertyName("taxBaseAmount")] public decimal TaxBaseAmount { get; set; }
        [JsonPropertyName("vatAmount")] public decimal VatAmount { get; set; }
    }
}
