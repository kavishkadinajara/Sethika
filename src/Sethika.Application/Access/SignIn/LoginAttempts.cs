using Sethika.Domain.Access;

namespace Sethika.Application.Access.SignIn;

/// <summary>Builds <c>auth.LoginHistory</c> rows, trimmed to the columns' lengths.</summary>
internal static class LoginAttempts
{
    public static LoginAttempt Succeeded(Guid userId, string loginName, string? ipAddress, string? userAgent, DateTimeOffset now) =>
        New(userId, loginName, failureReason: null, ipAddress, userAgent, now);

    public static LoginAttempt Failed(Guid? userId, string loginName, string failureReason, string? ipAddress, string? userAgent, DateTimeOffset now) =>
        New(userId, loginName, failureReason, ipAddress, userAgent, now);

    private static LoginAttempt New(
        Guid? userId,
        string loginName,
        string? failureReason,
        string? ipAddress,
        string? userAgent,
        DateTimeOffset now) => new()
        {
            UserId = userId,
            LoginName = Truncate(loginName, 256)!,
            Succeeded = failureReason is null,
            FailureReason = failureReason,
            IpAddress = Truncate(ipAddress, 45),
            UserAgent = Truncate(userAgent, 400),
            AttemptedAt = now.UtcDateTime,
        };

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}
