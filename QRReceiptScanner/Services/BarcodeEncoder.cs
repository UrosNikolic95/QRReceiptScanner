namespace QRReceiptScanner.Services;

/// <summary>
/// A linear barcode reduced to its bars: one entry per module, true where the bar is
/// dark. <see cref="Text"/> is the human-readable line printed beneath it.
/// </summary>
public sealed record LinearBarcode(string Symbology, string Text, bool[] Modules);

/// <summary>
/// Turns a scanned barcode value back into the bar pattern that encodes it, so the
/// receipt can print it the way the shop's own slip does.
/// </summary>
/// <remarks>
/// Hand-rolled on purpose: ZXing.Net is the obvious library for this, but its managed
/// zxing.dll collides with the native ZXing.dll that zxing-cpp ships for the Windows
/// scanner, so it cannot come back into this project. Only the symbologies
/// <see cref="Pages.BarcodeScanPage"/> accepts are implemented; anything else returns
/// null and the page falls back to printing the value as text.
/// </remarks>
public static class BarcodeEncoder
{
    // --- EAN/UPC digit patterns, 7 modules each. ---

    private static readonly string[] LeftOdd =
    {
        "0001101", "0011001", "0010011", "0111101", "0100011",
        "0110001", "0101111", "0111011", "0110111", "0001011",
    };

    private static readonly string[] LeftEven =
    {
        "0100111", "0110011", "0011011", "0100001", "0011101",
        "0111001", "0000101", "0010001", "0001001", "0010111",
    };

    private static readonly string[] Right =
    {
        "1110010", "1100110", "1101100", "1000010", "1011100",
        "1001110", "1010000", "1000100", "1001000", "1110100",
    };

    /// <summary>
    /// Which of the first six digits use the even-parity table, chosen by the leading
    /// digit. This is how a 13th digit is carried by a symbol only 12 digits wide.
    /// </summary>
    private static readonly string[] Parity =
    {
        "LLLLLL", "LLGLGG", "LLGGLG", "LLGGGL", "LGLLGG",
        "LGGLLG", "LGGGLL", "LGLGLG", "LGLGGL", "LGGLGL",
    };

    /// <summary>
    /// Code 128 element widths: six alternating bar/space runs per symbol, bar first,
    /// eleven modules in total. Index 106 is the stop pattern and has a seventh element.
    /// </summary>
    private static readonly string[] Code128Patterns =
    {
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312",
        "132212", "221213", "221312", "231212", "112232", "122132", "122231", "113222",
        "123122", "123221", "223211", "221132", "221231", "213212", "223112", "312131",
        "311222", "321122", "321221", "312212", "322112", "322211", "212123", "212321",
        "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
        "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121",
        "313121", "211331", "231131", "213113", "213311", "213131", "311123", "311321",
        "331121", "312113", "312311", "332111", "314111", "221411", "431111", "111224",
        "111422", "121124", "121421", "141122", "141221", "112214", "112412", "122114",
        "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
        "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112",
        "421211", "212141", "214121", "412121", "111143", "111341", "131141", "114113",
        "114311", "411113", "411311", "113141", "114131", "311141", "411131", "211412",
        "211214", "211232", "2331112",
    };

    private const int Code128CodeC = 99;
    private const int Code128CodeB = 100;
    private const int Code128StartB = 104;
    private const int Code128StartC = 105;
    private const int Code128Stop = 106;

    /// <summary>
    /// Encodes a scanned value, or returns null when this symbology is not drawable.
    /// <paramref name="format"/> is the scanner's own name for it; when it does not
    /// match anything known, the value's own shape decides.
    /// </summary>
    public static LinearBarcode? Encode(string? value, string? format)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim();

        var encoded = format switch
        {
            "Ean13" => EncodeEan13(value),
            "Ean8" => EncodeEan8(value),
            "Upca" => EncodeUpcA(value),
            "Code128" => EncodeCode128(value),
            _ => null,
        };

        // A scanner that names the symbology differently than expected should still
        // print: fall back to whatever the digits themselves allow.
        return encoded
               ?? EncodeEan13(value)
               ?? EncodeUpcA(value)
               ?? EncodeEan8(value)
               ?? EncodeCode128(value);
    }

    // --- EAN-13 / UPC-A / EAN-8 ---

    private static LinearBarcode? EncodeEan13(string value)
    {
        if (value.Length != 13 || !IsAllDigits(value) || !HasValidCheckDigit(value))
            return null;

        return new LinearBarcode("EAN-13", FormatEan13(value), BuildEan13(value));
    }

    /// <summary>UPC-A is EAN-13 with an implied leading zero, and prints as 12 digits.</summary>
    private static LinearBarcode? EncodeUpcA(string value)
    {
        if (value.Length != 12 || !IsAllDigits(value) || !HasValidCheckDigit(value))
            return null;

        return new LinearBarcode("UPC-A", FormatUpcA(value), BuildEan13("0" + value));
    }

    private static LinearBarcode? EncodeEan8(string value)
    {
        if (value.Length != 8 || !IsAllDigits(value) || !HasValidCheckDigit(value))
            return null;

        var modules = new List<bool>(67);
        AddPattern(modules, "101");
        for (var i = 0; i < 4; i++)
            AddPattern(modules, LeftOdd[value[i] - '0']);
        AddPattern(modules, "01010");
        for (var i = 4; i < 8; i++)
            AddPattern(modules, Right[value[i] - '0']);
        AddPattern(modules, "101");

        return new LinearBarcode("EAN-8", value[..4] + " " + value[4..], modules.ToArray());
    }

    private static bool[] BuildEan13(string value)
    {
        var parity = Parity[value[0] - '0'];
        var modules = new List<bool>(95);

        AddPattern(modules, "101");
        for (var i = 1; i <= 6; i++)
        {
            var digit = value[i] - '0';
            AddPattern(modules, parity[i - 1] == 'L' ? LeftOdd[digit] : LeftEven[digit]);
        }

        AddPattern(modules, "01010");
        for (var i = 7; i < 13; i++)
            AddPattern(modules, Right[value[i] - '0']);
        AddPattern(modules, "101");

        return modules.ToArray();
    }

    // --- Code 128, subsets B and C ---

    /// <summary>
    /// Encodes in subset B (one printable ASCII character per symbol), dropping into
    /// subset C for runs of digits, which it packs two to a symbol. That halving matters
    /// here: the barcode has to stay readable at the width of a receipt, and a symbol
    /// twice as long is drawn with bars half as wide.
    /// </summary>
    private static LinearBarcode? EncodeCode128(string value)
    {
        foreach (var c in value)
        {
            if (c < ' ' || c > '~')
                return null;
        }

        var values = new List<int>(value.Length + 3);
        var mode = 'N';
        var pos = 0;

        while (pos < value.Length)
        {
            var digits = DigitRunLength(value, pos);

            // Entering C costs a switch symbol, so it only pays for a long enough run —
            // unless the whole value is digits, where it pays from the first pair.
            var useC = digits >= 2 &&
                       (mode == 'C' || digits >= 4 || (pos == 0 && digits == value.Length));

            if (useC)
            {
                if (mode != 'C')
                {
                    values.Add(mode == 'N' ? Code128StartC : Code128CodeC);
                    mode = 'C';
                }

                // An odd digit is left behind for subset B on the next pass.
                for (var pair = 0; pair < digits / 2; pair++, pos += 2)
                    values.Add((value[pos] - '0') * 10 + (value[pos + 1] - '0'));

                continue;
            }

            if (mode != 'B')
            {
                values.Add(mode == 'N' ? Code128StartB : Code128CodeB);
                mode = 'B';
            }

            values.Add(value[pos] - ' ');
            pos++;
        }

        if (values.Count == 0)
            return null;

        // Weighted modulo 103: the start value counts once, every later symbol by its
        // one-based position, switch symbols included.
        long sum = values[0];
        for (var i = 1; i < values.Count; i++)
            sum += (long)values[i] * i;
        values.Add((int)(sum % 103));
        values.Add(Code128Stop);

        var modules = new List<bool>(values.Count * 11 + 2);
        foreach (var symbol in values)
        {
            // Widths alternate bar, space, bar, … starting with a bar.
            var dark = true;
            foreach (var width in Code128Patterns[symbol])
            {
                for (var i = 0; i < width - '0'; i++)
                    modules.Add(dark);
                dark = !dark;
            }
        }

        return new LinearBarcode("Code 128", value, modules.ToArray());
    }

    // --- Shared helpers ---

    /// <summary>How many digits run consecutively from <paramref name="start"/>.</summary>
    private static int DigitRunLength(string value, int start)
    {
        var i = start;
        while (i < value.Length && value[i] is >= '0' and <= '9')
            i++;

        return i - start;
    }

    private static void AddPattern(List<bool> modules, string pattern)
    {
        foreach (var c in pattern)
            modules.Add(c == '1');
    }

    private static bool IsAllDigits(string value)
    {
        foreach (var c in value)
        {
            if (c is < '0' or > '9')
                return false;
        }

        return value.Length > 0;
    }

    /// <summary>
    /// The modulo-10 check digit shared by EAN-8, EAN-13 and UPC-A: digits are weighted
    /// 3 and 1 alternately, counting back from the check digit itself.
    /// </summary>
    private static bool HasValidCheckDigit(string value)
    {
        var sum = 0;
        for (var i = 0; i < value.Length - 1; i++)
        {
            var digit = value[i] - '0';
            // Right-to-left, the digit next to the check digit is the one weighted 3.
            var weight = (value.Length - i) % 2 == 0 ? 3 : 1;
            sum += digit * weight;
        }

        var expected = (10 - sum % 10) % 10;
        return expected == value[^1] - '0';
    }

    /// <summary>Grouped the way it prints on the slip: 1 + 6 + 6.</summary>
    private static string FormatEan13(string value) =>
        $"{value[0]} {value[1..7]} {value[7..]}";

    /// <summary>UPC-A prints its first and last digits outside the bars: 1 + 5 + 5 + 1.</summary>
    private static string FormatUpcA(string value) =>
        $"{value[0]} {value[1..6]} {value[6..11]} {value[^1]}";
}
