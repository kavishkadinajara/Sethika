/*  Shapes of the access endpoints (InstitutionRoleEndpoints.cs, MeEndpoints.cs; plan 04, X6). */

/** GET /api/institution/roles */
export type InstitutionRole = {
  code: string;
  name: string;
  level: string;
  levelName: string;
  /** 1 is the most senior. */
  rank: number;
  /** Cloned from a template at approval (OWNER, MANAGER, STAFF, LEADER). */
  isSystem: boolean;
  /** The caller's rank is more senior: they may change it and hand it out. */
  canManage: boolean;
  members: number;
  permissions: string[];
};

/** GET /api/institution/permission-catalogue */
export type CataloguePermission = { code: string; action: string; name: string; requiresMfa: boolean; requires: string[] };
export type CatalogueScreen = { code: string; name: string; permissions: CataloguePermission[] };
export type CatalogueModule = { code: string; name: string; screens: CatalogueScreen[] };

/** GET /api/institution/role-levels: a level the caller may create roles at, and what such a role may hold. */
export type GrantableLevel = { level: string; name: string; rank: number; permissions: string[] };

/** GET /api/institution/members */
export type StaffMember = {
  userId: string;
  fullName: string;
  /** Masked by the database unless the login may unmask it. */
  phone: string | null;
  roles: { code: string; name: string; rank: number }[];
};

/** GET /api/institution/invitations */
export type PendingInvitation = {
  id: string;
  name: string;
  phone: string | null;
  roleCode: string;
  roleName: string;
  invitedAt: string;
  expiresAt: string;
  invitedBy: string;
};

/** GET /api/me/invitations */
export type MyInvitation = {
  id: string;
  institution: string;
  slug: string;
  roleName: string;
  invitedAt: string;
  expiresAt: string;
  invitedBy: string;
};
