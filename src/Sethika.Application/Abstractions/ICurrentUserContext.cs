namespace Sethika.Application.Abstractions;

public interface ICurrentUserContext
{
    Guid? UserId { get; }
    int? InstitutionId { get; }
    bool IsPlatform { get; }

    /// <summary>
    /// The <c>jti</c> of the access token this request presented, or null without one. A step-up
    /// grant is bound to it (plan A5), so a refreshed token does not inherit one.
    /// </summary>
    string? TokenId { get; }
}
