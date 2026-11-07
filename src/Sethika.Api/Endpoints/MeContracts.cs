namespace Sethika.Api.Endpoints;

/// <summary><c>PUT /api/me/language</c>: one of <c>en</c>, <c>si</c>, <c>ta</c>.</summary>
public sealed record PreferredLanguageBody(string? Language);

/// <summary><c>PUT /api/me/devices</c>: this phone's Expo push token, <c>Android</c> or <c>iOS</c>, and the app's version.</summary>
public sealed record RegisterDeviceBody(string? ExpoPushToken, string? Platform, string? AppVersion);

/// <summary><c>POST /api/me/devices/remove</c>: the token to stop pushing to.</summary>
public sealed record RemoveDeviceBody(string? ExpoPushToken);
