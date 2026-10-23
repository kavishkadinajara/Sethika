using Sethika.Application.Abstractions;

namespace Sethika.Api.Endpoints;

/// <summary>How a stored document leaves the API (plan 02, B4).</summary>
internal static class FileResults
{
    /// <summary>
    /// The file as a download, never as a page: <c>Content-Disposition: attachment</c>, the type
    /// decided when it was uploaded, and <c>nosniff</c> so no browser second-guesses that type into
    /// something it would run.
    /// </summary>
    public static async Task<IResult> AttachmentAsync(HttpContext httpContext, IFileStorage storage, StoredFileLocation file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(file);

        var content = await storage.OpenReadAsync(file.StorageKey, cancellationToken);

        httpContext.Response.Headers.XContentTypeOptions = "nosniff";
        httpContext.Response.Headers.CacheControl = "private, no-store";

        return Results.File(content, file.ContentType, fileDownloadName: file.OriginalName);
    }
}
