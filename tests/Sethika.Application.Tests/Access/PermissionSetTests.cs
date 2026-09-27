using Sethika.Application.Access;

namespace Sethika.Application.Tests.Access;

/// <summary>
/// Unit tests for the evaluated permission set.
/// </summary>
/// <remarks>
/// Note what is deliberately NOT tested here: deny-wins and module-disabled. Both are
/// applied by auth.vw_EffectivePermissions, so the only way to unit-test them would be to
/// reimplement them in C# - which would create a second source of truth that could disagree
/// with the database, and the database is the one that decides access. They are proved
/// against the real view in Sethika.Api.IntegrationTests instead.
///
/// What this class does cover is the contract the authorization handler relies on.
/// </remarks>
public class PermissionSetTests
{
    private static PermissionSet Set(params string[] codes) => new(codes, []);

    [Fact]
    public void Allows_a_code_that_is_present()
    {
        Assert.True(Set("BOOKINGS.CALENDAR.VIEW").Allows("BOOKINGS.CALENDAR.VIEW"));
    }

    [Fact]
    public void Does_not_allow_a_code_that_is_absent()
    {
        Assert.False(Set("BOOKINGS.CALENDAR.VIEW").Allows("BOOKINGS.CALENDAR.APPROVE"));
    }

    /// <summary>
    /// The view runs under a case-insensitive collation, so the in-memory set has to agree
    /// or a lookup could pass in SQL and fail in C#.
    /// </summary>
    [Theory]
    [InlineData("bookings.calendar.view")]
    [InlineData("Bookings.Calendar.View")]
    [InlineData("BOOKINGS.CALENDAR.VIEW")]
    public void Matches_codes_case_insensitively(string probe)
    {
        Assert.True(Set("BOOKINGS.CALENDAR.VIEW").Allows(probe));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Never_allows_a_blank_code(string? probe)
    {
        Assert.False(Set("BOOKINGS.CALENDAR.VIEW").Allows(probe!));
    }

    [Fact]
    public void Empty_allows_nothing()
    {
        Assert.False(PermissionSet.Empty.Allows("BOOKINGS.CALENDAR.VIEW"));
        Assert.Empty(PermissionSet.Empty.Codes);
    }

    [Fact]
    public void Tracks_which_codes_need_step_up_mfa()
    {
        var set = new PermissionSet(
            ["BOOKINGS.CALENDAR.VIEW", "BOOKINGS.CALENDAR.APPROVE"],
            ["BOOKINGS.CALENDAR.APPROVE"]);

        Assert.True(set.RequiresMfa("BOOKINGS.CALENDAR.APPROVE"));
        Assert.False(set.RequiresMfa("BOOKINGS.CALENDAR.VIEW"));
    }

    /// <summary>
    /// Holding a permission and being allowed to use it now are separate questions: an
    /// MFA-gated code is still held, so Allows stays true and the step-up check is what
    /// gates the action.
    /// </summary>
    [Fact]
    public void An_mfa_gated_code_is_still_held()
    {
        var set = new PermissionSet(["BOOKINGS.CALENDAR.APPROVE"], ["BOOKINGS.CALENDAR.APPROVE"]);

        Assert.True(set.Allows("BOOKINGS.CALENDAR.APPROVE"));
    }

    /// <summary>
    /// A deny override removes the code upstream in the view, so by the time the set exists
    /// the permission is simply absent. This pins the consequence the handler depends on.
    /// </summary>
    [Fact]
    public void A_code_removed_upstream_by_a_deny_is_absent()
    {
        var granted = new[] { "BOOKINGS.CALENDAR.VIEW", "BOOKINGS.CALENDAR.APPROVE" };
        var afterDeny = granted.Where(c => c != "BOOKINGS.CALENDAR.APPROVE");

        var set = new PermissionSet(afterDeny, []);

        Assert.True(set.Allows("BOOKINGS.CALENDAR.VIEW"));
        Assert.False(set.Allows("BOOKINGS.CALENDAR.APPROVE"));
    }
}
