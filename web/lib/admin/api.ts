import { empty, json, jsonBody, send, type OnboardingFailure, type OnboardingResult } from "@/lib/onboarding/api";
import type { Lookups } from "@/lib/onboarding/types";
import { toApiQuery, type InstitutionQuery, type UserQuery } from "./list-query";
import type { AdminInstitution, AdminPage, AdminUser, InstitutionStatusCount, PlatformRole, RegistrationDecision } from "./types";

/*  Typed calls to the platform administration endpoints (plan 04, U4). Entering an institution
    is two steps (Y1a): this asks for a support ticket, behind a step-up that apiFetch handles,
    and session.switchInstitution then spends it.                                              */

export type AdminFailure = OnboardingFailure;
export type AdminResult<T = void> = OnboardingResult<T>;

const BASE = "/api/admin";
const part = (value: string) => encodeURIComponent(value);

/** One page of the institutions the query picks (plan 05, X4). */
export async function listInstitutions(query: InstitutionQuery): Promise<AdminResult<AdminPage<AdminInstitution>>> {
  return json(await send(`${BASE}/institutions?${toApiQuery(query)}`, { method: "GET" }));
}

/** The names behind the type and district codes the institutions list filters by. */
export async function getInstitutionLookups(language: string): Promise<AdminResult<Lookups>> {
  return json(await send(`${BASE}/institutions/lookups?language=${part(language)}`, { method: "GET" }));
}

/** Every institution status with how many are in it: the platform dashboard (plan 05, R4). */
export async function getInstitutionStatuses(): Promise<AdminResult<InstitutionStatusCount[]>> {
  return json(await send(`${BASE}/institutions/statuses`, { method: "GET" }));
}

/** The latest ten approvals and rejections, newest first: the platform dashboard's ledger. */
export async function listDecided(): Promise<AdminResult<RegistrationDecision[]>> {
  return json(await send(`${BASE}/registrations/decided`, { method: "GET" }));
}

export async function requestSupport(slug: string): Promise<AdminResult> {
  return empty(await send(`${BASE}/institutions/${part(slug)}/support`, { method: "POST" }));
}

/** One page of the accounts the query picks (plan 05, X4). */
export async function listUsers(query: UserQuery): Promise<AdminResult<AdminPage<AdminUser>>> {
  return json(await send(`${BASE}/users?${toApiQuery(query)}`, { method: "GET" }));
}

export async function createUser(input: { fullName: string; phone: string; language: string }): Promise<AdminResult<{ id: string }>> {
  return json(await send(`${BASE}/users`, jsonBody("POST", input)));
}

export async function setUserSuspended(id: string, suspended: boolean): Promise<AdminResult> {
  return empty(await send(`${BASE}/users/${part(id)}/${suspended ? "suspend" : "reactivate"}`, { method: "POST" }));
}

export async function listPlatformRoles(): Promise<AdminResult<PlatformRole[]>> {
  return json(await send(`${BASE}/platform-roles`, { method: "GET" }));
}

export async function grantPlatformRole(id: string, role: string): Promise<AdminResult> {
  return empty(await send(`${BASE}/users/${part(id)}/platform-roles`, jsonBody("POST", { role })));
}

export async function revokePlatformRole(id: string, role: string): Promise<AdminResult> {
  return empty(await send(`${BASE}/users/${part(id)}/platform-roles/${part(role)}`, { method: "DELETE" }));
}
