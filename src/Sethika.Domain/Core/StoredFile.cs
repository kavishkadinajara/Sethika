namespace Sethika.Domain.Core;

/// <summary>
/// What is known about an uploaded file; its bytes live in file storage under <see cref="StorageKey"/>.
/// Maps <c>core.Files</c>.
/// </summary>
public class StoredFile
{
    public long Id { get; set; }

    /// <summary>How the file is named outside the database. The internal Id never leaves it.</summary>
    public Guid PublicId { get; set; }

    public string StorageProvider { get; set; } = null!;
    public string StorageKey { get; set; } = null!;

    /// <summary>The name the uploader gave it. Shown, and offered on download; never part of a path.</summary>
    public string OriginalName { get; set; } = null!;

    public string ContentType { get; set; } = null!;
    public long SizeBytes { get; set; }

    /// <summary>Lowercase hex SHA-256, so the same document sent twice can be recognised.</summary>
    public string Sha256Hash { get; set; } = null!;

    public bool IsPrivate { get; set; } = true;

    /// <summary><c>Pending</c> until a virus scanner exists (plan 02, B4: scanning is cut).</summary>
    public string ScanStatus { get; set; } = "Pending";

    public Guid UploadedBy { get; set; }
    public DateTime UploadedAt { get; set; }
    public bool IsDeleted { get; set; }
}
