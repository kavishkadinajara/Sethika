/**
 * Shape of GET /api/me/permissions on the Sethika API.
 */
export interface EffectivePermissions {
  userId: string;
  isPlatform: boolean;
  /** Permission codes such as "BOOKINGS.CALENDAR.APPROVE". */
  permissions: string[];
}

export type PermissionCode = string;
