using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Sethika.Application.Abstractions;

namespace Sethika.Infrastructure.Files;

/// <summary>
/// <see cref="IFileStorage"/> on local disk (plan 02, B4): <c>{root}/yyyy/MM/{guid}</c>.
/// </summary>
/// <remarks>
/// The key is made here from the date and a new GUID, so nothing the uploader sent - least of all
/// the file name - ever becomes part of a path. A key read back from the database is still checked
/// against that shape before it is used, so a tampered row cannot point outside the root.
///
/// A file is written to a <c>.partial</c> name and renamed only once it is complete and within its
/// limit, so a crash or an oversize upload never leaves a half-written file under a real key.
/// </remarks>
public sealed partial class LocalFileStorage : IFileStorage
{
    public const string Provider = "Local";

    private const int BufferSize = 81_920;

    private readonly string _root;
    private readonly TimeProvider _time;

    public LocalFileStorage(string root, TimeProvider time)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(time);

        if (!Path.IsPathFullyQualified(root))
        {
            throw new ArgumentException($"The file storage root must be an absolute path, not '{root}'.", nameof(root));
        }

        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        _time = time;
    }

    public string ProviderName => Provider;

    public async Task<SavedFile?> SaveAsync(Stream content, long maxBytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

        var now = _time.GetUtcNow();
        var key = string.Create(CultureInfo.InvariantCulture, $"{now:yyyy}/{now:MM}/{Guid.NewGuid():N}");
        var path = PathFor(key);
        var partial = path + ".partial";

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        long written = 0;

        try
        {
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
            {
                int read;
                while ((read = await content.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken).ConfigureAwait(false)) > 0)
                {
                    written += read;
                    if (written > maxBytes)
                    {
                        break;
                    }

                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }

            if (written > maxBytes)
            {
                File.Delete(partial);
                return null;
            }

            File.Move(partial, path);
            return new SavedFile(key, Convert.ToHexStringLower(hash.GetHashAndReset()), written);
        }
        catch
        {
            File.Delete(partial);
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var path = PathFor(storageKey);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("No stored file has this key.", storageKey);
        }

        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        File.Delete(PathFor(storageKey));
        return Task.CompletedTask;
    }

    /// <summary>The file's path, after checking the key is one this class could have made.</summary>
    private string PathFor(string storageKey)
    {
        ArgumentNullException.ThrowIfNull(storageKey);

        if (!KeyShape().IsMatch(storageKey))
        {
            throw new ArgumentException("Not a storage key this storage issues.", nameof(storageKey));
        }

        var path = Path.GetFullPath(Path.Combine(_root, storageKey));

        // The shape already rules out '..' and rooted keys; this holds even if the shape is loosened.
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The storage key resolves outside the storage root.", nameof(storageKey));
        }

        return path;
    }

    [GeneratedRegex("^[0-9]{4}/[0-9]{2}/[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyShape();
}
