import { empty, json, jsonBody, send, type OnboardingFailure, type OnboardingResult } from "@/lib/onboarding/api";
import type { AdminInstitution, AdminUser, PlatformRole } from "./types";

/*  Typed calls to the platform administration endpoints (plan 04, U4). Entering an institution
    is two steps (Y1a): this asks for a support ticket, behind a step-up that apiFetch handles,
    and session.switchInstitution then spends it.                                              */

export type AdminFailure = OnboardingFailure;
export type AdminResult<T = void> = OnboardingResult<T>;

const BASE = "/api/admin";
const part = (value: string) => encodeURIComponent(value);

export async function listInstitutions(search: string): Promise<AdminResult<AdminInstitution[]>> {
  return json(await send(`${BASE}/institutions?search=${part(search)}`, { method: "GET" }));
}

export async function requestSupport(slug: string): Promise<AdminResult> {
  return empty(await send(`${BASE}/institutions/${part(slug)}/support`, { method: "POST" }));
}

export async function listUsers(search: string): Promise<AdminResult<AdminUser[]>> {
  return json(await send(`${BASE}/users?search=${part(search)}`, { method: "GET" }));
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
