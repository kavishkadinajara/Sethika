using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Domain.Bookings;
using Sethika.Domain.Core;
using Sethika.Domain.Giving;

namespace Sethika.Application.Giving.Needs;

/// <summary>
/// An institution's needs (plan 09, M11): added as a draft, opened onto the board, changed while open,
/// closed or cancelled. Every method acts for the institution in the caller's token.
/// </summary>
/// <remarks>
/// The version the list read is what is checked. A pledge moves the need's counters and so its
/// <c>RowVersion</c>, so a step taken on an old reading is refused as stale rather than judged on numbers
/// that have changed. That is also what keeps "never below pledged" true while donors are pledging.
/// </remarks>
public sealed class NeedHandler(ISethikaDbContext db, TimeProvider timeProvider)
{
    /// <summary>The audit rows' entity.</summary>
    public const string Aggregate = "Need";

    /// <summary>Adds a need as a draft: nobody sees it until it is opened.</summary>
    public async Task<NeedResult> CreateAsync(int institutionId, Guid userId, NeedInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var details = await ReadAsync(input, today: SriLankaTime.Today(now), deadlineUnchanged: null, cancellationToken).ConfigureAwait(false);
        if (details.Invalid.Count > 0)
        {
            return NeedResult.Invalid([.. details.Invalid]);
        }

        var need = new Need
        {
            PublicId = Guid.CreateVersion7(now),
            InstitutionId = institutionId,
            QuantityNeeded = input.Quantity!.Value,
            CreatedBy = userId,
            CreatedAt = now,
        };
        details.CopyTo(need, input);

        db.Needs.Add(need);
        Audit(institutionId, userId, "Create", need, now);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return NeedResult.For(need.PublicId);
    }

    /// <summary>Changes a draft or an open need (M11).</summary>
    public async Task<NeedResult> UpdateAsync(int institutionId, Guid userId, Guid publicId, NeedInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var need = await FindAsync(institutionId, publicId, cancellationToken).ConfigureAwait(false);
        if (need is null)
        {
            return NeedResult.Failed(NeedErrors.NotFound);
        }

        if (input.Version is not { Length: 8 })
        {
            return NeedResult.Invalid("version");
        }

        if (!need.IsEditable)
        {
            return NeedResult.Failed(NeedErrors.WrongStatus);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var details = await ReadAsync(input, SriLankaTime.Today(now), need.Deadline, cancellationToken).ConfigureAwait(false);

        // What is promised was promised in this unit.
        if (details.UnitId is { } unitId && unitId != need.UnitLookupId && need.QuantityPledged > 0)
        {
            details.Invalid.Add("unit");
        }

        if (details.Invalid.Count > 0)
        {
            return NeedResult.Invalid([.. details.Invalid]);
        }

        if (need.ChangeQuantity(input.Quantity!.Value) is { } refused)
        {
            return NeedResult.Failed(refused);
        }

        details.CopyTo(need, input);
        need.UpdatedAt = now;
        need.UpdatedBy = userId;
        return await SaveAsync(need, input.Version, institutionId, userId, "Update", now, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Puts a draft on the public board.</summary>
    public Task<NeedResult> PublishAsync(int institutionId, Guid userId, Guid publicId, NeedStepInput input, CancellationToken cancellationToken = default) =>
        StepAsync(institutionId, userId, publicId, input, "Publish", need => need.Publish(), cancellationToken);

    /// <summary>Stops an open need taking more; what came stays counted.</summary>
    public Task<NeedResult> CloseAsync(int institutionId, Guid userId, Guid publicId, NeedStepInput input, CancellationToken cancellationToken = default) =>
        StepAsync(institutionId, userId, publicId, input, "Close", need => need.Close(), cancellationToken);

    /// <summary>Withdraws a need nothing came for.</summary>
    public Task<NeedResult> CancelAsync(int institutionId, Guid userId, Guid publicId, NeedStepInput input, CancellationToken cancellationToken = default) =>
        StepAsync(institutionId, userId, publicId, input, "Cancel", need => need.Cancel(), cancellationToken);

    private async Task<NeedResult> StepAsync(int institutionId, Guid userId, Guid publicId, NeedStepInput input, string action, Func<Need, string?> step, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var need = await FindAsync(institutionId, publicId, cancellationToken).ConfigureAwait(false);
        if (need is null)
        {
            return NeedResult.Failed(NeedErrors.NotFound);
        }

        if (input.Version is not { Length: 8 })
        {
            return NeedResult.Invalid("version");
        }

        if (step(need) is { } refused)
        {
            return NeedResult.Failed(refused);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        need.UpdatedAt = now;
        need.UpdatedBy = userId;
        return await SaveAsync(need, input.Version, institutionId, userId, action, now, cancellationToken).ConfigureAwait(false);
    }

    private Task<Need?> FindAsync(int institutionId, Guid publicId, CancellationToken cancellationToken) =>
        db.Needs.SingleOrDefaultAsync(n => n.PublicId == publicId && n.InstitutionId == institutionId, cancellationToken);

    private async Task<NeedResult> SaveAsync(Need need, byte[] version, int institutionId, Guid userId, string action, DateTime now, CancellationToken cancellationToken)
    {
        db.Entry(need).Property(n => n.RowVersion).OriginalValue = version;
        Audit(institutionId, userId, action, need, now);

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return NeedResult.Failed(NeedResult.Stale);
        }

        return NeedResult.For(need.PublicId);
    }

    private void Audit(int institutionId, Guid userId, string action, Need need, DateTime now) =>
        db.AuditLogs.Add(new AuditLogEntry
        {
            UserId = userId,
            InstitutionId = institutionId,
            Action = action,
            EntityName = Aggregate,
            EntityId = need.PublicId.ToString(),
            NewValues = JsonSerializer.Serialize(new { status = need.Status, quantity = need.QuantityNeeded }),
            OccurredAt = now,
        });

    private async Task<Details> ReadAsync(NeedInput input, DateOnly today, DateOnly? deadlineUnchanged, CancellationToken cancellationToken)
    {
        var invalid = new List<string>();

        var title = Tidy(input.Title);
        if (title is null || title.Length > Need.MaxTitle)
        {
            invalid.Add("title");
        }

        var description = Tidy(input.Description);
        if (description is { Length: > Need.MaxDescription })
        {
            invalid.Add("description");
        }

        var categoryId = await LookupAsync(LookupValue.NeedCategory, input.Category, cancellationToken).ConfigureAwait(false);
        if (categoryId is null)
        {
            invalid.Add("category");
        }

        var unitId = await LookupAsync(LookupValue.Unit, input.Unit, cancellationToken).ConfigureAwait(false);
        if (unitId is null)
        {
            invalid.Add("unit");
        }

        if (input.Quantity is not { } quantity || quantity <= 0 || quantity > PledgeHandler.MaxQuantity || decimal.Round(quantity, 2) != quantity)
        {
            invalid.Add("quantity");
        }

        if (!NeedPriority.All.Contains(input.Priority ?? string.Empty, StringComparer.Ordinal))
        {
            invalid.Add("priority");
        }

        // A deadline already passed may stay as it was; only a new one must be ahead.
        if (input.Deadline is { } deadline && deadline < today && deadline != deadlineUnchanged)
        {
            invalid.Add("deadline");
        }

        return new Details(invalid, title, description, categoryId, unitId);
    }

    private Task<int?> LookupAsync(string type, string? code, CancellationToken cancellationToken) =>
        db.LookupValues
            .Where(v => v.Code == code && v.IsActive)
            .Join(db.LookupTypes.Where(t => t.Code == type), v => v.LookupTypeId, t => t.Id, (v, t) => (int?)v.Id)
            .SingleOrDefaultAsync(cancellationToken);

    private static string? Tidy(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private sealed record Details(List<string> Invalid, string? Title, string? Description, int? CategoryId, int? UnitId)
    {
        public void CopyTo(Need need, NeedInput input)
        {
            need.Title = Title!;
            need.Description = Description;
            need.CategoryLookupId = CategoryId!.Value;
            need.UnitLookupId = UnitId!.Value;
            need.Priority = input.Priority!;
            need.Deadline = input.Deadline;
        }
    }
}
