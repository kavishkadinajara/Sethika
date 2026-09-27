using System.Net.Mail;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sethika.Application.Abstractions;
using Sethika.Domain.Access;
using Sethika.Domain.Core;

namespace Sethika.Application.Access.SignUp;

/// <summary>
/// Creates a donor account: user, donor profile, the DONOR role, consent to the current legal
/// documents, and an outbox event - all in one transaction.
/// </summary>
/// <remarks>
/// The order of the checks is the design:
///
/// First, everything that can be checked without spending the verification ticket - phone format,
/// name, email, language, password policy, consent. A typo in the form then costs a correction, not
/// a second SMS.
///
/// Then the ticket is redeemed, and only after that is "already registered" reported. Checking
/// duplicates earlier would answer "does this number have an account?" for any number anyone typed;
/// after redemption it answers only someone who has just proved they hold the phone.
///
/// One SaveChanges writes all five tables, and EF runs a multi-statement save in one transaction, so
/// a failure anywhere leaves nothing behind - no account without its consent record, and no outbox
/// event for an account that does not exist.
/// </remarks>
public sealed class SignUpDonorHandler(
    ISethikaDbContext db,
    IOtpService otp,
    IPhoneNumberNormalizer phoneNumbers,
    IPasswordPolicy passwordPolicy,
    IPasswordHasher<User> passwordHasher,
    TimeProvider timeProvider)
{
    private const int FullNameMaxLength = 150;
    private const int EmailMaxLength = 256;
    private const string DonorRoleCode = "DONOR";

    /// <summary>The documents a donor must accept. Each in any one language.</summary>
    private static readonly string[] RequiredDocuments = ["TERMS", "PRIVACY"];

    /// <summary>Creates the account, or says why not.</summary>
    public async Task<SignUpDonorResult> HandleAsync(
        SignUpDonorCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!phoneNumbers.TryNormalize(command.PhoneNumber, out var phoneNumber))
        {
            return SignUpDonorResult.Failed(SignUpFailure.InvalidPhoneNumber);
        }

        var fullName = command.FullName?.Trim();
        if (string.IsNullOrEmpty(fullName) || fullName.Length > FullNameMaxLength)
        {
            return SignUpDonorResult.Failed(SignUpFailure.InvalidFullName);
        }

        var email = string.IsNullOrWhiteSpace(command.Email) ? null : command.Email.Trim();
        if (email is not null && !IsPlainEmailAddress(email))
        {
            return SignUpDonorResult.Failed(SignUpFailure.InvalidEmail);
        }

        var language = SupportedLanguages.Resolve(command.PreferredLanguage);
        if (language is null)
        {
            return SignUpDonorResult.Failed(SignUpFailure.InvalidLanguage);
        }

        var password = command.Password ?? string.Empty;
        var policy = passwordPolicy.Validate(password, [phoneNumber, email, fullName]);
        if (!policy.IsValid)
        {
            return SignUpDonorResult.Failed(SignUpFailure.PasswordRejected, policy.Failures);
        }

        var consentedDocumentIds = await ResolveConsentsAsync(command.AcceptedDocumentHashes, cancellationToken)
            .ConfigureAwait(false);
        if (consentedDocumentIds is null)
        {
            return SignUpDonorResult.Failed(SignUpFailure.ConsentMissing);
        }

        var ticket = await otp.RedeemTicketAsync(
            phoneNumber,
            OtpPurpose.Signup,
            command.VerificationTicket ?? string.Empty,
            cancellationToken).ConfigureAwait(false);

        if (ticket == OtpTicketOutcome.Unavailable)
        {
            return SignUpDonorResult.Failed(SignUpFailure.Unavailable);
        }

        if (ticket != OtpTicketOutcome.Redeemed)
        {
            return SignUpDonorResult.Failed(SignUpFailure.PhoneNotVerified);
        }

        // NormalizedEmail.From is the one normaliser signup, email change and sign-in share - the
        // third duty plan A1 took over from UserManager. Without it Test@x.com and test@x.com pass
        // UX_Users_NormalizedEmail as two different people.
        var normalizedEmail = email is null ? null : NormalizedEmail.From(email);

        if (await IsRegisteredAsync(phoneNumber, normalizedEmail, cancellationToken).ConfigureAwait(false))
        {
            return SignUpDonorResult.Failed(SignUpFailure.AlreadyRegistered);
        }

        var donorRoleId = await db.Roles
            .AsNoTracking()
            .Where(r => r.InstitutionId == null && r.Code == DonorRoleCode && r.IsActive)
            .Select(r => (int?)r.Id)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "The DONOR role is missing: the database was published without Scripts/Seed/08_SystemRoles.sql.");

        var now = timeProvider.GetUtcNow();

        // Generated here rather than by NEWSEQUENTIALID so the five rows can reference it before any
        // of them is inserted (plan A9).
        var user = new User
        {
            Id = Guid.CreateVersion7(now),
            UserType = "Donor",
            FullName = fullName,
            Email = email,
            NormalizedEmail = normalizedEmail,
            PhoneNumber = phoneNumber,
            PhoneConfirmed = true,
            PasswordChangedAt = now.UtcDateTime,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            PermissionsVersion = 1,
            PreferredLanguage = language,
            Status = "Active",
            CreatedAt = now.UtcDateTime,
        };
        user.PasswordHash = passwordHasher.HashPassword(user, password);

        db.Users.Add(user);
        db.DonorProfiles.Add(new DonorProfile { UserId = user.Id, CountryCode = "LK" });
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = donorRoleId, IsActive = true });

        foreach (var documentId in consentedDocumentIds)
        {
            db.UserConsents.Add(new UserConsent
            {
                UserId = user.Id,
                LegalDocumentId = documentId,
                AcceptedAt = now.UtcDateTime,
                IpAddress = Truncate(command.IpAddress, 45),
                UserAgent = Truncate(command.UserAgent, 400),
            });
        }

        // The id and language only. A consumer that needs the phone number looks it up, so the
        // outbox - which is read by background jobs and retained until processed - holds no PII.
        db.OutboxMessages.Add(new OutboxMessage
        {
            EventType = "DonorSignedUp",
            AggregateType = "User",
            AggregateId = user.Id.ToString(),
            Payload = JsonSerializer.Serialize(new { userId = user.Id, preferredLanguage = language }),
            OccurredAt = now.UtcDateTime,
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // The unique indexes are the real guarantee against two accounts for one phone or email;
            // the check above only turns the common case into a clear answer. When two signups race
            // past it, one insert fails here, and a fresh look says whether that is what happened.
            db.ChangeTracker.Clear();

            if (await IsRegisteredAsync(phoneNumber, normalizedEmail, cancellationToken).ConfigureAwait(false))
            {
                return SignUpDonorResult.Failed(SignUpFailure.AlreadyRegistered);
            }

            throw;
        }

        return SignUpDonorResult.Success(user.Id);
    }

    private Task<bool> IsRegisteredAsync(string phoneNumber, string? normalizedEmail, CancellationToken cancellationToken) =>
        db.Users
          .AsNoTracking()
          .AnyAsync(
              u => !u.IsDeleted
                   && (u.PhoneNumber == phoneNumber || (normalizedEmail != null && u.NormalizedEmail == normalizedEmail)),
              cancellationToken);

    /// <summary>
    /// The ids of the current documents whose hashes were accepted, or null unless every required
    /// document is among them.
    /// </summary>
    private async Task<List<short>?> ResolveConsentsAsync(
        IReadOnlyCollection<string>? acceptedHashes,
        CancellationToken cancellationToken)
    {
        if (acceptedHashes is null || acceptedHashes.Count == 0)
        {
            return null;
        }

        var accepted = new HashSet<string>(
            acceptedHashes.Where(h => !string.IsNullOrWhiteSpace(h)).Select(h => h.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var current = await db.LegalDocuments
            .AsNoTracking()
            .Where(d => d.IsCurrent && RequiredDocuments.Contains(d.Code))
            .Select(d => new { d.Id, d.Code, d.ContentSha256 })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var matched = current.Where(d => accepted.Contains(d.ContentSha256)).ToList();

        return RequiredDocuments.All(code => matched.Any(d => d.Code == code))
            ? [.. matched.Select(d => d.Id)]
            : null;
    }

    /// <summary>A bare address, not a display-name form such as <c>Name &lt;a@b.lk&gt;</c>.</summary>
    private static bool IsPlainEmailAddress(string email) =>
        email.Length <= EmailMaxLength
        && MailAddress.TryCreate(email, out var parsed)
        && string.Equals(parsed.Address, email, StringComparison.Ordinal);

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}
