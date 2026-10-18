using Sethika.Application.Abstractions;
using Sethika.Application.Access;

namespace Sethika.Api.Endpoints;

/// <summary>The legal documents a donor reads, and accepts by hash, before signing up.</summary>
internal static class LegalEndpoints
{
    public static IEndpointRouteBuilder MapLegalEndpoints(this IEndpointRouteBuilder app)
    {
        // Anonymous: the signup screen shows these before any account exists. Public text only.
        app.MapGet("/api/legal/current", GetCurrentAsync).AllowAnonymous();

        return app;
    }

    private static async Task<IResult> GetCurrentAsync(
        string? language,
        ILegalDocumentQueries queries,
        CancellationToken cancellationToken)
    {
        var resolved = SupportedLanguages.Resolve(language);
        if (resolved is null)
        {
            return ApiErrors.Error(StatusCodes.Status400BadRequest, "invalid_language");
        }

        return Results.Ok(await queries.GetCurrentAsync(resolved, cancellationToken));
    }
}
