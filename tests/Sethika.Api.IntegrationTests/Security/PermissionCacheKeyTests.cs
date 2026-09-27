using Sethika.Infrastructure.Security;

namespace Sethika.Api.IntegrationTests.Security;

/// <summary>
/// Unit tests (no I/O) for the cache key. Lives in this project only because the key is an
/// Infrastructure type and Sethika.Application.Tests cannot see Infrastructure; CLAUDE.md
/// fixes the solution at three test projects, so a fourth was not added for it.
/// </summary>
public class PermissionCacheKeyTests
{
    private static readonly Guid User = Guid.Parse("11111111-1111-1111-1111-111111111111");

    /// <summary>Pins the exact format CLAUDE.md specifies.</summary>
    [Fact]
    public void Uses_the_documented_format()
    {
        var key = PermissionCacheKey.For(User, institutionId: 7, userVersion: 3, institutionVersion: 9);

        Assert.Equal("perm:11111111-1111-1111-1111-111111111111:7:v3.9", key);
    }

    /// <summary>
    /// Bumping either version has to yield a different key - that is the entire invalidation
    /// mechanism, so it is worth pinning rather than assuming.
    /// </summary>
    [Fact]
    public void Changes_when_either_version_changes()
    {
        var baseline = PermissionCacheKey.For(User, 7, 3, 9);

        Assert.NotEqual(baseline, PermissionCacheKey.For(User, 7, 4, 9));
        Assert.NotEqual(baseline, PermissionCacheKey.For(User, 7, 3, 10));
    }

    [Fact]
    public void Scopes_by_institution()
    {
        Assert.NotEqual(
            PermissionCacheKey.For(User, 7, 1, 1),
            PermissionCacheKey.For(User, 8, 1, 1));
    }

    /// <summary>
    /// Donors and platform staff have no institution row. The segment must not be empty, or
    /// "perm:{id}::v1.0" could collide with a future scope and is unreadable in redis-cli.
    /// </summary>
    [Fact]
    public void Renders_a_null_institution_as_an_explicit_scope()
    {
        var key = PermissionCacheKey.For(User, institutionId: null, userVersion: 2, institutionVersion: 0);

        Assert.Equal("perm:11111111-1111-1111-1111-111111111111:global:v2.0", key);
        Assert.DoesNotContain("::", key, StringComparison.Ordinal);
    }

    /// <summary>A null institution must not collide with institution 0.</summary>
    [Fact]
    public void Null_institution_differs_from_institution_zero()
    {
        Assert.NotEqual(
            PermissionCacheKey.For(User, null, 1, 0),
            PermissionCacheKey.For(User, 0, 1, 0));
    }
}
