namespace QRReceiptScanner.Services;

/// <summary>
/// Draws a <see cref="LinearBarcode"/> as bars, the way it is printed on a label:
/// black on white, one module wide each, filling the width it is given.
/// </summary>
public sealed class BarcodeDrawable : IDrawable
{
    private readonly bool[] _modules;

    public BarcodeDrawable(LinearBarcode barcode) => _modules = barcode.Modules;

    /// <summary>
    /// Blank margin on either side, in modules. Scanners need this quiet zone to find
    /// the symbol's edges; without it a reader can miss the first and last bars.
    /// </summary>
    public const int QuietZoneModules = 10;

    /// <summary>How many modules wide the drawn symbol is, quiet zone included.</summary>
    /// <remarks>
    /// Worth knowing when choosing a width: a symbol drawn at about one *pixel* per
    /// module is a grey smear no reader can decode, and it takes roughly two before it
    /// decodes reliably. A phone hides this — a GraphicsView paints at display density,
    /// so a barcode 230 units wide on a 2.6x screen still gets ~600 real pixels — but a
    /// long Code 128 in a narrow slip on a 1x display is close to that floor.
    /// </remarks>
    public int TotalModules => _modules.Length + QuietZoneModules * 2;

    public void Draw(ICanvas canvas, RectF rect)
    {
        if (_modules.Length == 0 || rect.Width <= 0 || rect.Height <= 0)
            return;

        // White underneath regardless of the page theme: a barcode is read by contrast,
        // and a dark background would invert it.
        canvas.FillColor = Colors.White;
        canvas.FillRectangle(rect);

        var total = _modules.Length + QuietZoneModules * 2;
        var moduleWidth = rect.Width / total;
        canvas.FillColor = Colors.Black;

        // Consecutive dark modules are filled as one bar so no seam shows between them.
        var run = 0;
        for (var i = 0; i <= _modules.Length; i++)
        {
            var dark = i < _modules.Length && _modules[i];
            if (dark)
            {
                run++;
                continue;
            }

            if (run > 0)
            {
                var x = rect.X + (QuietZoneModules + i - run) * moduleWidth;
                canvas.FillRectangle(x, rect.Y, run * moduleWidth, rect.Height);
                run = 0;
            }
        }
    }
}
