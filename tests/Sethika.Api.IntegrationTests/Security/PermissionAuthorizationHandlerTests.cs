using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Sethika.Api.Security;
using Sethika.Application.Abstractions;
using Sethika.Application.Access;

namespace Sethika.Api.IntegrationTests.Security;

/// <summary>
/// Unit tests (no I/O) for the [HasPermission] handler, with the permission service stubbed.
/// </summary>
public class PermissionAuthorizationHandlerTests
{
    private const string Code = "BOOKINGS.CALENDAR.APPROVE";

    private static async Task<bool> EvaluateAsync(ICurrentUserContext user, PermissionSet granted)
    {
        var handler = new PermissionAuthorizationHandler(user, new StubPermissionService(granted), new StubStepUpGrants());
        var requirement = new PermissionRequirement(Code);

        var context = new AuthorizationHandlerContext(
            [requirement],
            new ClaimsPrincipal(new ClaimsIdentity()),
            resource: null);

        await handler.HandleAsync(context);

        return context.HasSucceeded;
    }

    [Fact]
    public async Task Succeeds_when_the_user_holds_the_permission()
    {
        Assert.True(await EvaluateAsync(
            new StubUser { UserId = Guid.NewGuid(), InstitutionId = 7 },
            new PermissionSet([Code], [])));
    }

    [Fact]
    public async Task Fails_when_the_user_does_not_hold_the_permission()
    {
        Assert.False(await EvaluateAsync(
            new StubUser { UserId = Guid.NewGuid(), InstitutionId = 7 },
            new PermissionSet(["BOOKINGS.CALENDAR.VIEW"], [])));
    }

    /// <summary>
    /// The set arrives with denied codes already removed by the view, so a deny is
    /// indistinguishable from "never granted" at this layer - and both must be refused.
    /// </summary>
    [Fact]
    public async Task Fails_when_the_permission_was_denied_upstream()
    {
        Assert.False(await EvaluateAsync(
            new StubUser { UserId = Guid.NewGuid(), InstitutionId = 7 },
            PermissionSet.Empty));
    }

    /// <summary>An anonymous caller holds nothing, and must not reach the database to find out.</summary>
    [Fact]
    public async Task Fails_for_an_anonymous_caller_without_querying()
    {
        var service = new StubPermissionService(new PermissionSet([Code], []));
        var handler = new PermissionAuthorizationHandler(new StubUser(), service, new StubStepUpGrants());
        var requirement = new PermissionRequirement(Code);

        var context = new AuthorizationHandlerContext(
            [requirement], new ClaimsPrincipal(new ClaimsIdentity()), null);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
        Assert.Equal(0, service.CallCount);
    }

    /// <summary>Platform staff are not exempt: they still need the code.</summary>
    [Fact]
    public async Task Platform_staff_still_need_the_permission()
    {
        Assert.False(await EvaluateAsync(
            new StubUser { UserId = Guid.NewGuid(), IsPlatform = true },
            PermissionSet.Empty));
    }

    // ------------------------------------------------------------------ step-up (plan A5)

    private static async Task<(bool Succeeded, bool StepUpAsked)> EvaluateWithStepUpAsync(
        StubUser user,
        PermissionSet granted,
        StubStepUpGrants grants)
    {
        var handler = new PermissionAuthorizationHandler(user, new StubPermissionService(granted), grants);
        var httpContext = new DefaultHttpContext();

        var context = new AuthorizationHandlerContext(
            [new PermissionRequirement(Code)], new ClaimsPrincipal(new ClaimsIdentity()), httpContext);

        await handler.HandleAsync(context);

        return (context.HasSucceeded, StepUpAuthorizationResultHandler.IsStepUpRequired(httpContext));
    }

    [Fact]
    public async Task A_RequiresMfa_permission_is_refused_without_a_step_up_and_asks_for_one()
    {
        var user = new StubUser { UserId = Guid.NewGuid(), InstitutionId = 7, TokenId = "jti-1" };

        var (succeeded, asked) = await EvaluateWithStepUpAsync(
            user, new PermissionSet([Code], [Code]), new StubStepUpGrants());

        Assert.False(succeeded);
        Assert.True(asked);
    }

    [Fact]
    public async Task A_RequiresMfa_permission_is_allowed_with_a_grant_for_this_token()
    {
        var user = new StubUser { UserId = Guid.NewGuid(), InstitutionId = 7, TokenId = "jti-1" };

        var (succeeded, asked) = await EvaluateWithStepUpAsync(
            user, new PermissionSet([Code], [Code]), new StubStepUpGrants((user.UserId.Value, "jti-1")));

        Assert.True(succeeded);
        Assert.False(asked);
    }

    /// <summary>A refreshed token has a new jti, and must not inherit the old one's grant.</summary>
    [Fact]
    public async Task A_grant_for_another_token_does_not_count()
    {
        var user = new StubUser { UserId = Guid.NewGuid(), InstitutionId = 7, TokenId = "jti-2" };

        var (succeeded, _) = await EvaluateWithStepUpAsync(
            user, new PermissionSet([Code], [Code]), new StubStepUpGrants((user.UserId.Value, "jti-1")));

        Assert.False(succeeded);
    }

    [Fact]
    public async Task A_grant_for_another_user_does_not_count()
    {
        var user = new StubUser { UserId = Guid.NewGuid(), InstitutionId = 7, TokenId = "jti-1" };

        var (succeeded, _) = await EvaluateWithStepUpAsync(
            user, new PermissionSet([Code], [Code]), new StubStepUpGrants((Guid.NewGuid(), "jti-1")));

        Assert.False(succeeded);
    }

    [Fact]
    public async Task A_token_without_a_jti_cannot_have_stepped_up()
    {
        var user = new StubUser { UserId = Guid.NewGuid(), InstitutionId = 7 };
        var grants = new StubStepUpGrants(grantEverything: true);

        var (succeeded, asked) = await EvaluateWithStepUpAsync(user, new PermissionSet([Code], [Code]), grants);

        Assert.False(succeeded);
        Assert.True(asked);
    }

    /// <summary>Ordinary permissions do not touch the step-up store - one Redis call fewer per request.</summary>
    [Fact]
    public async Task A_permission_without_RequiresMfa_needs_no_step_up()
    {
        var user = new StubUser { UserId = Guid.NewGuid(), InstitutionId = 7, TokenId = "jti-1" };
        var grants = new StubStepUpGrants();

        var (succeeded, asked) = await EvaluateWithStepUpAsync(user, new PermissionSet([Code], []), grants);

        Assert.True(succeeded);
        Assert.False(asked);
        Assert.Equal(0, grants.CallCount);
    }

    /// <summary>Step-up cannot lift a refusal for a permission not held, so the client is not told to try.</summary>
    [Fact]
    public async Task A_permission_not_held_is_refused_without_asking_for_step_up()
    {
        var user = new StubUser { UserId = Guid.NewGuid(), InstitutionId = 7, TokenId = "jti-1" };

        var (succeeded, asked) = await EvaluateWithStepUpAsync(
            user, new PermissionSet(["BOOKINGS.CALENDAR.VIEW"], [Code]), new StubStepUpGrants(grantEverything: true));

        Assert.False(succeeded);
        Assert.False(asked);
    }

    private sealed class StubStepUpGrants(params (Guid UserId, string TokenId)[] granted) : IStepUpGrants
    {
        private readonly bool _grantEverything;

        public StubStepUpGrants(bool grantEverything)
            : this()
        {
            _grantEverything = grantEverything;
        }

        public int CallCount { get; private set; }

        public Task<bool> GrantAsync(Guid userId, string tokenId, TimeSpan lifetime, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The handler only reads grants.");

        public Task<bool> IsGrantedAsync(Guid userId, string tokenId, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_grantEverything || granted.Contains((userId, tokenId)));
        }
    }

    private sealed class StubUser : ICurrentUserContext
    {
        public Guid? UserId { get; init; }
        public int? InstitutionId { get; init; }
        public bool IsPlatform { get; init; }
        public string? TokenId { get; init; }
    }

    private sealed class StubPermissionService(PermissionSet granted) : IPermissionService
    {
        public int CallCount { get; private set; }

        public Task<PermissionSet> GetEffectivePermissionsAsync(
            Guid userId, int? institutionId, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(granted);
        }
    }
}
