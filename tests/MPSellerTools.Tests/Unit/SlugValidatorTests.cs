using MPSellerTools.Core.Platform;

namespace MPSellerTools.Tests.Unit;

public class SlugValidatorTests
{
    [Theory]
    [InlineData("  Acme Corp  ", "acme-corp")]
    [InlineData("Acme_Corp", "acme-corp")]
    [InlineData("Acme   Corp", "acme-corp")]
    [InlineData("acme-corp", "acme-corp")]
    [InlineData("-acme-corp-", "acme-corp")]
    public void Normalize_produces_the_expected_slug(string input, string expected)
    {
        Assert.Equal(expected, SlugValidator.Normalize(input));
    }

    [Theory]
    [InlineData("acme-corp", true)]
    [InlineData("ab", false)] // too short (2 chars, minimum is 3)
    [InlineData("a-b", true)] // exactly 3 chars, meets the minimum
    [InlineData("abc", true)]
    [InlineData("Acme-Corp", false)] // uppercase not allowed
    [InlineData("acme_corp", false)] // underscore not allowed
    [InlineData("acme--corp", false)] // double hyphen not allowed
    [InlineData("-acmecorp", false)] // leading hyphen not allowed
    [InlineData("acmecorp-", false)] // trailing hyphen not allowed
    [InlineData("", false)]
    public void IsValid_enforces_the_safe_character_set(string slug, bool expected)
    {
        Assert.Equal(expected, SlugValidator.IsValid(slug));
    }

    [Fact]
    public void IsValid_rejects_a_slug_over_the_max_length()
    {
        var tooLong = new string('a', 41);
        Assert.False(SlugValidator.IsValid(tooLong));
    }
}
