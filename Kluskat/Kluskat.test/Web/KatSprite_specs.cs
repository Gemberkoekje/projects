using Kluskat.WebApp.Kat;

namespace Kluskat.test.Web;

public sealed class KatSprite_specs
{
    [Test]
    public void every_row_of_the_sprite_is_the_same_width()
    {
        KatSprite.Basis.Should().AllSatisfy(rij => rij.Should().HaveLength(KatSprite.Breedte));
    }

    [Test]
    public void only_the_sitting_cat_blinks()
    {
        KatSprite.Svg(KatHouding.Zit).Should().Contain("kat-knipper");
        KatSprite.Svg(KatHouding.Blij).Should().NotContain("kat-knipper");
        KatSprite.Svg(KatHouding.Slaap).Should().NotContain("kat-knipper");
    }

    [Test]
    public void every_combination_of_pose_form_and_accessory_renders()
    {
        var combinaties =
            from houding in new[] { KatHouding.Zit, KatHouding.Blij, KatHouding.Slaap }
            from vorm in new[] { KatVorm.Kitten, KatVorm.Kat, KatVorm.Majesteit }
            from accessoire in Enum.GetValues<Accessoire>()
            select KatSprite.Svg(houding, vorm, accessoire);

        combinaties.Should().AllSatisfy(svg => svg.Should().StartWith("<svg").And.EndWith("</svg>"));
    }

    [Test]
    public void an_accessory_adds_pixels()
    {
        KatSprite.Svg(KatHouding.Zit, KatVorm.Kat, Accessoire.Kroon).Should().NotBe(KatSprite.Svg(KatHouding.Zit));
    }

    [TestCase(KatVorm.Kitten, "kat-kitten")]
    [TestCase(KatVorm.Kat, "kat-volwassen")]
    [TestCase(KatVorm.Majesteit, "kat-majesteit")]
    public void the_form_sets_the_size_class(KatVorm vorm, string klasse)
    {
        KatSprite.Svg(KatHouding.Zit, vorm).Should().Contain(klasse);
    }

    [Test]
    public void only_the_majesty_sparkles()
    {
        KatSprite.Svg(KatHouding.Zit, KatVorm.Majesteit).Should().Contain("kat-glans");
        KatSprite.Svg(KatHouding.Zit, KatVorm.Kat).Should().NotContain("kat-glans");
    }
}
