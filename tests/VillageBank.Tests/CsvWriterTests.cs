using System.Text;
using VillageBank.Api.Services;

namespace VillageBank.Tests;

public class CsvWriterTests
{
    private static IReadOnlyList<object?> Row(params object?[] cells) => cells;

    [Fact]
    public void Rows_are_written_comma_separated_with_a_header()
    {
        var csv = CsvWriter.Build(new[] { "a", "b" }, new[] { Row("x", 1), Row("y", 2) });

        Assert.Equal("a,b\r\nx,1\r\ny,2\r\n", csv);
    }

    [Fact]
    public void Text_with_commas_quotes_or_line_breaks_is_quoted()
    {
        var csv = CsvWriter.Build(new[] { "text" }, new[] { Row("a,b"), Row("say \"hi\""), Row("two\nlines") });

        Assert.Equal("text\r\n\"a,b\"\r\n\"say \"\"hi\"\"\"\r\n\"two\nlines\"\r\n", csv);
    }

    [Theory]
    [InlineData("=SUM(A1:A9)", "'=SUM(A1:A9)")]
    [InlineData("+1", "'+1")]
    [InlineData("-1", "'-1")]
    [InlineData("@cmd", "'@cmd")]
    public void Text_that_would_run_as_a_formula_is_neutralised(string input, string expected)
    {
        var csv = CsvWriter.Build(new[] { "name" }, new[] { Row(input) });

        Assert.Equal($"name\r\n{expected}\r\n", csv);
    }

    [Fact]
    public void Negative_numbers_are_not_mistaken_for_formulas()
    {
        var csv = CsvWriter.Build(new[] { "amount" }, new[] { Row(-5m) });

        Assert.Equal("amount\r\n-5\r\n", csv);
    }

    [Fact]
    public void Decimals_use_the_invariant_culture_without_trailing_zeros()
    {
        var csv = CsvWriter.Build(
            new[] { "amount" },
            new[] { Row(300_000.0000m), Row(6_000.5000m), Row(0.25m) });

        Assert.Equal("amount\r\n300000\r\n6000.5\r\n0.25\r\n", csv);
    }

    [Fact]
    public void Dates_booleans_and_nulls_are_formatted_plainly()
    {
        var csv = CsvWriter.Build(
            new[] { "date", "flag", "missing" },
            new[] { Row(new DateOnly(2026, 10, 30), true, null) });

        Assert.Equal("date,flag,missing\r\n2026-10-30,true,\r\n", csv);
    }

    [Fact]
    public void The_bytes_start_with_a_byte_order_mark_and_keep_unicode_text()
    {
        var bytes = CsvWriter.ToUtf8WithBom("name\r\nມາອາຍ\r\n");

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3));
        Assert.Contains("ມາອາຍ", Encoding.UTF8.GetString(bytes));
    }
}
