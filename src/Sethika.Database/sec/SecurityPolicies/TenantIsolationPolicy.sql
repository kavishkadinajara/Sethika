CREATE SECURITY POLICY sec.TenantIsolationPolicy
    /* private tenant data: filter reads + block writes */
    ADD FILTER PREDICATE sec.fn_TenantOrOwner(InstitutionId, DonorUserId) ON booking.Bookings,
    ADD BLOCK  PREDICATE sec.fn_TenantOrOwner(InstitutionId, DonorUserId) ON booking.Bookings AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_TenantOrOwner(InstitutionId, DonorUserId) ON booking.Bookings AFTER UPDATE,
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
    ADD FILTER PREDICATE sec.fn_Tenant(InstitutionId) ON org.StaffInvitations,
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
    ADD BLOCK  PREDICATE sec.fn_Tenant(InstitutionId) ON giving.Needs AFTER INSERT   -- no AFTER UPDATE: donor pledges update pledged qty
WITH (STATE = ON, SCHEMABINDING = ON);
GO
