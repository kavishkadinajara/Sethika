namespace Sethika.Api.Endpoints;

/// <summary><c>PUT /api/me/language</c>: one of <c>en</c>, <c>si</c>, <c>ta</c>.</summary>
public sealed record PreferredLanguageBody(string? Language);
