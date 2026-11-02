using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Domain.Bookings;

namespace Sethika.Application.Bookings.Memorial;

/// <summary>A new memorial plan, as the planner's form sends it.</summary>
/// <param name="Religion">A <c>RELIGION</c> lookup code with active observances.</param>
/// <param name="Relationship">A <c>RELATIONSHIP</c> lookup code, or <see langword="null"/>.</param>
public sealed record CreateMemorialPlanCommand(string? DeceasedName, string? Relationship, DateOnly DateOfPassing, string? Religion, string? Notes);

/// <summary>The outcome of a change to a memorial plan: the plan's id, or why not.</summary>
public sealed record MemorialPlanResult(string? Error, IReadOnlyList<string> Fields, Guid? Plan)
{
    /// <summary>Some fields are missing or wrong; <see cref="Fields"/> lists them.</summary>
    public const string InvalidDetails = "invalid_details";

    /// <summary>No active plan of this donor's with that id.</summary>
    public const string NotFound = "memorial_plan_not_found";

    public bool Succeeded => Error is null;

    internal static MemorialPlanResult Ok(Guid plan) => new(null, [], plan);

    internal static MemorialPlanResult Invalid(IReadOnlyList<string> fields) => new(InvalidDetails, fields, null);
}

/// <summary>The donor's changes to their memorial plans (plan 07, H6).</summary>
/// <remarks>
/// A plan stores who died, when, and their faith; its observances are worked out when read, so there
/// is nothing else to keep in step. Row-Level Security makes every plan its donor's alone (H13).
/// </remarks>
public sealed class MemorialPlanHandler(ISethikaDbContext db, IMemorialQueries queries, TimeProvider timeProvider)
{
    /// <summary>The earliest date of passing a plan takes; anything before is a typing slip.</summary>
    public static readonly DateOnly EarliestDateOfPassing = new(1900, 1, 1);

    public async Task<MemorialPlanResult> CreateAsync(Guid donorId, CreateMemorialPlanCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var invalid = new List<string>();

        var name = Tidy(command.DeceasedName);
        if (name is null or { Length: > MemorialPlan.MaxDeceasedName })
        {
            invalid.Add("deceasedName");
        }

        // A plan remembers a death that has happened, in Colombo's today.
        if (command.DateOfPassing < EarliestDateOfPassing || command.DateOfPassing > SriLankaTime.Today(now))
        {
            invalid.Add("dateOfPassing");
        }

        var notes = Tidy(command.Notes);
        if (notes is { Length: > MemorialPlan.MaxNotes })
        {
            invalid.Add("notes");
        }

        var religion = Tidy(command.Religion);
        var relationship = Tidy(command.Relationship);
        var lookups = religion is null
            ? new MemorialLookups(null, false, null)
            : await queries.ResolveAsync(religion, relationship, cancellationToken).ConfigureAwait(false);

        // A religion with no observance would make a plan that shows nothing.
        if (lookups is not { Religion: not null, ReligionHasRules: true })
        {
            invalid.Add("religion");
        }

        if (relationship is not null && lookups.Relationship is null)
        {
            invalid.Add("relationship");
        }

        if (invalid.Count > 0)
        {
            return MemorialPlanResult.Invalid(invalid);
        }

        var plan = new MemorialPlan
        {
            PublicId = Guid.CreateVersion7(now),
            DonorUserId = donorId,
            DeceasedName = name!,
            RelationshipLookupId = lookups.Relationship,
            DateOfPassing = command.DateOfPassing,
            ReligionLookupId = lookups.Religion!.Value,
            Notes = notes,
            IsActive = true,
            CreatedAt = now,
        };

        db.MemorialPlans.Add(plan);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return MemorialPlanResult.Ok(plan.PublicId);
    }

    /// <summary>
    /// Archives <paramref name="publicId"/>: it leaves the planner and is never reminded of again. The
    /// bookings made from it, and any yearly booking, stay; the donor stops those from My bookings.
    /// <see langword="null"/> on success, else <see cref="MemorialPlanResult.NotFound"/>.
    /// </summary>
    /// <remarks>The donor id is checked as well as Row-Level Security, as with stopping a yearly booking.</remarks>
    public async Task<string?> ArchiveAsync(Guid publicId, Guid donorId, CancellationToken cancellationToken = default)
    {
        var archived = await db.MemorialPlans
            .Where(p => p.PublicId == publicId && p.DonorUserId == donorId && p.IsActive)
            .ExecuteUpdateAsync(set => set.SetProperty(p => p.IsActive, false), cancellationToken)
            .ConfigureAwait(false);

        return archived == 1 ? null : MemorialPlanResult.NotFound;
    }

    private static string? Tidy(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
