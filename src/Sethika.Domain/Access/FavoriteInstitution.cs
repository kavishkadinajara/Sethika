namespace Sethika.Domain.Access;

/// <summary>
/// A place a donor keeps among "My places" (plan 07, H12): the heart on an institution's page. Maps
/// <c>auth.FavoriteInstitutions</c>, whose key is the pair, so a place is kept once.
/// </summary>
public class FavoriteInstitution
{
    public Guid UserId { get; set; }
    public int InstitutionId { get; set; }
    public DateTime AddedAt { get; set; }
}
