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
    ADD FILTER PREDICATE sec.fn_TenantOrOwner(InstitutionId, DonorUserId) ON giving.Donations,
    ADD BLOCK  PREDICATE sec.fn_TenantOrOwner(InstitutionId, DonorUserId) ON giving.Donations AFTER INSERT,
    ADD FILTER PREDICATE sec.fn_TenantOrOwner(InstitutionId, DonorUserId) ON giving.Pledges,
    ADD BLOCK  PREDICATE sec.fn_TenantOrOwner(InstitutionId, DonorUserId) ON giving.Pledges AFTER INSERT,
    ADD FILTER PREDICATE sec.fn_Tenant(InstitutionId) ON org.InstitutionDonors,
    ADD BLOCK  PREDICATE sec.fn_Tenant(InstitutionId) ON org.InstitutionDonors AFTER INSERT,
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
    ADD BLOCK  PREDICATE sec.fn_Tenant(InstitutionId) ON giving.Needs AFTER INSERT,  -- no AFTER UPDATE: donor pledges update pledged qty
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
