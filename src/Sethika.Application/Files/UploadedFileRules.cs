namespace Sethika.Application.Files;

/// <summary>The outcome of checking an upload: an error code, or what the file is.</summary>
/// <param name="Error"><see langword="null"/> when the file is acceptable; otherwise an API error code.</param>
/// <param name="Extension">The lower-case extension, with its dot.</param>
/// <param name="ContentType">The type the file is stored and served as - decided here, never taken from the client.</param>
public sealed record UploadedFileCheck(string? Error, string? Extension, string? ContentType)
{
    public bool IsAcceptable => Error is null;

    internal static UploadedFileCheck Refused(string error) => new(error, null, null);
}

/// <summary>
/// What an uploaded document must be before it is stored (plan 02, B4). Pure, so every rule is
/// tested without a file system.
/// </summary>
/// <remarks>
/// The extension and the client's content type are both chosen by whoever uploads, so neither is
/// trusted alone: the extension must be one the document type allows, and the file's first bytes
/// must be what that extension promises. A renamed executable fails the second check.
/// </remarks>
public static class UploadedFileRules
{
    public const string Empty = "file_empty";
    public const string TooLarge = "file_too_large";
    public const string TypeNotAllowed = "file_type_not_allowed";
    public const string ContentMismatch = "file_content_mismatch";

    /// <summary>How many leading bytes <see cref="Check"/> needs to see.</summary>
    public const int HeaderLength = 8;

    private static readonly (string Extension, string ContentType, byte[] Signature)[] Known =
    [
        (".pdf",  "application/pdf", "%PDF-"u8.ToArray()),
        (".jpg",  "image/jpeg",      [0xFF, 0xD8, 0xFF]),
        (".jpeg", "image/jpeg",      [0xFF, 0xD8, 0xFF]),
        (".png",  "image/png",       [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
    ];

    /// <param name="fileName">The name the client sent; only its extension is used.</param>
    /// <param name="length">The upload's length in bytes.</param>
    /// <param name="header">The file's first bytes, up to <see cref="HeaderLength"/>.</param>
    /// <param name="allowedExtensions"><c>org.DocumentTypes.AllowedExtensions</c>: <c>.pdf,.jpg,.jpeg,.png</c>.</param>
    /// <param name="maxSizeMb"><c>org.DocumentTypes.MaxSizeMb</c>.</param>
    public static UploadedFileCheck Check(
        string? fileName,
        long length,
        ReadOnlySpan<byte> header,
        string allowedExtensions,
        int maxSizeMb)
    {
        ArgumentNullException.ThrowIfNull(allowedExtensions);

        if (length <= 0)
        {
            return UploadedFileCheck.Refused(Empty);
        }

        if (length > MaxBytes(maxSizeMb))
        {
            return UploadedFileCheck.Refused(TooLarge);
        }

        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        var allowed = allowedExtensions
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.ToLowerInvariant());

        var known = Array.FindIndex(Known, k => k.Extension == extension);
        if (extension.Length == 0 || known < 0 || !allowed.Contains(extension))
        {
            return UploadedFileCheck.Refused(TypeNotAllowed);
        }

        var (_, contentType, signature) = Known[known];
        return header.StartsWith(signature)
            ? new UploadedFileCheck(null, extension, contentType)
            : UploadedFileCheck.Refused(ContentMismatch);
    }

    /// <summary>The byte limit for a document type's <c>MaxSizeMb</c>.</summary>
    public static long MaxBytes(int maxSizeMb) => maxSizeMb * 1024L * 1024L;
}
