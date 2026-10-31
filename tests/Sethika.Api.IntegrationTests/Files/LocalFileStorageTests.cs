using System.Security.Cryptography;
using Sethika.Infrastructure.Files;
using Sethika.Api.IntegrationTests.Security;

namespace Sethika.Api.IntegrationTests.Files;

/// <summary>Plan 02, B4: local file storage against the real file system, in a directory of its own.</summary>
public sealed class LocalFileStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sethika-files-" + Guid.NewGuid().ToString("N"));
    private readonly LocalFileStorage _storage;

    public LocalFileStorageTests()
    {
        Directory.CreateDirectory(_root);
        _storage = new LocalFileStorage(_root, new FixedTimeProvider(new DateTimeOffset(2026, 10, 18, 9, 0, 0, TimeSpan.Zero)));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string[] FilesOnDisk() => Directory.GetFiles(_root, "*", SearchOption.AllDirectories);

    [Fact]
    public async Task AFileIsStoredUnderADatedKeyWithItsHashAndSize()
    {
        var bytes = RandomNumberGenerator.GetBytes(200_000);

        var saved = await _storage.SaveAsync(new MemoryStream(bytes), maxBytes: 1_000_000);

        Assert.NotNull(saved);
        Assert.Matches("^2026/10/[0-9a-f]{32}$", saved.StorageKey);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), saved.Sha256);
        Assert.Equal(bytes.Length, saved.SizeBytes);

        await using var read = await _storage.OpenReadAsync(saved.StorageKey);
        using var copy = new MemoryStream();
        await read.CopyToAsync(copy);
        Assert.Equal(bytes, copy.ToArray());
    }

    [Fact]
    public async Task ATooLargeFileLeavesNothingBehind()
    {
        // Read as a stream that never says its length, as a request body does.
        var saved = await _storage.SaveAsync(new UnseekableStream(new byte[1_001]), maxBytes: 1_000);

        Assert.Null(saved);
        Assert.Empty(FilesOnDisk());
    }

    [Fact]
    public async Task AFileOfExactlyTheLimitIsKept()
    {
        var saved = await _storage.SaveAsync(new UnseekableStream(new byte[1_000]), maxBytes: 1_000);

        Assert.NotNull(saved);
        Assert.Equal(1_000, saved.SizeBytes);
        Assert.Single(FilesOnDisk());
    }

    [Fact]
    public async Task AFailedCopyLeavesNothingBehind()
    {
        await Assert.ThrowsAsync<IOException>(() => _storage.SaveAsync(new FailingStream(), maxBytes: 1_000_000));

        Assert.Empty(FilesOnDisk());
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("2026/10/../../../outside")]
    [InlineData("C:/Windows/win.ini")]
    [InlineData("2026/10/certificate.pdf")]
    [InlineData("2026/10/ABCDEF0123456789ABCDEF0123456789")]
    [InlineData("")]
    public async Task AKeyStorageCouldNotHaveIssuedIsRefused(string key)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _storage.OpenReadAsync(key));
        await Assert.ThrowsAsync<ArgumentException>(() => _storage.DeleteAsync(key));
    }

    [Fact]
    public async Task ADeletedFileIsGoneAndDeletingAgainIsHarmless()
    {
        var saved = await _storage.SaveAsync(new MemoryStream([1, 2, 3]), maxBytes: 10);
        Assert.NotNull(saved);

        await _storage.DeleteAsync(saved.StorageKey);
        await _storage.DeleteAsync(saved.StorageKey);

        Assert.Empty(FilesOnDisk());
        await Assert.ThrowsAsync<FileNotFoundException>(() => _storage.OpenReadAsync(saved.StorageKey));
    }

    [Fact]
    public void ARelativeRootIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new LocalFileStorage("files", TimeProvider.System));
    }

    private sealed class UnseekableStream(byte[] content) : MemoryStream(content)
    {
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
    }

    private sealed class FailingStream : Stream
    {
        private int _reads;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) =>
            _reads++ == 0 ? count : throw new IOException("The connection dropped mid-upload.");

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
