using Sethika.Application.Abstractions;
using Sethika.Application.Onboarding.Applying;

namespace Sethika.Application.Tests.Onboarding;

/// <summary>Plan 02, 5.3: what an applicant typed, field by field.</summary>
public class ApplicationInputTests
{
    private static readonly DateOnly Today = new(2026, 10, 18);

    /// <summary>Accepts ten digits starting with 0 and writes them as +94; refuses anything else.</summary>
    private sealed class TenDigitPhones : IPhoneNumberNormalizer
    {
        public bool TryNormalize(string? phoneNumber, out string normalized)
        {
            var digits = new string([.. (phoneNumber ?? string.Empty).Where(char.IsAsciiDigit)]);
            normalized = digits.Length == 10 && digits[0] == '0' ? "+94" + digits[1..] : string.Empty;
            return normalized.Length > 0;
        }

        // An application's phones are any line, so the form never asks this.
        public bool TryNormalizeMobile(string? phoneNumber, out string normalized) =>
            throw new NotSupportedException("An application takes landlines.");
    }

    private static readonly TenDigitPhones Phones = new();

    private static ApplicationDetails Valid() => new(
        InstitutionType: " temple ",
        ProposedName: "  Sri Sudharmaramaya ",
        NameSi: "ශ්‍රී සුධර්මාරාමය",
        NameTa: "",
        OfficialRegistrationNo: "BUD/GAL/1952/77",
        RegisteringAuthority: "Department of Buddhist Affairs",
        RegistrationDate: new DateOnly(1952, 4, 1),
        EstablishedYear: 1890,
        ResidentCapacity: null,
        Description: null,
        AddressLine1: "12 Temple Road",
        AddressLine2: null,
        City: "Galle",
        District: "gll",
        GnDivision: null,
        PostalCode: "80000",
        Latitude: 6.0535m,
        Longitude: 80.221m,
        OfficePhone: "091 223 4567",
        OfficeEmail: "office@sudharmaramaya.lk",
        Website: "https://sudharmaramaya.lk",
        ApplicantFullName: "Kamal Perera",
        ApplicantNic: " 901234567v ",
        ApplicantDesignation: "Secretary",
        ApplicantPhone: "077 100 0001",
        ApplicantEmail: null);

    [Fact]
    public void AValidApplicationIsTidied()
    {
        var (tidy, invalid) = ApplicationInput.Check(Valid(), Phones, Today);

        Assert.Empty(invalid);
        Assert.Equal(("TEMPLE", "GLL", "Sri Sudharmaramaya", "901234567V"), (tidy.InstitutionType, tidy.District, tidy.ProposedName, tidy.ApplicantNic));
        Assert.Equal(("+94912234567", "+94771000001"), (tidy.OfficePhone, tidy.ApplicantPhone));
        Assert.Null(tidy.NameTa);
    }

    [Theory]
    [InlineData("901234567V")]
    [InlineData("901234567X")]
    [InlineData("199012345678")]
    public void BothNicFormatsAreAccepted(string nic)
    {
        Assert.Empty(ApplicationInput.Check(Valid() with { ApplicantNic = nic }, Phones, Today).Invalid);
    }

    [Theory]
    [InlineData("90123456V")]
    [InlineData("9012345678")]
    [InlineData("1990123456789")]
    [InlineData("90123456AV")]
    [InlineData("")]
    public void AnythingElseIsNotANic(string nic)
    {
        Assert.Equal(["applicantNic"], ApplicationInput.Check(Valid() with { ApplicantNic = nic }, Phones, Today).Invalid);
    }

    public static TheoryData<string, Func<ApplicationDetails, ApplicationDetails>> OneBadField() => new()
    {
        { "proposedName", d => d with { ProposedName = "   " } },
        { "proposedName", d => d with { ProposedName = new string('a', 201) } },
        { "officialRegistrationNo", d => d with { OfficialRegistrationNo = "සංගම/1952" } },
        { "registrationDate", d => d with { RegistrationDate = Today.AddDays(1) } },
        { "registrationDate", d => d with { RegistrationDate = null } },
        { "establishedYear", d => d with { EstablishedYear = 2027 } },
        { "residentCapacity", d => d with { ResidentCapacity = 0 } },
        { "postalCode", d => d with { PostalCode = "8000A" } },
        { "latitude", d => d with { Latitude = 5.49m } },
        { "latitude", d => d with { Latitude = null } },
        { "longitude", d => d with { Longitude = 82.11m } },
        { "officePhone", d => d with { OfficePhone = "12345" } },
        { "officeEmail", d => d with { OfficeEmail = "Office <office@x.lk>" } },
        { "website", d => d with { Website = "javascript:alert(1)" } },
        { "website", d => d with { Website = "ftp://sudharmaramaya.lk" } },
        { "applicantEmail", d => d with { ApplicantEmail = "not an address" } },
        { "district", d => d with { District = null } },
    };

    [Theory]
    [MemberData(nameof(OneBadField))]
    public void EachBadFieldIsNamed(string field, Func<ApplicationDetails, ApplicationDetails> spoil)
    {
        Assert.Equal([field], ApplicationInput.Check(spoil(Valid()), Phones, Today).Invalid);
    }

    [Fact]
    public void EveryBadOfficeBearerFieldIsNamedWithItsPosition()
    {
        OfficeBearerDetails[] bearers =
        [
            new("Chief Trustee", "561234567v", "Trustee", "0771234567", null, true),
            new(" ", "123", "", "1", "nope", false),
        ];

        var (tidy, invalid) = ApplicationInput.Check(bearers, Phones);

        Assert.Equal(
            ["officeBearers[1].designation", "officeBearers[1].email", "officeBearers[1].fullName", "officeBearers[1].nic", "officeBearers[1].phone"],
            invalid.Order());
        Assert.Equal(("561234567V", "+94771234567"), (tidy[0].Nic, tidy[0].Phone));
    }

    [Fact]
    public void MoreThanFifteenOfficeBearersAreRefused()
    {
        var bearers = Enumerable.Range(0, ApplicationInput.MaxOfficeBearers + 1)
            .Select(i => new OfficeBearerDetails("Member", $"90123456{i % 10}V", "Member", "0771234567", null, false))
            .ToList();

        Assert.Equal(["officeBearers"], ApplicationInput.Check(bearers, Phones).Invalid);
    }
}
