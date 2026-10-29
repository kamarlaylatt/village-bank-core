using System.Globalization;
using System.Text;

namespace VillageBank.Api.Services;

public static class CsvWriter
{
    private const string LineBreak = "\r\n";

    public static string Build(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows)
    {
        var builder = new StringBuilder();

        AppendRow(builder, headers);
        foreach (var row in rows)
            AppendRow(builder, row);

        return builder.ToString();
    }

    // The byte order mark makes Excel read the file as UTF-8, which matters for Lao and Burmese names.
    public static byte[] ToUtf8WithBom(string csv)
    {
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        return encoding.GetPreamble().Concat(encoding.GetBytes(csv)).ToArray();
    }

    private static void AppendRow(StringBuilder builder, IEnumerable<object?> cells)
    {
        builder.Append(string.Join(",", cells.Select(Format))).Append(LineBreak);
    }

    private static string Format(object? value) => value switch
    {
        null => "",
        string text => Escape(Neutralise(text)),
        decimal number => number.ToString("0.####", CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime time => time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => Escape(Neutralise(value.ToString() ?? ""))
    };

    // Spreadsheets run a cell that starts with = + - or @ as a formula. Names come from user input,
    // so such text gets a leading apostrophe and is shown as plain text instead.
    private static string Neutralise(string text) =>
        text.Length > 0 && "=+-@\t\r".Contains(text[0]) ? "'" + text : text;

    private static string Escape(string text) =>
        text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
            ? "\"" + text.Replace("\"", "\"\"") + "\""
            : text;
}
