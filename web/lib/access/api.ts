import { empty, json, jsonBody, send, type OnboardingFailure, type OnboardingResult } from "@/lib/onboarding/api";
import type {
  CatalogueModule,
  GrantableLevel,
  InstitutionRole,
  MyInvitation,
  PendingInvitation,
  StaffMember,
} from "./types";

/*  Typed calls to the access endpoints (plan 04, U3): roles, the permission catalogue, staff and
    invitations of the session's institution, and the caller's own invitations. The same refusal
    shape as the onboarding calls; a role refusal's offending permissions arrive in `fields`.
    Every write here needs a step-up, which apiFetch handles (StepUpHost).                       */

export type AccessFailure = OnboardingFailure;
export type AccessResult<T = void> = OnboardingResult<T>;

const BASE = "/api/institution";
const code = (value: string) => encodeURIComponent(value);

// ---- reads ----------------------------------------------------------------------------------------

export async function listRoles(): Promise<AccessResult<InstitutionRole[]>> {
  return json(await send(`${BASE}/roles`, { method: "GET" }));
}

export async function getCatalogue(): Promise<AccessResult<CatalogueModule[]>> {
  return json(await send(`${BASE}/permission-catalogue`, { method: "GET" }));
}

export async function getRoleLevels(): Promise<AccessResult<GrantableLevel[]>> {
  return json(await send(`${BASE}/role-levels`, { method: "GET" }));
}

export async function listMembers(): Promise<AccessResult<StaffMember[]>> {
  return json(await send(`${BASE}/members`, { method: "GET" }));
}

export async function listInvitations(): Promise<AccessResult<PendingInvitation[]>> {
  return json(await send(`${BASE}/invitations`, { method: "GET" }));
}

// ---- roles ----------------------------------------------------------------------------------------

export async function createRole(input: { code: string; name: string; level: string; permissions: string[] }): Promise<AccessResult> {
  return empty(await send(`${BASE}/roles`, jsonBody("POST", input)));
}

export async function setRolePermissions(role: string, permissions: string[]): Promise<AccessResult> {
  return empty(await send(`${BASE}/roles/${code(role)}/permissions`, jsonBody("PUT", { permissions })));
}

export async function assignRole(role: string, user: string): Promise<AccessResult> {
  return empty(await send(`${BASE}/roles/${code(role)}/members`, jsonBody("POST", { user })));
}

export async function revokeRole(role: string, user: string): Promise<AccessResult> {
  return empty(await send(`${BASE}/roles/${code(role)}/members/${code(user)}`, { method: "DELETE" }));
}

// ---- invitations ----------------------------------------------------------------------------------

export async function invite(phone: string, role: string): Promise<AccessResult<{ id: string }>> {
  return json(await send(`${BASE}/invitations`, jsonBody("POST", { phone, role })));
}

export async function cancelInvitation(id: string): Promise<AccessResult> {
  return empty(await send(`${BASE}/invitations/${code(id)}/cancel`, { method: "POST" }));
}

export async function myInvitations(): Promise<AccessResult<MyInvitation[]>> {
  return json(await send("/api/me/invitations", { method: "GET" }));
}

export async function answerInvitation(id: string, answer: "accept" | "decline"): Promise<AccessResult> {
  return empty(await send(`/api/me/invitations/${code(id)}/${answer}`, { method: "POST" }));
}
