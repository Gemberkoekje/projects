using ByGalacticAccord.Presentation.Text;

namespace ByGalacticAccord.Tests;

/// <summary>How numbers read on screen (design §11.1): the same in every browser.</summary>
public sealed class FormatTests
{
    [Theory]
    [InlineData(1234.4, "₵1,234")]
    [InlineData(1234.5, "₵1,235")]
    [InlineData(-42, "−₵42")]
    [InlineData(-0.4, "₵0")]
    [InlineData(0, "₵0")]
    public void Money_reads_in_whole_credits(decimal amount, string expected) =>
        Assert.Equal(expected, Format.Money(amount));

    [Theory]
    [InlineData(12.8, "₵12.80")]
    [InlineData(-42, "−₵42.00")]
    [InlineData(1408, "₵1,408.00")]
    [InlineData(0.18, "₵0.18")]
    public void Money_to_the_cent(decimal amount, string expected) =>
        Assert.Equal(expected, Format.MoneyCents(amount));

    [Theory]
    [InlineData(0.048, "4.8%")]
    [InlineData(0.04, "4%")]
    [InlineData(0.26, "26%")]
    [InlineData(0.4, "40%")]
    [InlineData(0.0096, "1.0%")]
    [InlineData(1, "100%")]
    public void Percentages_keep_a_decimal_only_when_small(decimal fraction, string expected) =>
        Assert.Equal(expected, Format.Percent(fraction));

    [Fact]
    public void Quantities_points_and_hours_are_whole_numbers()
    {
        Assert.Equal("90", Format.Quantity(89.6m));
        Assert.Equal("1,200", Format.Quantity(1200m));
        Assert.Equal("14", Format.Points(0.14m));
        Assert.Equal("110", Format.Hours(110));
    }
}
