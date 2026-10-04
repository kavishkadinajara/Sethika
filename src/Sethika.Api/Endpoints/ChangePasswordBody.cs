namespace Sethika.Api.Endpoints;

/// <summary>Replaces the signed-in user's own password.</summary>
public sealed record ChangePasswordBody(string? CurrentPassword, string? NewPassword)
{
    /// <summary>Names the type and nothing else: both members are passwords, and none may reach a log.</summary>
    public override string ToString() => nameof(ChangePasswordBody);
}
