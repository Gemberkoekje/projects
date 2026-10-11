using Curator.Core.Text;

namespace Curator.Core.Tests;

public sealed class PageMarkupTests
{
    [Fact]
    public void PlainTextIsTranslated()
    {
        var span = Assert.Single(PageMarkup.Parse("Eye of Newt"));

        Assert.Equal(new TextSpan(SpanKind.Translated, "Eye of Newt"), span);
    }

    [Fact]
    public void MarkersMakeUntranslatedAndIllegibleSpans()
    {
        var spans = PageMarkup.Parse("Speak {?Varre?} at the keyhole {~and wait~}.");

        Assert.Equal(
            [
                new TextSpan(SpanKind.Translated, "Speak "),
                new TextSpan(SpanKind.Untranslated, "Varre"),
                new TextSpan(SpanKind.Translated, " at the keyhole "),
                new TextSpan(SpanKind.Illegible, "and wait"),
                new TextSpan(SpanKind.Translated, "."),
            ],
            spans);
    }

    [Theory]
    [InlineData("{~never closed")]
    [InlineData("stray close~}")]
    [InlineData("{??}")]
    [InlineData("{~  ~}")]
    [InlineData("{~outer {?inner?} ~}")]
    [InlineData("{~a ?} b~}")]
    public void BadMarkupIsReported(string markup)
    {
        Assert.NotEmpty(PageMarkup.Validate(markup));
        Assert.Throws<FormatException>(() => PageMarkup.Parse(markup));
    }

    [Fact]
    public void LegibleDropsIllegibleText()
    {
        Assert.Equal("Never upon anything", PageMarkup.Legible("{~Never~}Never upon anything{~ that breathes~}"));
    }

    [Fact]
    public void EveryGoldPageParses()
    {
        foreach (var page in TestWorld.Content.Books.SelectMany(b => b.Pages))
        {
            Assert.Empty(PageMarkup.Validate(page.Text));
            Assert.Empty(PageMarkup.Validate(page.SpellName));
        }
    }
}
