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

    [TestCase(KatHouding.Zit)]
    [TestCase(KatHouding.Blij)]
    [TestCase(KatHouding.Slaap)]
    public void every_pose_renders_as_one_svg(KatHouding houding)
    {
        var svg = KatSprite.Svg(houding);

        svg.Should().StartWith("<svg").And.EndWith("</svg>");
    }
}
