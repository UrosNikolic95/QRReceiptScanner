using System.Text.Json;
using QRReceiptScanner.Models;

namespace QRReceiptScanner.Services;

/// <summary>
/// Local, file-backed storage: every receipt is one JSON file in
/// <c>&lt;app data&gt;/receipts</c>. Nothing leaves the device except the lookup
/// against the tax administration when a receipt is scanned.
/// </summary>
public sealed class ReceiptStore
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public string FolderPath { get; }

    public ReceiptStore()
        : this(Path.Combine(FileSystem.AppDataDirectory, "receipts"))
    {
    }

    public ReceiptStore(string folderPath)
    {
        FolderPath = folderPath;
        Directory.CreateDirectory(FolderPath);
    }

    /// <summary>All stored receipts, newest first.</summary>
    public async Task<List<Receipt>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var receipts = new List<Receipt>();

            foreach (var file in Directory.EnumerateFiles(FolderPath, "*.json"))
            {
                try
                {
                    await using var stream = File.OpenRead(file);
                    var receipt = await JsonSerializer.DeserializeAsync<Receipt>(stream, cancellationToken: cancellationToken);
                    if (receipt is not null)
                        receipts.Add(receipt);
                }
                catch (Exception ex) when (ex is JsonException or IOException)
                {
                    // A single corrupt file must not hide the rest of the archive.
                }
            }

            return receipts
                .OrderByDescending(r => r.SdcDateTime ?? r.ScannedAt)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Writes the receipt to disk. Re-scanning the same receipt updates the existing
    /// file instead of creating a duplicate.
    /// </summary>
    public async Task SaveAsync(Receipt receipt, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var stream = File.Create(PathFor(receipt));
            await JsonSerializer.SerializeAsync(stream, receipt, WriteOptions, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task DeleteAsync(Receipt receipt)
    {
        var path = PathFor(receipt);
        if (File.Exists(path))
            File.Delete(path);

        return Task.CompletedTask;
    }

    /// <summary>Finds an already-stored receipt with the same fiscal invoice number.</summary>
    public async Task<Receipt?> FindByInvoiceNumberAsync(string? invoiceNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(invoiceNumber))
            return null;

        var all = await LoadAllAsync(cancellationToken);
        return all.FirstOrDefault(r => string.Equals(r.InvoiceNumber, invoiceNumber, StringComparison.OrdinalIgnoreCase));
    }

    private string PathFor(Receipt receipt) => Path.Combine(FolderPath, Sanitise(receipt.Id) + ".json");

    private static string Sanitise(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
