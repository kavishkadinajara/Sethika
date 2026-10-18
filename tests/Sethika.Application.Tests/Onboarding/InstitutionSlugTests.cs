using Sethika.Application.Onboarding.Approval;

namespace Sethika.Application.Tests.Onboarding;

/// <summary>Plan 02, B6 step 2: the URL name of a new institution.</summary>
public class InstitutionSlugTests
{
    [Theory]
    [InlineData("Sri Sudharmaramaya", "Galle", "sri-sudharmaramaya-galle")]
    [InlineData("St. Mary's Church", "Negombo", "st-mary-s-church-negombo")]
    [InlineData("  Sri  Kathiresan   Kovil ", " Colombo 4 ", "sri-kathiresan-kovil-colombo-4")]
    [InlineData("Pathirakāḷi Amman Kōvil", "Trincomalee", "pathirakali-amman-kovil-trincomalee")]
    [InlineData("Jumma Mosque", null, "jumma-mosque")]
    [InlineData("Sri Bodhirajaramaya (ශ්‍රී බෝධිරාජාරාමය)", "Kandy", "sri-bodhirajaramaya-kandy")]
    public void ANameAndCityBecomeLowerCaseAsciiJoinedByHyphens(string name, string? city, string expected)
    {
        Assert.Equal(expected, InstitutionSlug.From(name, city));
    }

    [Theory]
    [InlineData("ශ්‍රී සුධර්මාරාමය")]
    [InlineData("ஸ்ரீ கதிரேசன் கோவில்")]
    [InlineData("1952")]
    public void ANameWithNoLatinLettersHasNoSlug_EvenWithALatinCity(string name)
    {
        // "galle" alone would say nothing about which institution it is; the caller uses the code.
        Assert.Null(InstitutionSlug.From(name, "Galle"));
    }

    [Fact]
    public void ALongSlugIsCutAtAWordAndNeverEndsInAHyphen()
    {
        var slug = InstitutionSlug.From(string.Join(' ', Enumerable.Repeat("Mahavihara", 20)), "Anuradhapura");

        Assert.NotNull(slug);
        Assert.True(slug.Length <= InstitutionSlug.MaxLength);
        Assert.False(slug.EndsWith('-'));
    }

    [Fact]
    public void TheFirstFreeSlugCountsFromTwo()
    {
        Assert.Equal("galle-temple", InstitutionSlug.FirstFree("galle-temple", []));
        Assert.Equal("galle-temple-2", InstitutionSlug.FirstFree("galle-temple", ["galle-temple"]));
        Assert.Equal("galle-temple-4", InstitutionSlug.FirstFree("galle-temple", ["galle-temple", "galle-temple-2", "GALLE-TEMPLE-3"]));
    }
}
