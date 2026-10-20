using System.Globalization;
using System.Text.RegularExpressions;

namespace Overmark.Editor;

/// <summary>
/// What the picture's pixels measure in real units, set by typing a length with a unit (such as "50 mm") into a
/// dimension. Dimensions whose text says what they measured then say it in that unit, to as many decimal places as the
/// typed length has, and new dimensions start that way.
/// </summary>
/// <param name="UnitsPerPixel">How many <paramref name="Unit"/>s one image pixel is.</param>
/// <param name="Decimals">How many decimal places lengths are given to.</param>
/// <param name="Spaced">Whether a space goes between the number and the unit, as it was typed.</param>
internal sealed partial record DimensionScale(double UnitsPerPixel, string Unit, int Decimals, bool Spaced)
{
    /// <summary><paramref name="pixels"/> image pixels as a length in the scale's unit, e.g. "37.4 mm".</summary>
    public string Format(double pixels) =>
        (pixels * UnitsPerPixel).ToString("F" + Decimals, CultureInfo.CurrentCulture) + (Spaced ? " " : "") + Unit;

    /// <summary>The scale as people would say it, e.g. "1 mm = 4.28 px".</summary>
    public string Describe() =>
        $"1{(Spaced ? " " : "")}{Unit} = {(1 / UnitsPerPixel).ToString("0.##", CultureInfo.CurrentCulture)} px";

    /// <summary>What a dimension measuring <paramref name="pixels"/> says until it's typed over: the length in pixels, or in <paramref name="scale"/>'s unit.</summary>
    public static string Measured(double pixels, DimensionScale? scale) => scale?.Format(pixels) ?? $"{Math.Round(pixels)} px";

    /// <summary>
    /// The scale <paramref name="text"/> gives a dimension measuring <paramref name="pixels"/>: a length with a unit of
    /// mm, cm, m, µm, in, ft, " or ' (e.g. "50 mm", "2.5in" or 3"), with a point or comma for decimals. Null for
    /// anything else, including a bare number or pixels.
    /// </summary>
    public static DimensionScale? FromText(string text, double pixels)
    {
        var match = Length().Match(text);
        if (!match.Success || !(pixels > 0))
        {
            return null;
        }

        var number = match.Groups["number"].Value.Replace(',', '.');
        if (!double.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            return null;
        }

        var point = number.IndexOf('.');
        var unit = match.Groups["unit"].Value;
        return new DimensionScale(
            value / pixels,
            char.IsLetter(unit[0]) ? unit.ToLowerInvariant() : unit,
            point < 0 ? 0 : number.Length - point - 1,
            match.Groups["space"].Length > 0);
    }

    [GeneratedRegex("""^\s*(?<number>\d+(?:[.,]\d+)?|[.,]\d+)(?<space>\s*)(?<unit>mm|cm|m|µm|um|in|ft|"|'|″|′)\s*$""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Length();
}
