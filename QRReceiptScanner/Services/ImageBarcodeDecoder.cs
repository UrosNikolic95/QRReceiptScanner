using BarcodeScanning;

namespace QRReceiptScanner.Services;

/// <summary>
/// Reads a QR code out of a still image (a photo or a screenshot) with the same
/// native detector the live camera uses: ML Kit on Android, the Vision framework
/// on iOS and macOS, zxing-cpp on Windows.
/// </summary>
public static class ImageBarcodeDecoder
{
    /// <summary>Returns the QR text, or null when the image holds no readable QR code.</summary>
    public static async Task<string?> DecodeQrAsync(Stream image)
    {
        var results = await Methods.ScanFromImageAsync(image);

        // The detector reports every symbology it knows; receipts only ever carry a QR code.
        return results
            .Where(r => r.BarcodeFormat == BarcodeFormats.QRCode)
            .Select(Text)
            .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
    }

    /// <summary>
    /// The raw content is what was encoded; the display value is the platform's
    /// prettified form and can differ for typed barcodes such as URLs.
    /// </summary>
    public static string? Text(BarcodeResult result) =>
        string.IsNullOrEmpty(result.RawValue) ? result.DisplayValue : result.RawValue;
}
