namespace Sethika.Api.IntegrationTests.Security;

/// <summary>A clock that always reads <paramref name="now"/>, for asserting exact token times.</summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
