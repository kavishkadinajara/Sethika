using Sethika.Application.Files;

namespace Sethika.Application.Tests.Files;

/// <summary>Plan 02, B4: what an uploaded document must be before it is stored.</summary>
public class UploadedFileRulesTests
{
    private const string Allowed = ".pdf,.jpg,.jpeg,.png";
    private const int MaxMb = 5;

    private static readonly byte[] Pdf = "%PDF-1.7\n"u8.ToArray();
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Exe = "MZ\x90\0\x03\0\0\0"u8.ToArray();

    [Theory]
    [InlineData("certificate.pdf", ".pdf", "application/pdf")]
    [InlineData("CERTIFICATE.PDF", ".pdf", "application/pdf")]
    [InlineData("photo.jpg", ".jpg", "image/jpeg")]
    [InlineData("photo.jpeg", ".jpeg", "image/jpeg")]
    [InlineData("scan.png", ".png", "image/png")]
    public void AFileWhoseBytesMatchAnAllowedExtensionIsAccepted(string name, string extension, string contentType)
    {
        var header = extension switch { ".pdf" => Pdf, ".png" => Png, _ => Jpeg };

        var check = UploadedFileRules.Check(name, 1_000, header, Allowed, MaxMb);

        Assert.True(check.IsAcceptable, check.Error);
        Assert.Equal(extension, check.Extension);
        Assert.Equal(contentType, check.ContentType);
    }

    [Fact]
    public void ARenamedExecutableIsRefusedByItsContent()
    {
        Assert.Equal(UploadedFileRules.ContentMismatch, UploadedFileRules.Check("certificate.pdf", 1_000, Exe, Allowed, MaxMb).Error);
    }

    [Fact]
    public void AnImageNamedAsAPdfIsRefused()
    {
        // Both are allowed types; the bytes still have to be the one the name says.
        Assert.Equal(UploadedFileRules.ContentMismatch, UploadedFileRules.Check("certificate.pdf", 1_000, Png, Allowed, MaxMb).Error);
    }

    [Theory]
    [InlineData("tool.exe")]
    [InlineData("certificate.pdf.exe")]
    [InlineData("certificate")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("page.html")]
    public void AnExtensionThatIsNotAllowedIsRefused(string? name)
    {
        Assert.Equal(UploadedFileRules.TypeNotAllowed, UploadedFileRules.Check(name, 1_000, Pdf, Allowed, MaxMb).Error);
    }

    [Fact]
    public void AKnownTypeTheDocumentTypeDoesNotAllowIsRefused()
    {
        // A document type that takes PDFs only: a genuine PNG still may not be uploaded for it.
        Assert.Equal(UploadedFileRules.TypeNotAllowed, UploadedFileRules.Check("scan.png", 1_000, Png, ".pdf", MaxMb).Error);
    }

    [Fact]
    public void TheSizeLimitIsTheDocumentTypesMegabytesExactly()
    {
        var limit = UploadedFileRules.MaxBytes(MaxMb);

        Assert.Equal(5L * 1024 * 1024, limit);
        Assert.True(UploadedFileRules.Check("a.pdf", limit, Pdf, Allowed, MaxMb).IsAcceptable);
        Assert.Equal(UploadedFileRules.TooLarge, UploadedFileRules.Check("a.pdf", limit + 1, Pdf, Allowed, MaxMb).Error);
    }

    [Fact]
    public void AnEmptyFileIsRefused()
    {
        Assert.Equal(UploadedFileRules.Empty, UploadedFileRules.Check("a.pdf", 0, [], Allowed, MaxMb).Error);
    }

    [Fact]
    public void AHeaderShorterThanTheSignatureIsRefused()
    {
        Assert.Equal(UploadedFileRules.ContentMismatch, UploadedFileRules.Check("a.pdf", 3, "%PD"u8, Allowed, MaxMb).Error);
    }
}
