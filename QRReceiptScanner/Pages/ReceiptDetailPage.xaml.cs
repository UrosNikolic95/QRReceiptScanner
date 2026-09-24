using QRCoder;
using System.Text.RegularExpressions;
using QRReceiptScanner.Models;
using QRReceiptScanner.Services;

namespace QRReceiptScanner.Pages
{
    /// <summary>One label/value line in the details section.</summary>
    public sealed record DetailRow(string Label, string Value);

    public partial class ReceiptDetailPage : ContentPage, IQueryAttributable
    {
        public const string ReceiptKey = "receipt";

        private readonly ReceiptStore _store;

        /// <summary>The drawn barcodes, kept so they can be resized with the slip.</summary>
        private readonly List<GraphicsView> _barcodeViews = new();

        public ReceiptDetailPage(ReceiptStore store)
        {
            InitializeComponent();
            _store = store;
            BindingContext = this;
        }

        public Receipt? Receipt { get; private set; }

        public List<DetailRow> Details { get; private set; } = new();

        public void ApplyQueryAttributes(IDictionary<string, object> query)
        {
            if (query.TryGetValue(ReceiptKey, out var value) && value is Receipt receipt)
            {
                Receipt = receipt;
                Render();
            }
        }

        private void Render()
        {
            if (Receipt is not { } receipt)
                return;

            Title = receipt.CompanyName ?? "Рачун";
            TitleLabel.Text = receipt.DisplayTitle;
            AmountLabel.Text = receipt.DisplayAmount;
            StatusLabel.Text = receipt.InvoiceStatus ?? (receipt.DetailsLoaded ? "Проверен" : "Још није проверен");

            WarningLabel.IsVisible = receipt.LoadError is not null;
            WarningLabel.Text = receipt.LoadError is null
                ? string.Empty
                : $"Детаљи нису могли да се учитају: {receipt.LoadError}";

            Details = BuildDetails(receipt);
            OnPropertyChanged(nameof(Details));
            OnPropertyChanged(nameof(Receipt));

            ItemsHeading.IsVisible = ItemsHost.IsVisible = receipt.Items.Count > 0;
            TaxesHeading.IsVisible = TaxesHost.IsVisible = receipt.Taxes.Count > 0;
            PaymentsHeading.IsVisible = PaymentsHost.IsVisible = receipt.Payments.Count > 0;

            var hasJournal = !string.IsNullOrWhiteSpace(receipt.Journal);
            var hasQr = !string.IsNullOrWhiteSpace(receipt.VerificationUrl);
            var hasExtras = receipt.ExtraBarcodes.Count > 0;
            JournalHeading.IsVisible = JournalBorder.IsVisible = hasJournal || hasQr || hasExtras;
            JournalScroll.IsVisible = hasJournal || hasQr || hasExtras;
            JournalScroll.WidthRequest = -1;  // Re-measured against the new text.

            var (body, closingLine) = SplitAtClosingLine(TightenInvoiceNumber(receipt.Journal));
            JournalLabel.IsVisible = body.Length > 0;
            JournalLabel.Text = body;
            JournalFooterLabel.IsVisible = closingLine.Length > 0;
            JournalFooterLabel.Text = closingLine;

            QrImage.IsVisible = hasQr;
            QrImage.Source = hasQr ? RenderQr(receipt.VerificationUrl) : null;

            RenderExtraBarcodes(receipt);
        }

        /// <summary>
        /// Redraws when coming back from the barcode scanner, which adds to the same
        /// receipt instance this page is holding.
        /// </summary>
        protected override void OnAppearing()
        {
            base.OnAppearing();
            Render();
        }

        /// <summary>
        /// Prints the hand-added barcodes under the slip: the bars, and the value beneath
        /// them as a label does. A symbology this app cannot draw still shows its value,
        /// so nothing a user scanned is ever silently dropped.
        /// </summary>
        private void RenderExtraBarcodes(Receipt receipt)
        {
            ExtraBarcodesHost.Clear();
            _barcodeViews.Clear();
            ExtraBarcodesHost.IsVisible = receipt.ExtraBarcodes.Count > 0;

            foreach (var extra in receipt.ExtraBarcodes)
            {
                var block = new VerticalStackLayout { Spacing = 2 };
                var encoded = BarcodeEncoder.Encode(extra.Value, extra.Format);

                if (encoded is not null)
                {
                    var bars = new GraphicsView
                    {
                        Drawable = new BarcodeDrawable(encoded),
                        HeightRequest = 56,
                        // A starting width so the bars draw before the slip is measured;
                        // OnJournalSizeChanged then scales them to the slip.
                        WidthRequest = 240,
                        HorizontalOptions = LayoutOptions.Center,
                    };
                    _barcodeViews.Add(bars);
                    block.Add(bars);
                }

                block.Add(new Label
                {
                    Text = encoded?.Text ?? extra.Value,
                    FontFamily = "Monospace",
                    FontSize = 11,
                    HorizontalTextAlignment = TextAlignment.Center,
                    HorizontalOptions = LayoutOptions.Center,
                });

                ExtraBarcodesHost.Add(block);
            }
        }

        /// <summary>
        /// Puts the invoice number back on a single line. The tax service prints the slip
        /// forty columns wide, so a number one or two characters too long spills its tail
        /// onto a line of its own. Dropping the space after the colon wins back the column
        /// the number needs, and the spilled tail is pulled up behind it. Every other line
        /// stays exactly as the service printed it.
        /// </summary>
        private static string? TightenInvoiceNumber(string? journal)
        {
            if (string.IsNullOrEmpty(journal))
                return journal;

            var lines = journal.Split('\n').ToList();
            var slipWidth = lines.Max(l => l.TrimEnd().Length);

            for (var i = 0; i < lines.Count; i++)
            {
                var match = InvoiceNumberLine().Match(lines[i]);
                if (!match.Success)
                    continue;

                var number = match.Groups[2].Value;

                // A full-width line means the number ran out of columns: the rest of it is
                // alone on the line below, flush left, which no printed field ever is.
                if (lines[i].TrimEnd().Length >= slipWidth &&
                    i + 1 < lines.Count && SpilledTail().IsMatch(lines[i + 1]))
                {
                    number += lines[i + 1].Trim();
                    lines.RemoveAt(i + 1);
                }

                lines[i] = match.Groups[1].Value + number;
                break;
            }

            return string.Join('\n', lines);
        }

        /// <summary>The invoice-number line, in either the Cyrillic or the Latin rendering.</summary>
        [GeneratedRegex(@"^((?:ПФР број рачуна|PFR broj ra[cč]una):)[ \t]*(\S.*?)[ \t]*$")]
        private static partial Regex InvoiceNumberLine();

        [GeneratedRegex(@"^\S+[ \t]*$")]
        private static partial Regex SpilledTail();

        /// <summary>
        /// Cuts the printed receipt in two just above its closing line
        /// ("КРАЈ ФИСКАЛНОГ РАЧУНА"), which the paper slip prints below the QR code.
        /// The row of "=" signs above that line stays with the body, so the code lands
        /// between the two exactly as it does on paper. A receipt with no closing line
        /// keeps its whole text in the body and the code goes underneath.
        /// </summary>
        private static (string Body, string ClosingLine) SplitAtClosingLine(string? journal)
        {
            if (string.IsNullOrWhiteSpace(journal))
                return (string.Empty, string.Empty);

            var lines = journal.Split('\n');
            for (var i = lines.Length - 1; i >= 0; i--)
            {
                if (!IsClosingLine(lines[i]))
                    continue;

                return (string.Join('\n', lines[..i]).TrimEnd('\n'),
                        string.Join('\n', lines[i..]).Trim('\n'));
            }

            return (journal, string.Empty);
        }

        /// <summary>The receipt's last line, in either the Cyrillic or the Latin rendering.</summary>
        private static bool IsClosingLine(string line) =>
            line.Contains("КРАЈ", StringComparison.Ordinal) ||
            line.Contains("KRAJ", StringComparison.Ordinal);

        /// <summary>The same QR code that is printed on the paper receipt: its verification link.</summary>
        private static ImageSource RenderQr(string text)
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
            var png = new PngByteQRCode(data).GetGraphic(pixelsPerModule: 4);
            return ImageSource.FromStream(() => new MemoryStream(png));
        }

        private static List<DetailRow> BuildDetails(Receipt receipt)
        {
            var rows = new List<DetailRow>();

            Add("Фирма", receipt.CompanyName);
            Add("ПИБ", receipt.Tin);
            Add("Продавница", receipt.ShopFullName ?? receipt.ShopName);
            Add("Адреса", receipt.Address);
            Add("Град", receipt.City);
            Add("Општина", receipt.AdministrativeUnit);
            Add("Касир", receipt.Cashier);
            Add("ЕСИР број", receipt.EsirNumber);
            Add("ИД купца", receipt.BuyerId);
            Add("Број рачуна", receipt.InvoiceNumber);
            Add("Врста рачуна", receipt.InvoiceType);
            Add("Трансакција", receipt.TransactionType);
            Add("Бројач", receipt.TransactionTypeCounter is { } t && receipt.TotalCounter is { } a
                ? $"{t}/{a}{receipt.InvoiceCounterExtension}"
                : null);
            Add("Захтевао", receipt.RequestedBy);
            Add("Потписао", receipt.SignedBy);
            Add("Издат", receipt.SdcDateTime?.ToString("dd.MM.yyyy HH:mm:ss"));
            Add("Скениран", receipt.ScannedAt.ToString("dd.MM.yyyy HH:mm"));
            Add("Укупно", $"{receipt.TotalAmount:N2} RSD");
            Add("Од тога порез", receipt.TotalTaxAmount > 0 ? $"{receipt.TotalTaxAmount:N2} RSD" : null);
            // The verification link is kept on file and drawn as the QR code below, but the
            // page does not spell it out: it is a wall of encoded text, of no use to read.

            return rows;

            void Add(string label, string? value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    rows.Add(new DetailRow(label, value));
            }
        }

        /// <summary>
        /// Sizes the printed receipt to its own text: the box hugs the slip and the QR
        /// code spans it, the way it prints on paper. Android's horizontal scroller
        /// otherwise reports the full page width, which stretches the box away from the
        /// text; Windows sizes it to the content on its own.
        /// </summary>
        private void OnJournalSizeChanged(object? sender, EventArgs e)
        {
            var text = Math.Max(TextWidth(JournalLabel), TextWidth(JournalFooterLabel));
            if (text <= 0)
                return;

            // How wide the slip may get before it has to scroll sideways: the page
            // without its own padding and without the box's padding and stroke.
            var available = Root.Width
                            - Root.Padding.HorizontalThickness
                            - JournalBorder.Padding.HorizontalThickness
                            - (JournalBorder.StrokeThickness * 2);
            var side = available > 0 ? Math.Min(text, available) : text;

            if (Math.Abs(JournalScroll.WidthRequest - side) > 0.5)
                JournalScroll.WidthRequest = side;

            QrImage.WidthRequest = side;
            QrImage.HeightRequest = side;

            // The added barcodes are scaled to the slip, exactly like the QR code above.
            // Nothing inside the slip may be wider than this: the box is sized to its own
            // text, and one wider child would stretch it and push everything else off the
            // left edge.
            foreach (var bars in _barcodeViews)
                bars.WidthRequest = side;
        }

        /// <summary>
        /// How wide the monospace text itself is. Android stretches the label across the
        /// scroller, so its laid-out Width is the width of the page rather than of the slip;
        /// measuring it unconstrained returns the width the longest printed line needs,
        /// which is what the box has to match on either platform.
        /// </summary>
        private static double TextWidth(Label label) =>
            label.IsVisible ? label.Measure(double.PositiveInfinity, double.PositiveInfinity).Width : 0;

        private async void OnAddBarcodeClicked(object? sender, EventArgs e)
        {
            if (Receipt is not { } receipt)
                return;

            await Shell.Current.GoToAsync(nameof(BarcodeScanPage), new Dictionary<string, object>
            {
                [BarcodeScanPage.ReceiptKey] = receipt,
            });
        }

        private async void OnDeleteClicked(object? sender, EventArgs e)
        {
            if (Receipt is not { } receipt)
                return;

            if (!await DisplayAlertAsync("Обриши рачун", "Уклонити овај рачун са уређаја?", "Обриши", "Откажи"))
                return;

            await _store.DeleteAsync(receipt);
            await Shell.Current.GoToAsync("..");
        }
    }
}
