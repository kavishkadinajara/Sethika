namespace Sethika.Application.Abstractions;

public interface ICurrentUserContext
{
    Guid? UserId { get; }
    int? InstitutionId { get; }
    bool IsPlatform { get; }
}
