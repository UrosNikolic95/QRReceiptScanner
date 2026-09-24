using System.Buffers.Binary;
using System.Text;

namespace QRReceiptScanner.Services;

/// <summary>
/// The binary payload carried in the <c>vl</c> query parameter of a Serbian
/// fiscal receipt verification URL (https://suf.purs.gov.rs/v/?vl=...).
///
/// Layout (version 3), all offsets in bytes:
///   0        version
///   1..8     requested-by SDC identifier (ASCII)
///   9..16    signed-by SDC identifier (ASCII)
///   17..20   total counter              (uint32, little endian)
///   21..24   transaction type counter   (uint32, little endian)
///   25..32   total amount x 10000       (int64, little endian)
///   33..40   issue time, Unix millis    (int64, BIG endian)
///   41..     encrypted internal data and signature - not used here
/// </summary>
public sealed record FiscalQrPayload(
    byte Version,
    string RequestedBy,
    string SignedBy,
    long TotalCounter,
    long TransactionTypeCounter,
    decimal TotalAmount,
    DateTimeOffset IssuedAt)
{
    public string InvoiceNumber => $"{RequestedBy}-{SignedBy}-{TotalCounter}";

    private const int MinimumLength = 41;

    /// <summary>
    /// Decodes the payload out of a verification URL. Returns null when the text
    /// is not a verification URL or the payload is malformed.
    /// </summary>
    public static FiscalQrPayload? TryParse(string? qrText)
    {
        if (string.IsNullOrWhiteSpace(qrText))
            return null;

        if (!Uri.TryCreate(qrText.Trim(), UriKind.Absolute, out var uri))
            return null;

        var vl = GetQueryValue(uri, "vl");
        if (vl is null)
            return null;

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(vl);
        }
        catch (FormatException)
        {
            return null;
        }

        if (bytes.Length < MinimumLength)
            return null;

        var span = bytes.AsSpan();
        var totalAmount = BinaryPrimitives.ReadInt64LittleEndian(span[25..33]) / 10000m;
        var unixMillis = BinaryPrimitives.ReadInt64BigEndian(span[33..41]);

        if (unixMillis < 0 || unixMillis > 253_402_300_799_000)
            return null;

        return new FiscalQrPayload(
            Version: span[0],
            RequestedBy: Encoding.ASCII.GetString(span[1..9]).Trim(),
            SignedBy: Encoding.ASCII.GetString(span[9..17]).Trim(),
            TotalCounter: BinaryPrimitives.ReadUInt32LittleEndian(span[17..21]),
            TransactionTypeCounter: BinaryPrimitives.ReadUInt32LittleEndian(span[21..25]),
            TotalAmount: totalAmount,
            IssuedAt: DateTimeOffset.FromUnixTimeMilliseconds(unixMillis).ToLocalTime());
    }

    /// <summary>
    /// Reads a query parameter without letting Uri normalisation touch the value:
    /// the base64 payload contains '+' and '/', which must survive intact.
    /// </summary>
    private static string? GetQueryValue(Uri uri, string name)
    {
        var query = uri.Query.TrimStart('?');
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0)
                continue;

            if (!pair.AsSpan(0, separator).Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;

            return Uri.UnescapeDataString(pair[(separator + 1)..]);
        }

        return null;
    }
}
