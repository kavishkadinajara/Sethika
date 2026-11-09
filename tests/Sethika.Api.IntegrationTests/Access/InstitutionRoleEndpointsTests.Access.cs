using System.Net;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Sethika.Api.IntegrationTests.Access;

/// <summary>
/// Plan 04, U3: the access screens' reads (X6) and staff invitations (X1-X5), over HTTP against real
/// SQL Server and Redis. The invitee is always someone with an account and no role in Alpha.
/// </summary>
public sealed partial class InstitutionRoleEndpointsTests
{
    private static string Local(string e164) => "0" + e164[3..]; // +94771234567 → 0771234567

    private async Task<Guid> InviteAsync(Member actor, Member invitee, string role = "T_STAFF")
    {
        using var response = await SendAsync(actor, HttpMethod.Post, "/api/institution/invitations", new { phone = invitee.Phone, role });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await BodyOf(response)).GetProperty("id").GetGuid();
    }

    private async Task<JsonElement[]> ListAsync(Member member, string path)
    {
        using var response = await SendAsync(member, HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return [.. (await BodyOf(response)).EnumerateArray()];
    }

    /// <summary>A statement on a platform connection: RLS hides invitations from a bare one.</summary>
    private async Task AsPlatformAsync(string sql, object parameters) =>
        await ExecuteAsync("EXEC sp_set_session_context N'IsPlatform', 1;\n" + sql, parameters);

    private Task<string> StatusOfAsync(Guid invitation) =>
        ScalarAsync<string>(
            "EXEC sp_set_session_context N'IsPlatform', 1; SELECT Status FROM org.StaffInvitations WHERE PublicId = @invitation;",
            new { invitation });

    private Task<int> ActiveRolesAsync(Member member, string role) =>
        ScalarAsync<int>(
            """
            SELECT COUNT(*) FROM auth.UserRoles ur JOIN auth.Roles r ON r.Id = ur.RoleId
            WHERE ur.UserId = @user AND r.InstitutionId = @alpha AND r.Code = @role AND ur.IsActive = 1;
            """,
            new { user = member.UserId, alpha = database.AlphaInstitutionId, role });

    // ------------------------------------------------------------------ the access screens' reads (X6)

    [Fact]
    public async Task RolesAreListedWithTheirPermissionsHoldersAndWhetherTheCallerMayManageThem()
    {
        var owner = await MemberAsync("T_OWNER");
        await MemberAsync("T_STAFF");
        await MemberAsync("T_STAFF");
        var manager = await MemberAsync("T_MANAGER");

        var byOwner = (await ListAsync(owner, "/api/institution/roles")).ToDictionary(r => r.GetProperty("code").GetString()!);

        Assert.False(byOwner["T_OWNER"].GetProperty("canManage").GetBoolean());   // rank 1 is nobody's junior
        Assert.True(byOwner["T_MANAGER"].GetProperty("canManage").GetBoolean());
        Assert.True(byOwner["T_STAFF"].GetProperty("canManage").GetBoolean());
        Assert.Equal(2, byOwner["T_STAFF"].GetProperty("members").GetInt32());
        Assert.Equal(StaffPermissions.Order(StringComparer.Ordinal), byOwner["T_STAFF"].GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal("STAFF", byOwner["T_STAFF"].GetProperty("level").GetString());

        var byManager = (await ListAsync(manager, "/api/institution/roles")).ToDictionary(r => r.GetProperty("code").GetString()!);
        Assert.False(byManager["T_MANAGER"].GetProperty("canManage").GetBoolean());
        Assert.True(byManager["T_STAFF"].GetProperty("canManage").GetBoolean());
    }

    [Fact]
    public async Task TheCatalogueHoldsOnlyTheInstitutionsSwitchedOnModules_WithStepUpsAndDependencies()
    {
        var owner = await MemberAsync("T_OWNER");

        var modules = await ListAsync(owner, "/api/institution/permission-catalogue");
        var codes = modules.Select(m => m.GetProperty("code").GetString()).ToList();

        Assert.Contains("ACCESS", codes);
        Assert.Contains("INSTITUTION", codes);
        Assert.DoesNotContain("SERVICES", codes);   // not switched on for Alpha here
        Assert.DoesNotContain("ONBOARDING", codes); // the platform's, never an institution's

        var roles = modules.Single(m => m.GetProperty("code").GetString() == "ACCESS")
            .GetProperty("screens").EnumerateArray().Single(s => s.GetProperty("code").GetString() == "ACCESS.ROLES");
        var edit = roles.GetProperty("permissions").EnumerateArray().Single(p => p.GetProperty("code").GetString() == "ACCESS.ROLES.EDIT");
        Assert.Equal("EDIT", edit.GetProperty("action").GetString());
        Assert.True(edit.GetProperty("requiresMfa").GetBoolean());
        Assert.Equal(["ACCESS.ROLES.VIEW"], edit.GetProperty("requires").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public async Task MembersAreEveryoneHoldingARoleHere_AndNobodyElse()
    {
        var owner = await MemberAsync("T_OWNER");
        var staff = await MemberAsync("T_STAFF");
        var outsider = await MemberAsync(roleCode: null);

        var members = await ListAsync(owner, "/api/institution/members");
        var ids = members.Select(m => m.GetProperty("userId").GetGuid()).ToList();

        Assert.Contains(owner.UserId, ids);
        Assert.Contains(staff.UserId, ids);
        Assert.DoesNotContain(outsider.UserId, ids);
        var staffRow = members.Single(m => m.GetProperty("userId").GetGuid() == staff.UserId);
        Assert.Equal(["T_STAFF"], staffRow.GetProperty("roles").EnumerateArray().Select(r => r.GetProperty("code").GetString()));
    }

    [Fact]
    public async Task StaffCannotReadTheAccessScreens()
    {
        var staff = await MemberAsync("T_STAFF");

        foreach (var path in new[] { "/api/institution/roles", "/api/institution/permission-catalogue", "/api/institution/members", "/api/institution/invitations" })
        {
            using var response = await SendAsync(staff, HttpMethod.Get, path);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    // ------------------------------------------------------------------ invitations (X1-X5)

    [Fact]
    public async Task AnOwnerInvitesAnAccountByPhone_AndTheInviteeAcceptsIntoTheRole()
    {
        var owner = await MemberAsync("T_OWNER");
        var invitee = await MemberAsync(roleCode: null);
        await SteppedUpAsync(owner);

        // Typed the local way; the account stores E.164.
        using var invited = await SendAsync(owner, HttpMethod.Post, "/api/institution/invitations", new { phone = Local(invitee.Phone), role = "t_staff" });
        Assert.Equal(HttpStatusCode.Created, invited.StatusCode);
        var id = (await BodyOf(invited)).GetProperty("id").GetGuid();

        var waiting = Assert.Single(await ListAsync(owner, "/api/institution/invitations"), i => i.GetProperty("id").GetGuid() == id);
        Assert.Equal("T_STAFF", waiting.GetProperty("roleCode").GetString());

        var mine = Assert.Single(await ListAsync(invitee, "/api/me/invitations"));
        Assert.Equal(id, mine.GetProperty("id").GetGuid());
        Assert.Equal("Alpha Temple", mine.GetProperty("institution").GetString());
        Assert.Equal("alpha-temple", mine.GetProperty("slug").GetString());

        using var accepted = await SendAsync(invitee, HttpMethod.Post, $"/api/me/invitations/{id}/accept");

        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        Assert.Equal("Accepted", await StatusOfAsync(id));
        Assert.Equal(1, await ActiveRolesAsync(invitee, "T_STAFF"));
        Assert.Empty(await ListAsync(invitee, "/api/me/invitations"));
        Assert.DoesNotContain(await ListAsync(owner, "/api/institution/invitations"), i => i.GetProperty("id").GetGuid() == id);
        Assert.Contains(await ListAsync(owner, "/api/institution/members"), m => m.GetProperty("userId").GetGuid() == invitee.UserId);
    }

    [Theory]
    // Access audit, 2026-11-08: an invitation sent while the institution was live cannot be accepted
    // once it is not. Each status the permissions view leaves out, and deletion on its own.
    [InlineData("Suspended", false)]
    [InlineData("Deactivated", false)]
    [InlineData("Active", true)]
    public async Task AnInvitationToAnInstitutionNoLongerLiveCannotBeAccepted_AndStaysOpen(string status, bool deleted)
    {
        var owner = await MemberAsync("T_OWNER");
        var invitee = await MemberAsync(roleCode: null);
        await SteppedUpAsync(owner);

        using var invited = await SendAsync(owner, HttpMethod.Post, "/api/institution/invitations", new { phone = invitee.Phone, role = "T_STAFF" });
        Assert.Equal(HttpStatusCode.Created, invited.StatusCode);
        var id = (await BodyOf(invited)).GetProperty("id").GetGuid();

        const string SetStatus = "EXEC sys.sp_set_session_context @key = N'IsPlatform', @value = 1; " +
            "UPDATE org.Institutions SET Status = @status, IsDeleted = @deleted WHERE Id = @alpha;";
        await ExecuteAsync(SetStatus, new { status, deleted, alpha = database.AlphaInstitutionId });
        try
        {
            using var refused = await SendAsync(invitee, HttpMethod.Post, $"/api/me/invitations/{id}/accept");

            await AssertRefusedAsync(refused, HttpStatusCode.Conflict, "institution_not_active");
            Assert.Equal("Pending", await StatusOfAsync(id));
            Assert.Equal(0, await ActiveRolesAsync(invitee, "T_STAFF"));
        }
        finally
        {
            await ExecuteAsync(SetStatus, new { status = "Active", deleted = false, alpha = database.AlphaInstitutionId });
        }

        // The control: the same invitation, the institution live again, is accepted. So the refusal
        // above was the institution's state and nothing else about the invitation.
        using var accepted = await SendAsync(invitee, HttpMethod.Post, $"/api/me/invitations/{id}/accept");
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        Assert.Equal(1, await ActiveRolesAsync(invitee, "T_STAFF"));
    }

    [Fact]
    public async Task InvitingNeedsAStepUp()
    {
        var owner = await MemberAsync("T_OWNER");
        var invitee = await MemberAsync(roleCode: null);

        using var response = await SendAsync(owner, HttpMethod.Post, "/api/institution/invitations", new { phone = invitee.Phone, role = "T_STAFF" });

        await AssertRefusedAsync(response, HttpStatusCode.Forbidden, "stepup_required");
    }

    [Fact]
    public async Task AnInvitationGoesOnlyToAnExistingAccount_ByAValidNumber()
    {
        var owner = await MemberAsync("T_OWNER");
        await SteppedUpAsync(owner);

        using var nobody = await SendAsync(owner, HttpMethod.Post, "/api/institution/invitations", new { phone = "+94770000001", role = "T_STAFF" });
        using var nonsense = await SendAsync(owner, HttpMethod.Post, "/api/institution/invitations", new { phone = "12", role = "T_STAFF" });

        await AssertRefusedAsync(nobody, HttpStatusCode.Conflict, "account_not_found");
        await AssertRefusedAsync(nonsense, HttpStatusCode.BadRequest, "invalid_phone_number");
    }

    [Fact]
    public async Task NobodyInvitesAMemberOrThemselves_OrIntoARoleNotJuniorToTheirs()
    {
        var owner = await MemberAsync("T_OWNER");
        var manager = await MemberAsync("T_MANAGER");
        var staff = await MemberAsync("T_STAFF");
        var outsider = await MemberAsync(roleCode: null);
        await SteppedUpAsync(owner);
        await SteppedUpAsync(manager);

        using var member = await SendAsync(owner, HttpMethod.Post, "/api/institution/invitations", new { phone = staff.Phone, role = "T_MANAGER" });
        using var self = await SendAsync(owner, HttpMethod.Post, "/api/institution/invitations", new { phone = owner.Phone, role = "T_STAFF" });
        using var senior = await SendAsync(manager, HttpMethod.Post, "/api/institution/invitations", new { phone = outsider.Phone, role = "T_MANAGER" });
        using var unknown = await SendAsync(owner, HttpMethod.Post, "/api/institution/invitations", new { phone = outsider.Phone, role = "NO_SUCH_ROLE" });

        await AssertRefusedAsync(member, HttpStatusCode.Conflict, "already_member");
        await AssertRefusedAsync(self, HttpStatusCode.Forbidden, "own_roles");
        await AssertRefusedAsync(senior, HttpStatusCode.Forbidden, "rank_not_junior");
        await AssertRefusedAsync(unknown, HttpStatusCode.NotFound, "role_not_found");
        Assert.Empty(await ListAsync(outsider, "/api/me/invitations"));
    }

    [Fact]
    public async Task OneOpenInvitationPerPerson_AndAnExpiredOneMakesWayForANewOne()
    {
        var owner = await MemberAsync("T_OWNER");
        var invitee = await MemberAsync(roleCode: null);
        await SteppedUpAsync(owner);
        var first = await InviteAsync(owner, invitee);

        using var again = await SendAsync(owner, HttpMethod.Post, "/api/institution/invitations", new { phone = invitee.Phone, role = "T_STAFF" });
        await AssertRefusedAsync(again, HttpStatusCode.Conflict, "invitation_pending");

        await AsPlatformAsync("UPDATE org.StaffInvitations SET ExpiresAt = DATEADD(MINUTE, -1, SYSUTCDATETIME()) WHERE PublicId = @first;", new { first });
        var second = await InviteAsync(owner, invitee);

        Assert.NotEqual(first, second);
        Assert.Equal("Expired", await StatusOfAsync(first));
        Assert.Equal("Pending", await StatusOfAsync(second));
    }

    [Fact]
    public async Task OnlyTheInviteeSeesOrAnswersTheirInvitation()
    {
        var owner = await MemberAsync("T_OWNER");
        var invitee = await MemberAsync(roleCode: null);
        var stranger = await MemberAsync(roleCode: null);
        await SteppedUpAsync(owner);
        var id = await InviteAsync(owner, invitee);

        Assert.Empty(await ListAsync(stranger, "/api/me/invitations"));
        using var byStranger = await SendAsync(stranger, HttpMethod.Post, $"/api/me/invitations/{id}/accept");
        // The owner's session sees the row (RLS: its institution) but it is still not theirs to answer.
        using var byOwner = await SendAsync(owner, HttpMethod.Post, $"/api/me/invitations/{id}/accept");
        using var declineByStranger = await SendAsync(stranger, HttpMethod.Post, $"/api/me/invitations/{id}/decline");

        await AssertRefusedAsync(byStranger, HttpStatusCode.NotFound, "invitation_not_found");
        await AssertRefusedAsync(byOwner, HttpStatusCode.NotFound, "invitation_not_found");
        await AssertRefusedAsync(declineByStranger, HttpStatusCode.NotFound, "invitation_not_found");
        Assert.Equal("Pending", await StatusOfAsync(id));
        Assert.Equal(0, await ActiveRolesAsync(stranger, "T_STAFF"));
    }

    [Fact]
    public async Task ACancelledOrExpiredInvitationGrantsNothing()
    {
        var owner = await MemberAsync("T_OWNER");
        var cancelledFor = await MemberAsync(roleCode: null);
        var expiredFor = await MemberAsync(roleCode: null);
        await SteppedUpAsync(owner);
        var cancelled = await InviteAsync(owner, cancelledFor);
        var expired = await InviteAsync(owner, expiredFor);

        using (var cancel = await SendAsync(owner, HttpMethod.Post, $"/api/institution/invitations/{cancelled}/cancel"))
        {
            Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
        }

        using var twice = await SendAsync(owner, HttpMethod.Post, $"/api/institution/invitations/{cancelled}/cancel");
        using var late = await SendAsync(cancelledFor, HttpMethod.Post, $"/api/me/invitations/{cancelled}/accept");
        await AssertRefusedAsync(twice, HttpStatusCode.Conflict, "invitation_closed");
        await AssertRefusedAsync(late, HttpStatusCode.Conflict, "invitation_closed");
        Assert.Equal("Revoked", await StatusOfAsync(cancelled));

        await AsPlatformAsync("UPDATE org.StaffInvitations SET ExpiresAt = DATEADD(MINUTE, -1, SYSUTCDATETIME()) WHERE PublicId = @expired;", new { expired });
        Assert.Empty(await ListAsync(expiredFor, "/api/me/invitations"));
        using var tooLate = await SendAsync(expiredFor, HttpMethod.Post, $"/api/me/invitations/{expired}/accept");
        await AssertRefusedAsync(tooLate, HttpStatusCode.Conflict, "invitation_expired");

        Assert.Equal(0, await ActiveRolesAsync(cancelledFor, "T_STAFF"));
        Assert.Equal(0, await ActiveRolesAsync(expiredFor, "T_STAFF"));
    }

    [Fact]
    public async Task DecliningGrantsNothing_AndClosesTheInvitation()
    {
        var owner = await MemberAsync("T_OWNER");
        var invitee = await MemberAsync(roleCode: null);
        await SteppedUpAsync(owner);
        var id = await InviteAsync(owner, invitee);

        using var declined = await SendAsync(invitee, HttpMethod.Post, $"/api/me/invitations/{id}/decline");
        using var thenAccepted = await SendAsync(invitee, HttpMethod.Post, $"/api/me/invitations/{id}/accept");

        Assert.Equal(HttpStatusCode.NoContent, declined.StatusCode);
        await AssertRefusedAsync(thenAccepted, HttpStatusCode.Conflict, "invitation_closed");
        Assert.Equal("Declined", await StatusOfAsync(id));
        Assert.Equal(0, await ActiveRolesAsync(invitee, "T_STAFF"));
    }

    [Fact]
    public async Task OfAnAcceptAndACancelThatRace_ExactlyOneHappens()
    {
        // The claim is a conditional UPDATE (still Pending, not expired). A lock on the row, taken
        // from a platform connection (without one RLS hides the row and the UPDLOCK locks nothing),
        // holds both requests at the claim until both wait there; then one wins and the other finds
        // the invitation closed. Repeated, because either may win.
        var owner = await MemberAsync("T_OWNER");
        await SteppedUpAsync(owner);

        for (var round = 0; round < 3; round++)
        {
            var invitee = await MemberAsync(roleCode: null);
            var id = await InviteAsync(owner, invitee);

            await using var holder = new SqlConnection(database.ConnectionString);
            await holder.OpenAsync();
            await holder.ExecuteAsync("EXEC sp_set_session_context N'IsPlatform', 1;");
            await using var hold = await holder.BeginTransactionAsync();
            await holder.ExecuteAsync("SELECT Id FROM org.StaffInvitations WITH (UPDLOCK, ROWLOCK) WHERE PublicId = @id;", new { id }, hold);

            var accept = SendAsync(invitee, HttpMethod.Post, $"/api/me/invitations/{id}/accept");
            var cancel = SendAsync(owner, HttpMethod.Post, $"/api/institution/invitations/{id}/cancel");
            await WaitForLockWaitsOnInvitationsAsync(expected: 2);
            await hold.RollbackAsync();

            using var accepted = await accept;
            using var cancelled = await cancel;

            var status = await StatusOfAsync(id);
            if (accepted.StatusCode == HttpStatusCode.NoContent)
            {
                await AssertRefusedAsync(cancelled, HttpStatusCode.Conflict, "invitation_closed");
                Assert.Equal("Accepted", status);
                Assert.Equal(1, await ActiveRolesAsync(invitee, "T_STAFF"));
            }
            else
            {
                Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
                await AssertRefusedAsync(accepted, HttpStatusCode.Conflict, "invitation_closed");
                Assert.Equal("Revoked", status);
                Assert.Equal(0, await ActiveRolesAsync(invitee, "T_STAFF"));
            }
        }
    }

    private async Task WaitForLockWaitsOnInvitationsAsync(int expected)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (true)
        {
            var waiting = await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM sys.dm_tran_locks AS l
                JOIN sys.partitions AS p ON p.hobt_id = l.resource_associated_entity_id
                WHERE l.resource_database_id = DB_ID()
                  AND l.request_status = 'WAIT'
                  AND p.object_id = OBJECT_ID(N'org.StaffInvitations');
                """);

            if (waiting >= expected)
            {
                return;
            }

            Assert.True(DateTime.UtcNow < deadline, $"Only {waiting} of {expected} requests reached the claim.");
            await Task.Delay(50);
        }
    }
}
