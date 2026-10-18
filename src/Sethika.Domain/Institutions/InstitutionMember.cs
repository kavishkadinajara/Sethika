namespace Sethika.Domain.Institutions;

/// <summary>A person who belongs to an institution. Maps <c>org.InstitutionMembers</c>.</summary>
/// <remarks>
/// Behind an RLS filter, so only the institution's own sessions and the platform see these rows -
/// which is why a user's institution list at sign-in comes from <c>auth.UserRoles</c> (plan 01, A7).
/// <c>UX_InstMember_Owner</c> allows one active owner per institution.
/// </remarks>
public class InstitutionMember
{
    public int Id { get; set; }
    public int InstitutionId { get; set; }
    public Guid UserId { get; set; }
    public string? Designation { get; set; }
    public bool IsOwner { get; set; }
    public string Status { get; set; } = "Active";
    public DateTime JoinedAt { get; set; }
    public Guid AddedBy { get; set; }
    public DateTime? RemovedAt { get; set; }
    public Guid? RemovedBy { get; set; }
}
