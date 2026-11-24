using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Domain.Bookings;
using Sethika.Domain.Core;
using Sethika.Domain.Services;

namespace Sethika.Application.Services.Appointments;

/// <summary>
/// A signed-in donor asks an institution for a religious service (Phase E, section 4, flow 1).
/// </summary>
/// <remarks>
/// A request holds no leader, so unlike a booking there is nothing to lock and nothing to count:
/// one transaction reads the service type and its institution fresh, asks
/// <see cref="ServiceRules.CanRequest"/>, numbers the appointment, and saves it with its history and
/// its outbox message. Whether anyone is free is decided when leaders are assigned (V2).
/// </remarks>
public sealed class RequestServiceHandler(
    ISethikaDbContext db,
    IInstitutionModules modules,
    INumberSeries numbers,
    TimeProvider timeProvider)
{
    /// <summary>Written when a request is made: the institution is told.</summary>
    public const string RequestedEvent = "AppointmentRequested";

    // The columns' lengths in booking.ServiceAppointments.
    private const int MaxAddress = 300;
    private const int MaxOccasion = 200;
    private const int MaxNote = 500;

    public async Task<RequestServiceResult> RequestAsync(Guid donorId, RequestServiceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var location = command.Location?.Trim();
        var invalid = new List<string>();
        if (location is not (ServiceLocation.AtInstitution or ServiceLocation.AtDonorPlace))
        {
            invalid.Add("location");
        }

        if (command.Address?.Trim().Length > MaxAddress)
        {
            invalid.Add("address");
        }

        if (command.Occasion?.Trim().Length > MaxOccasion)
        {
            invalid.Add("occasion");
        }

        if (command.Note?.Trim().Length > MaxNote)
        {
            invalid.Add("note");
        }

        if (invalid.Count > 0)
        {
            return RequestServiceResult.Invalid(invalid);
        }

        var donor = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == donorId)
            .Select(u => new { u.FullName, u.PhoneNumber, u.PhoneConfirmed })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (donor is not { PhoneConfirmed: true, PhoneNumber: { Length: > 0 } phone })
        {
            return RequestServiceResult.Failed(RequestServiceResult.PhoneRequired);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        // The number is taken inside the transaction that saves it, so a refused save gives it back.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var type = await db.ServiceTypes
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.PublicId == command.ServiceType, cancellationToken)
            .ConfigureAwait(false);

        if (type is null)
        {
            return RequestServiceResult.Failed(RequestServiceResult.ServiceNotFound);
        }

        // An institution that switched services off takes no requests, whatever its types say; the
        // public page stops listing them for the same reason.
        if (!await modules.IsOnAsync(type.InstitutionId, IInstitutionModules.Services, cancellationToken).ConfigureAwait(false))
        {
            return RequestServiceResult.Failed(ServiceErrors.ServiceInactive);
        }

        var institutionStatus = await db.Institutions
            .Where(i => i.Id == type.InstitutionId)
            .Select(i => i.Status)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        var startsAt = SriLankaTime.ToUtc(command.Date, command.StartTime);
        var refused = ServiceRules.CanRequest(type, institutionStatus, startsAt, location!, command.Address, now);
        if (refused is not null)
        {
            return RequestServiceResult.Failed(refused);
        }

        var appointmentNo = await numbers
            .NextAsync(NumberSeriesDefinition.Appointment, NumberSeriesDefinition.PlatformScope, now.Year, cancellationToken)
            .ConfigureAwait(false);

        var appointment = ServiceAppointment.Request(
            appointmentNo, type, startsAt, location!, command.Address, command.Occasion, command.Note,
            donorId, donor.FullName, phone, now);
        db.ServiceAppointments.Add(appointment);

        db.OutboxMessages.Add(new OutboxMessage
        {
            EventType = RequestedEvent,
            AggregateType = nameof(ServiceAppointment),
            AggregateId = appointmentNo,
            Payload = JsonSerializer.Serialize(new { appointmentNo, donorUserId = donorId }),
            OccurredAt = now,
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return RequestServiceResult.Ok(appointmentNo, appointment.Status);
    }
}
