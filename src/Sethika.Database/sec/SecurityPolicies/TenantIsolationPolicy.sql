CREATE SECURITY POLICY sec.TenantIsolationPolicy
    /* private tenant data: filter reads + block writes */
    ADD FILTER PREDICATE sec.fn_TenantOrOwner(InstitutionId, DonorUserId) ON booking.Bookings,
    ADD BLOCK  PREDICATE sec.fn_TenantOrOwner(InstitutionId, DonorUserId) ON booking.Bookings AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_TenantOrOwner(InstitutionId, DonorUserId) ON booking.Bookings AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_TenantOrOwner(InstitutionId, DonorUserId) ON booking.RecurringBookings,
    ADD BLOCK  PREDICATE sec.fn_TenantOrOwner(InstitutionId, DonorUserId) ON booking.RecurringBookings AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_TenantOrOwner(InstitutionId, DonorUserId) ON booking.RecurringBookings AFTER UPDATE,
    /* a memorial plan names someone's dead: its donor alone, and no institution (plan 07, H13) */
    ADD FILTER PREDICATE sec.fn_Owner(DonorUserId) ON booking.MemorialPlans,
    ADD BLOCK  PREDICATE sec.fn_Owner(DonorUserId) ON booking.MemorialPlans AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Owner(DonorUserId) ON booking.MemorialPlans AFTER UPDATE,
    /* a donor's preferred places: theirs alone (plan 07, H12) */
    ADD FILTER PREDICATE sec.fn_Owner(UserId) ON auth.FavoriteInstitutions,
    ADD BLOCK  PREDICATE sec.fn_Owner(UserId) ON auth.FavoriteInstitutions AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Owner(UserId) ON auth.FavoriteInstitutions AFTER UPDATE,
    /* a co-sponsor: the institution, the person who answered, and the donor who invited (H18) */
    ADD FILTER PREDICATE sec.fn_Contributor(InstitutionId, UserId, InvitedBy) ON booking.BookingContributors,
    ADD BLOCK  PREDICATE sec.fn_Contributor(InstitutionId, UserId, InvitedBy) ON booking.BookingContributors AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Contributor(InstitutionId, UserId, InvitedBy) ON booking.BookingContributors AFTER UPDATE,
    /* giving (plan 09). A donation: its institution and the donor's account may read it (the receipt,
       M4); only the institution writes it (a donor's session cannot make itself a receipt; an online gift
       is recorded as the platform, M9), and never edits it (trg_Donations_Immutable, M7). An online
       gift (M8) and a pledge (M13): the institution and the donor; a donor's own pledge and cancel run
       as the platform (M14). A merchant and its secret (M10): the institution alone.                */
    ADD FILTER PREDICATE sec.fn_TenantOrOwner(InstitutionId, DonorUserId) ON giving.Donations,
    ADD BLOCK  PREDICATE sec.fn_Tenant(InstitutionId) ON giving.Donations AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(InstitutionId) ON giving.Donations AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_TenantOrOwner(InstitutionId, DonorUserId) ON giving.Pledges,
    ADD BLOCK  PREDICATE sec.fn_TenantOrOwner(InstitutionId, DonorUserId) ON giving.Pledges AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_TenantOrOwner(InstitutionId, DonorUserId) ON giving.Pledges AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_TenantOrOwner(InstitutionId, DonorUserId) ON giving.OnlinePayments,
    ADD BLOCK  PREDICATE sec.fn_TenantOrOwner(InstitutionId, DonorUserId) ON giving.OnlinePayments AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_TenantOrOwner(InstitutionId, DonorUserId) ON giving.OnlinePayments AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(InstitutionId) ON giving.PaymentMerchants,
    ADD BLOCK  PREDICATE sec.fn_Tenant(InstitutionId) ON giving.PaymentMerchants AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(InstitutionId) ON giving.PaymentMerchants AFTER UPDATE,
    /* the donor book (plan 08, K1): the institution only; a linked sponsor reads their days as bookings */
    ADD FILTER PREDICATE sec.fn_Tenant(InstitutionId) ON org.InstitutionDonors,
    ADD BLOCK  PREDICATE sec.fn_Tenant(InstitutionId) ON org.InstitutionDonors AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(InstitutionId) ON org.InstitutionDonors AFTER UPDATE,
    /* standing sponsorships and their let-go days (plan 08, K2, K4): the institution only. A day request:
       the institution and the donor who asked, as Bookings (K6). */
    ADD FILTER PREDICATE sec.fn_Tenant(InstitutionId) ON booking.StandingSponsorships,
    ADD BLOCK  PREDICATE sec.fn_Tenant(InstitutionId) ON booking.StandingSponsorships AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(InstitutionId) ON booking.StandingSponsorships AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(InstitutionId) ON booking.StandingExceptions,
    ADD BLOCK  PREDICATE sec.fn_Tenant(InstitutionId) ON booking.StandingExceptions AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(InstitutionId) ON booking.StandingExceptions AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_TenantOrOwner(InstitutionId, DonorUserId) ON booking.DayRequests,
    ADD BLOCK  PREDICATE sec.fn_TenantOrOwner(InstitutionId, DonorUserId) ON booking.DayRequests AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_TenantOrOwner(InstitutionId, DonorUserId) ON booking.DayRequests AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(InstitutionId) ON org.InstitutionSettings,
    ADD BLOCK  PREDICATE sec.fn_Tenant(InstitutionId) ON org.InstitutionSettings AFTER INSERT,
    ADD FILTER PREDICATE sec.fn_Tenant(InstitutionId) ON org.InstitutionDocuments,
    ADD FILTER PREDICATE sec.fn_Tenant(InstitutionId) ON org.InstitutionMembers,
    /* a staff invitation: its institution and the person invited (plan 04, X2) */
    ADD FILTER PREDICATE sec.fn_TenantOrOwner(InstitutionId, InviteeUserId) ON org.StaffInvitations,
    ADD BLOCK  PREDICATE sec.fn_TenantOrOwner(InstitutionId, InviteeUserId) ON org.StaffInvitations AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_TenantOrOwner(InstitutionId, InviteeUserId) ON org.StaffInvitations AFTER UPDATE,
    /* an application: its applicant, the platform, and once approved the institution it created (plan 02, B2) */
    ADD FILTER PREDICATE sec.fn_TenantOrOwner(CreatedInstitutionId, ApplicantUserId) ON org.RegistrationRequests,
    ADD BLOCK  PREDICATE sec.fn_TenantOrOwner(CreatedInstitutionId, ApplicantUserId) ON org.RegistrationRequests AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_TenantOrOwner(CreatedInstitutionId, ApplicantUserId) ON org.RegistrationRequests AFTER UPDATE,
    /* public-read data: anyone can read, only the owning tenant can write */
    ADD BLOCK  PREDICATE sec.fn_Tenant(InstitutionId) ON booking.MealSlots AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(InstitutionId) ON booking.MealSlots AFTER UPDATE,
    ADD BLOCK  PREDICATE sec.fn_Tenant(InstitutionId) ON booking.MealSlots BEFORE DELETE,
    ADD BLOCK  PREDICATE sec.fn_Tenant(InstitutionId) ON booking.BlockedDates AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(InstitutionId) ON booking.BlockedDates BEFORE DELETE,
    /* A need is public; only its institution writes it. A donor's pledge moves its counter as the
       platform (M14), so no donor session can change a need's title.                             */
    ADD BLOCK  PREDICATE sec.fn_Tenant(InstitutionId) ON giving.Needs AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(InstitutionId) ON giving.Needs AFTER UPDATE,
    /* religious services (Phase E, V9). An appointment: its institution and its donor, as Bookings.
       Service types: public read, like MealSlots. Leaders, their hours, leave, assignments and time
       blocks: the institution only, so a donor's cancel leaves the blocks to the outbox (V16). */
    ADD FILTER PREDICATE sec.fn_TenantOrOwner(InstitutionId, DonorUserId) ON booking.ServiceAppointments,
    ADD BLOCK  PREDICATE sec.fn_TenantOrOwner(InstitutionId, DonorUserId) ON booking.ServiceAppointments AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_TenantOrOwner(InstitutionId, DonorUserId) ON booking.ServiceAppointments AFTER UPDATE,
    ADD BLOCK  PREDICATE sec.fn_Tenant(InstitutionId) ON booking.ServiceTypes AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(InstitutionId) ON booking.ServiceTypes AFTER UPDATE,
    ADD BLOCK  PREDICATE sec.fn_Tenant(InstitutionId) ON booking.ServiceTypes BEFORE DELETE,
    ADD FILTER PREDICATE sec.fn_Tenant(InstitutionId) ON org.ReligiousLeaders,
    ADD BLOCK  PREDICATE sec.fn_Tenant(InstitutionId) ON org.ReligiousLeaders AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(InstitutionId) ON org.ReligiousLeaders AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(InstitutionId) ON org.LeaderWeeklyHours,
    ADD BLOCK  PREDICATE sec.fn_Tenant(InstitutionId) ON org.LeaderWeeklyHours AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(InstitutionId) ON org.LeaderWeeklyHours AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(InstitutionId) ON org.LeaderTimeOff,
    ADD BLOCK  PREDICATE sec.fn_Tenant(InstitutionId) ON org.LeaderTimeOff AFTER INSERT,
    ADD FILTER PREDICATE sec.fn_Tenant(InstitutionId) ON booking.AppointmentLeaders,
    ADD BLOCK  PREDICATE sec.fn_Tenant(InstitutionId) ON booking.AppointmentLeaders AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_Tenant(InstitutionId) ON booking.AppointmentLeaders AFTER UPDATE,
    ADD FILTER PREDICATE sec.fn_Tenant(InstitutionId) ON booking.LeaderTimeBlocks,
    ADD BLOCK  PREDICATE sec.fn_Tenant(InstitutionId) ON booking.LeaderTimeBlocks AFTER INSERT
WITH (STATE = ON, SCHEMABINDING = ON);
GO
