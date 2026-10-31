namespace Sethika.Application.Abstractions;

/// <summary>What storage kept: where, and the proof of what it was.</summary>
/// <param name="StorageKey"><c>core.Files.StorageKey</c>. Chosen by storage, never by the uploader.</param>
/// <param name="Sha256">Lowercase hex SHA-256 of the bytes as stored, for <c>core.Files.Sha256Hash</c>.</param>
/// <param name="SizeBytes">The bytes stored.</param>
public sealed record SavedFile(string StorageKey, string Sha256, long SizeBytes);

/// <summary>
/// Where uploaded files' bytes live (plan 02, B4). <c>core.Files</c> holds what is known about each
/// one; this holds the content. Local disk in development, Blob storage before deployment.
/// </summary>
/// <remarks>
/// Files are private: nothing here serves them. A download goes through an endpoint that checks
/// the caller's permission and then reads through <see cref="OpenReadAsync"/>.
/// </remarks>
public interface IFileStorage
{
    /// <summary>The <c>core.Files.StorageProvider</c> value for what this stores.</summary>
    string ProviderName { get; }

    /// <summary>
    /// Copies <paramref name="content"/> into storage, hashing it on the way, or keeps nothing if it
    /// turns out to be longer than <paramref name="maxBytes"/>.
    /// </summary>
    /// <returns>Where it went, or <see langword="null"/> when it was too large.</returns>
    Task<SavedFile?> SaveAsync(Stream content, long maxBytes, CancellationToken cancellationToken = default);

    /// <summary>Reads a stored file. The caller disposes the stream.</summary>
    /// <exception cref="FileNotFoundException">Nothing is stored under the key.</exception>
    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default);

    /// <summary>Removes a stored file; nothing happens if it is already gone.</summary>
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);
}
