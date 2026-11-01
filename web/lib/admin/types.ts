/*  Shapes of the platform administration endpoints (AdminEndpoints.cs; plan 04, Y1-Y6). */

/** GET /api/admin/institutions */
export type AdminInstitution = {
  slug: string;
  name: string;
  type: string;
  district: string | null;
  status: "Onboarding" | "Active" | "Suspended" | "Deactivated";
  owner: string | null;
  staff: number;
};

/** GET /api/admin/users */
export type AdminUser = {
  id: string;
  fullName: string;
  /** Masked by the database unless the login may unmask it. */
  phone: string | null;
  userType: "Donor" | "InstitutionStaff" | "PlatformStaff";
  status: "Active" | "PendingVerification" | "Suspended" | "Deactivated";
  institutions: { slug: string; name: string; roles: string[] }[];
  platformRoles: string[];
};

/** GET /api/admin/platform-roles: the global platform roles, and whether the caller may hand each out. */
export type PlatformRole = { code: string; name: string; rank: number; canManage: boolean };

/** How many institutions are in one status (GET /api/admin/institutions/statuses); every status comes, zero included. */
export type InstitutionStatusCount = { status: "Onboarding" | "Active" | "Suspended" | "Deactivated"; count: number };

/** An application decided (GET /api/admin/registrations/decided), newest first. */
export type RegistrationDecision = {
  requestNo: string;
  proposedName: string;
  /** The institution type's code. */
  institutionType: string;
  status: "Approved" | "Rejected";
  decidedAt: string;
  /** The slug of the institution an approval made; null for a rejection. */
  institution: string | null;
};

/** One page of a platform list, and how many there are in all. */
export type AdminPage<T> = { items: T[]; total: number; page: number; pageSize: number };
