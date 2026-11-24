import { request, type APIRequestContext, type APIResponse } from "@playwright/test";
import { createAccount, lastTextId, latestCode, password, type Account } from "../../support/accounts";
import { sql } from "../../support/sql";
import { e164, redis } from "./redis-cli";

/*  API-level steps of the QA cases: refusals, sessions and step-up checked at the API, because
    hiding a button is UX only (QA-PLAN section 3, "API negative checks"). Straight to the QA API,
    each "person" with an address of their own in X-Forwarded-For, as accounts.ts does.       */

export const apiURL = process.env.E2E_API_URL!;

export function clientAddress(): string {
  const octet = () => 1 + Math.floor(Math.random() * 254);
  return `10.${octet()}.${octet()}.${octet()}`;
}

/** A bare client from one address: no token. */
export function anonymous(address = clientAddress()): Promise<APIRequestContext> {
  return request.newContext({ baseURL: apiURL, extraHTTPHeaders: { "X-Forwarded-For": address } });
}

export type MobileSession = { accessToken: string; refreshToken: string; institutions: { slug: string; name: string }[]; activeInstitution: string | null };

/** Signs in on the mobile endpoints, whose refresh token comes in the body: sessions are testable without a cookie jar. */
export async function mobileLogin(account: Account, secret = password, address = clientAddress()): Promise<{ status: number; session: MobileSession | null; body: string }> {
  const api = await anonymous(address);
  try {
    const response = await api.post("/api/auth/mobile/login", { data: { login: account.phone, password: secret } });
    const body = await response.text();
    return { status: response.status(), session: response.ok() ? (JSON.parse(body) as MobileSession) : null, body };
  } finally {
    await api.dispose();
  }
}

/** An API client carrying <accessToken>. */
export function bearer(accessToken: string, address = clientAddress()): Promise<APIRequestContext> {
  return request.newContext({ baseURL: apiURL, extraHTTPHeaders: { Authorization: `Bearer ${accessToken}`, "X-Forwarded-For": address } });
}

/** Signs in and returns a client for the session (an institution one when they have a single institution). */
export async function signedIn(account: Account): Promise<APIRequestContext> {
  const { session, status, body } = await mobileLogin(account);
  if (!session) throw new Error(`Login ${account.fullName}: ${status} ${body}`);
  return bearer(session.accessToken);
}

/**
 * Starts a persona's step-up past its resend cooldown and its hourly send cap. A real person steps
 * up a few times an hour; P1 and O1 step up in nearly every case of a run, so without this the
 * cap (QA-F01-06 tests the same rule) would fail cases that are about something else.
 */
export function freshStepUp(phone: string) {
  redis("DEL", `otp:stepup:${e164(phone)}`, `otp:sends:stepup:${e164(phone)}`);
}

/** Proves who <account> is on <api>'s token with the texted code. */
export async function stepUpApi(api: APIRequestContext, account: Account) {
  freshStepUp(account.phone);
  const before = lastTextId(account.phone);
  const requested = await api.post("/api/auth/stepup/request");
  if (!requested.ok()) throw new Error(`Step-up request: ${requested.status()} ${await requested.text()}`);
  const { code } = await latestCode(account.phone, before);
  const verified = await api.post("/api/auth/stepup/verify", { data: { code } });
  if (!verified.ok()) throw new Error(`Step-up verify: ${verified.status()} ${await verified.text()}`);
}

export async function codeOf(response: APIResponse): Promise<string | null> {
  try {
    return ((await response.json()) as { code?: string }).code ?? null;
  } catch {
    return null;
  }
}

let throwaways = 0;

/** A QA account made for one destructive case (lockout, reset, logout-all), so the personas stay usable. */
export async function throwaway(label: string, language: "en" | "si" | "ta" = "en"): Promise<Account> {
  throwaways++;
  const phone = freePhone("07702");
  return createAccount(apiURL, `QA tmp ${label} ${throwaways}`, language, phone);
}

/** A number nobody on Sethika_QA holds, under <prefix> (07702... throwaways, 07703... sign-ups by hand). */
export function freePhone(prefix: string): string {
  for (;;) {
    const phone = prefix + String(Math.floor(Math.random() * 10 ** (10 - prefix.length))).padStart(10 - prefix.length, "0");
    const [taken] = sql("SELECT COUNT(*) FROM auth.Users WHERE RIGHT(PhoneNumber, 9) = '$(last9)';", { last9: phone.slice(-9) });
    if (taken === "0") return phone;
  }
}

/** The user row's id, status and phone as stored. */
export function userRow(phone: string): { id: string; status: string; phone: string; language: string } | null {
  const [row] = sql("SELECT CONCAT(Id, '|', Status, '|', PhoneNumber, '|', PreferredLanguage) FROM auth.Users WHERE RIGHT(PhoneNumber, 9) = '$(last9)';", { last9: phone.replace(/\D/g, "").slice(-9) });
  if (!row) return null;
  const [id, status, stored, language] = row.split("|");
  return { id: id.toLowerCase(), status, phone: stored, language };
}
