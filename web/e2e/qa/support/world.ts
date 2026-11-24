import { mkdirSync, readFileSync } from "node:fs";
import path from "node:path";
import type { APIRequestContext, Page, TestInfo } from "@playwright/test";
import type { Account } from "../../support/accounts";

/*  The QA world: what setup/personas.ts built on Sethika_QA, read back by every QA spec. It lives
    in .personas.json (git-ignored), rebuilt on every reset, so a spec names a persona ("D1") and
    never a phone number.                                                                      */

export const worldFile = path.join(__dirname, "..", ".personas.json");

export type PersonaKey =
  | "V1" | "D1" | "D2" | "D3" | "D4" | "D5" | "D6"
  | "O1" | "M1" | "S1" | "C1" | "L1" | "L2" | "O2" | "O3" | "O4" | "A1" | "P1" | "P2";
export type TenantKey = "T1" | "T2" | "T3" | "T4" | "T5" | "T6";

export type Persona = {
  key: PersonaKey;
  name: string;
  /** The number they sign in with, local format (07x...). */
  phone: string;
  language: "en" | "si" | "ta";
  /** Whether setup made an account; D3 signs up during F01, D6 and L2 have none. */
  hasAccount: boolean;
  role: string;
  tenant?: TenantKey;
};

export type Tenant = {
  key: TenantKey;
  name: string;
  type: string;
  requestNo: string;
  /** Approved ones only. */
  slug: string | null;
  /** Institution status, or the application's status for T5 / T6. */
  status: string;
};

export type World = { builtAt: string; commit: string; personas: Record<PersonaKey, Persona>; tenants: Record<TenantKey, Tenant> };

let cached: World | null = null;

export function world(): World {
  if (!cached) {
    try {
      cached = JSON.parse(readFileSync(worldFile, "utf8")) as World;
    } catch {
      throw new Error(`No QA world at ${worldFile}: run scripts/qa/qa.ps1 personas after a reset.`);
    }
  }
  return cached;
}

export function persona(key: PersonaKey): Persona {
  return world().personas[key];
}

/** A persona as the e2e helpers take an account (signIn, apiAs). */
export function account(key: PersonaKey): Account {
  const p = persona(key);
  return { phone: p.phone, fullName: p.name };
}

export function tenant(key: TenantKey): Tenant {
  return world().tenants[key];
}

const evidenceRoot = path.join(__dirname, "..", "..", "..", "..", "docs", "qa", "evidence");

/** A screenshot kept as evidence (QA-PLAN section 13): docs/qa/evidence/<Fxx>/<case>-<project>.png. */
export async function evidence(page: Page, caseId: string, testInfo: TestInfo, suffix = "") {
  const feature = /^QA-(F\d\d)-/.exec(caseId)?.[1] ?? "misc";
  const folder = path.join(evidenceRoot, feature);
  mkdirSync(folder, { recursive: true });
  await page.screenshot({ path: path.join(folder, `${caseId}${suffix ? `-${suffix}` : ""}-${testInfo.project.name}.png`), fullPage: true });
}

/** The JSON body of an API answer, with its status, for asserting refusals by code. */
export async function answer(response: Awaited<ReturnType<APIRequestContext["get"]>>): Promise<{ status: number; body: Record<string, unknown> | null }> {
  const text = await response.text();
  let body: Record<string, unknown> | null = null;
  try {
    body = text ? (JSON.parse(text) as Record<string, unknown>) : null;
  } catch {
    body = { raw: text };
  }
  return { status: response.status(), body };
}
