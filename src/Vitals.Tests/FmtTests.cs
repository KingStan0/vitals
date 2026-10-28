using Vitals.Ui;

namespace Vitals.Tests;

public sealed class FmtTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(1073741824, "1.0 GB")]
    public void Bytes_uses_binary_units(double value, string expected)
    {
        Assert.Equal(expected, Fmt.Bytes(value));
    }

    [Fact]
    public void Bar_never_exceeds_width()
    {
        Assert.Equal(10, Fmt.Bar(0, 10, false).Length);
        Assert.Equal(10, Fmt.Bar(50, 10, false).Length);
        Assert.Equal(10, Fmt.Bar(100, 10, false).Length);
        Assert.Equal(10, Fmt.Bar(-20, 10, false).Length);
        Assert.Equal(10, Fmt.Bar(420, 10, false).Length);
    }

    [Fact]
    public void Bar_is_all_filled_at_hundred_percent()
    {
        Assert.Equal(new string('█', 8), Fmt.Bar(100, 8, false));
        Assert.Equal(new string('-', 8), Fmt.Bar(0, 8, true));
    }

    [Fact]
    public void Spark_always_returns_requested_width()
    {
        var values = new[] { 1d, 5, 2, 8, 3 };
        Assert.Equal(12, Fmt.Spark(values, 12, false).Length);
        Assert.Equal(3, Fmt.Spark(values, 3, false).Length);
        Assert.Equal(6, Fmt.Spark(Array.Empty<double>(), 6, true).Length);
    }

    [Fact]
    public void Truncate_adds_ellipsis_only_when_needed()
    {
        Assert.Equal("chrome", Fmt.Truncate("chrome", 10));
        Assert.Equal(5, Fmt.Truncate("chrome.exe", 5).Length);
        Assert.Equal("chro…", Fmt.Truncate("chrome.exe", 5));
    }

    [Fact]
    public void Truncate_uses_ascii_in_ascii_mode()
    {
        Assert.Equal("chro.", Fmt.Truncate("chrome.exe", 5, ascii: true));
        Assert.Equal(".", Fmt.Truncate("chrome.exe", 1, ascii: true));
    }

    [Fact]
    public void Truncate_counts_wide_glyphs_and_never_splits_a_surrogate_pair()
    {
        Assert.Equal("你…", Fmt.Truncate("你好世界", 3));
        Assert.Equal("你.", Fmt.Truncate("你好", 3, ascii: true));
        Assert.Equal("😀…", Fmt.Truncate("😀😀", 3));
        Assert.Equal("你", Fmt.Truncate("你", 2));
    }

    [Fact]
    public void Truncate_keeps_text_that_fits_exactly()
    {
        Assert.Equal("abc", Fmt.Truncate("abc", 3));
        Assert.Equal("你好", Fmt.Truncate("你好", 4));
    }

    [Fact]
    public void Truncate_replaces_control_characters_before_rendering()
    {
        Assert.Equal("a b", Fmt.Truncate("a\nb", 10));
        Assert.Equal("a b", Fmt.Truncate("a\tb", 10));
        Assert.Equal("a b", Fmt.OneLine("a\rb"));
        Assert.Equal("plain", Fmt.OneLine("plain"));
    }

    [Fact]
    public void Formatting_handles_non_finite_and_empty_values()
    {
        Assert.Equal("0 B", Fmt.Bytes(double.NaN));
        Assert.Equal("0 B", Fmt.Bytes(double.PositiveInfinity));
        Assert.Equal(string.Empty, Fmt.Bar(50, 0, false));
        Assert.Equal(new string('-', 4), Fmt.Bar(double.NaN, 4, true));
        Assert.Equal("n/a", Fmt.Celsius(null, false));
        Assert.Equal("12C", Fmt.Celsius(12, true));
    }
}
