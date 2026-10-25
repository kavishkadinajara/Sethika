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
