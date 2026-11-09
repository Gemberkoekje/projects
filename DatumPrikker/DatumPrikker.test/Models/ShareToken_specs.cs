namespace DatumPrikker.test.Models;

public sealed class ShareToken_specs
{
    [Test]
    public void new_tokens_are_well_formed_and_differ()
    {
        var tokens = Enumerable.Range(0, 200).Select(_ => ShareToken.New()).ToArray();

        tokens.Should().OnlyContain(token => ShareToken.IsWellFormed(token));
        tokens.Should().OnlyHaveUniqueItems();
    }

    [TestCase("")]
    [TestCase("k7Gm2xQ")]
    [TestCase("k7Gm2xQab")]
    [TestCase("k7Gm2xQ0")]
    [TestCase("../../et")]
    public void anything_else_is_not_a_token(string token)
    {
        ShareToken.IsWellFormed(token).Should().BeFalse();
    }
}
