using System.Text.Json.Serialization;
using QRReceiptScanner.Services;

namespace QRReceiptScanner.Models;

/// <summary>
/// A single fiscal receipt, as decoded from its QR code and enriched from the
/// tax administration verification page (suf.purs.gov.rs).
/// </summary>
public sealed class Receipt
{
    /// <summary>Local identifier, also used as the file name on disk.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>The raw text encoded in the QR code (the verification URL).</summary>
    public string VerificationUrl { get; set; } = string.Empty;

    public DateTimeOffset ScannedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>False when only the QR payload could be decoded (no network).</summary>
    public bool DetailsLoaded { get; set; }

    public string? LoadError { get; set; }

    // --- Fiscalisation request (seller) ---
    public string? Tin { get; set; }
    public string? CompanyName { get; set; }
    public string? ShopName { get; set; }
    public string? ShopFullName { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? AdministrativeUnit { get; set; }
    public string? Cashier { get; set; }
    public string? EsirNumber { get; set; }
    public string? BuyerId { get; set; }

    // --- Fiscalisation result ---
    public string? InvoiceStatus { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? InvoiceType { get; set; }
    public string? TransactionType { get; set; }
    public string? InvoiceCounterExtension { get; set; }
    public string? RequestedBy { get; set; }
    public string? SignedBy { get; set; }
    public long? TotalCounter { get; set; }
    public long? TransactionTypeCounter { get; set; }
    public DateTimeOffset? SdcDateTime { get; set; }

    public decimal TotalAmount { get; set; }
    public decimal TotalTaxAmount { get; set; }

    public List<ReceiptItem> Items { get; set; } = new();
    public List<TaxItem> Taxes { get; set; } = new();
    public List<PaymentItem> Payments { get; set; } = new();

    /// <summary>The printed receipt text exactly as the tax administration renders it.</summary>
    public string? Journal { get; set; }

    /// <summary>
    /// Barcodes scanned by hand and kept with this receipt — a warranty label, a loyalty
    /// card, an article code. They print under the fiscal slip, not part of it.
    /// </summary>
    public List<ExtraBarcode> ExtraBarcodes { get; set; } = new();

    [JsonIgnore]
    public string DisplayTitle => CompanyName ?? ShopFullName ?? Tin ?? "Непознат продавац";

    [JsonIgnore]
    public string DisplaySubtitle =>
        $"{(SdcDateTime?.ToString("dd.MM.yyyy HH:mm") ?? ScannedAt.ToString("dd.MM.yyyy HH:mm"))}" +
        $"  ·  {Items.Count} {SerbianText.Items(Items.Count)}";

    [JsonIgnore]
    public string DisplayAmount => TotalAmount.ToString("N2") + " RSD";
}

/// <summary>A barcode added to a receipt after the fact.</summary>
public sealed class ExtraBarcode
{
    /// <summary>The decoded content, exactly as the scanner read it.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>The scanner's name for the symbology, e.g. "Ean13".</summary>
    public string? Format { get; set; }

    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class ReceiptItem
{
    public string? Name { get; set; }
    public string? Gtin { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Total { get; set; }
    /// <summary>Tax label as printed on the receipt, e.g. "Ђ".</summary>
    public string? TaxLabel { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxBaseAmount { get; set; }
    public decimal VatAmount { get; set; }

    [JsonIgnore]
    public string DisplayQuantity => $"{Quantity:0.###} x {UnitPrice:N2}";

    [JsonIgnore]
    public string DisplayTotal => Total.ToString("N2");
}

public sealed class TaxItem
{
    public string? Label { get; set; }
    public string? Name { get; set; }
    public decimal Rate { get; set; }
    public decimal Amount { get; set; }

    [JsonIgnore]
    public string DisplayName => $"{Name} ({Rate:0.##}%)";

    [JsonIgnore]
    public string DisplayAmount => Amount.ToString("N2");
}

public sealed class PaymentItem
{
    public string? Method { get; set; }
    public decimal Amount { get; set; }

    [JsonIgnore]
    public string DisplayAmount => Amount.ToString("N2");
}
