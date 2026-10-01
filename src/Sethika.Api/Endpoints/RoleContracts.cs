namespace Sethika.Api.Endpoints;

/// <summary>A new role. Everything is named by code - internal ids are never sent (CLAUDE.md).</summary>
public sealed record CreateRoleBody(string? Code, string? Name, string? Level, string[]? Permissions);

/// <summary>Everything the role should grant from now on.</summary>
public sealed record RolePermissionsBody(string[]? Permissions);

/// <summary>The member to give a role to, by the user id their own token carries as <c>sub</c>.</summary>
public sealed record RoleMemberBody(Guid? User);

/// <summary>The role as created.</summary>
public sealed record RoleResponse(string Code, string Name, string Level, IReadOnlyList<string> Permissions);
