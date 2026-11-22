import type { APIRequestContext } from "@playwright/test";
import si from "../../messages/si.json";
import { signIn, type Account } from "../support/accounts";
import { expect, scrollsSideways, test } from "../support/test";
import { anonymous, bearer, codeOf, mobileLogin, signedIn, stepUpApi, throwaway, userRow, type MobileSession } from "./support/api";
import { account, evidence, tenant } from "./support/world";

/*  F04 - Institution switching and personal mode (P2). Cases: docs/qa/cases/F04-switching.md.
    M1 is T1's manager and a donor; a throwaway "QA tmp twohomes" is staff in T1 and manager in
    T3, so the session really has two places to be. Mobile endpoints carry the refresh token in
    the body; the web's cookie routes run the same SessionHandler.                            */

async function rotate(path: "switch-institution" | "switch-personal", session: MobileSession, institution?: string) {
  const api = await anonymous();
  try {
    const response = await api.post(`/api/auth/mobile/session/${path}`, { data: { refreshToken: session.refreshToken, institution } });
    return { status: response.status(), code: response.ok() ? null : await codeOf(response), session: response.ok() ? ((await response.json()) as MobileSession) : null };
  } finally {
    await api.dispose();
  }
}

async function as<T>(session: MobileSession, work: (api: APIRequestContext) => Promise<T>): Promise<T> {
  const api = await bearer(session.accessToken);
  try {
    return await work(api);
  } finally {
    await api.dispose();
  }
}

/** A throwaway who accepted STAFF in T1 and MANAGER in T3. */
async function twoHomes(): Promise<Account> {
  const person = await throwaway("twohomes");
  for (const [owner, role] of [["O1", "STAFF"], ["O3", "MANAGER"]] as const) {
    const api = await signedIn(account(owner));
    try {
      await stepUpApi(api, account(owner));
      const invited = await api.post("/api/institution/invitations", { data: { phone: person.phone, role } });
      expect(invited.status(), await invited.text()).toBe(201);
    } finally {
      await api.dispose();
    }
  }
  const mine = await signedIn(person);
  try {
    const invitations = (await (await mine.get("/api/me/invitations")).json()) as { id: string }[];
    expect(invitations).toHaveLength(2);
    for (const invitation of invitations) expect((await mine.post(`/api/me/invitations/${invitation.id}/accept`)).status()).toBe(204);
  } finally {
    await mine.dispose();
  }
  return person;
}

test("QA-F04-01 a manager who is also a donor moves between T1 and their own account, in Sinhala @phone", async ({ page }, testInfo) => {
  const m1 = account("M1");
  const t1 = tenant("T1");
  const signedInM1 = (await mobileLogin(m1)).session!;
  expect(signedInM1.activeInstitution, "one institution: sign-in puts them in it").toBe(t1.slug);
  expect(await as(signedInM1, async (api) => (await api.get("/api/institution/roles")).status())).toBe(200);

  const personal = await rotate("switch-personal", signedInM1);
  expect([personal.status, personal.session?.activeInstitution]).toEqual([200, null]);
  expect(await as(personal.session!, async (api) => (await api.get("/api/institution/roles")).status()), "staff screens from the own account").not.toBe(200);
  expect(await as(personal.session!, async (api) => (await api.get("/api/bookings/mine")).status()), "own bookings").toBe(200);
  const back = await rotate("switch-institution", personal.session!, t1.slug!);
  expect([back.status, back.session?.activeInstitution]).toEqual([200, t1.slug]);

  // On screen, from the context card (in the menu sheet on a phone).
  await signIn(page, m1);
  await page.goto("/si");
  const small = (page.viewportSize()?.width ?? 1280) < 1024;
  if (small) await page.getByRole("button", { name: si.Shell.openMenu }).click();
  const card = page.getByRole("button", { name: `${si.Shell.context.switch}: ${t1.name}` });
  await expect(card).toBeVisible();
  await card.click();
  await page.getByRole("menuitem", { name: si.Shell.context.personal }).click();
  if (small) await page.getByRole("button", { name: si.Shell.openMenu }).click();
  const personalCard = page.getByRole("button", { name: `${si.Shell.context.switch}: ${si.Shell.context.donor}` });
  await expect(personalCard).toBeVisible();
  expect(await scrollsSideways(page)).toBe(false);
  await evidence(page, "QA-F04-01", testInfo, "personal");
  await personalCard.click();
  await page.getByRole("menuitem", { name: t1.name }).click();
  if (small) await page.getByRole("button", { name: si.Shell.openMenu }).click();
  await expect(page.getByRole("button", { name: `${si.Shell.context.switch}: ${t1.name}` })).toBeVisible();
});

test("QA-F04-02 someone in two places chooses one at sign-in and sees only that place's people", async ({ page }, testInfo) => {
  const person = await twoHomes();
  const [t1, t3] = [tenant("T1"), tenant("T3")];
  const start = (await mobileLogin(person)).session!;
  expect(start.activeInstitution, "two places: no guess").toBeNull();
  expect(start.institutions.map((i) => i.slug).sort()).toEqual([t1.slug, t3.slug].sort());

  await signIn(page, person);
  await page.goto("/select-institution");
  await expect(page.getByRole("button", { name: new RegExp(t1.name) }).or(page.getByRole("link", { name: new RegExp(t1.name) })).first()).toBeVisible();
  await evidence(page, "QA-F04-02", testInfo, "choose");
  await page.getByRole("button", { name: new RegExp(t3.name) }).or(page.getByRole("link", { name: new RegExp(t3.name) })).first().click();
  await page.waitForURL((url) => !url.pathname.endsWith("/select-institution"));
  await page.goto("/access/members");
  await expect(page.getByText("Sriyani Abeysekera").first()).toBeVisible();
  await expect(page.getByText("Ven. Ananda")).toHaveCount(0);
  await evidence(page, "QA-F04-02", testInfo, "t3-members");

  const inT3 = await rotate("switch-institution", start, t3.slug!);
  const names = await as(inT3.session!, async (api) => JSON.stringify(await (await api.get("/api/institution/members")).json()));
  expect(names).toContain("Sriyani Abeysekera");
  for (const outsider of ["Ven. Ananda", "Dilani Karunaratne", "Selvam Kanagaratnam"]) expect(names).not.toContain(outsider);
});

test("QA-F04-03 permissions follow the switch at once: staff in T1, manager in T3, staff again in T1", async () => {
  const person = await twoHomes();
  const [t1, t3] = [tenant("T1"), tenant("T3")];
  const start = (await mobileLogin(person)).session!;
  const inT1 = (await rotate("switch-institution", start, t1.slug!)).session!;
  expect(await as(inT1, async (api) => (await api.get("/api/institution/members")).status()), "staff in T1").toBe(403);
  const inT3 = (await rotate("switch-institution", inT1, t3.slug!)).session!;
  expect(await as(inT3, async (api) => (await api.get("/api/institution/members")).status()), "manager in T3").toBe(200);
  const againT1 = (await rotate("switch-institution", inT3, t1.slug!)).session!;
  expect(await as(againT1, async (api) => (await api.get("/api/institution/members")).status()), "staff in T1 again").toBe(403);
  // The T3 token handed back by the switch is now a rotated one: it cannot be refreshed into T3 again.
  expect((await rotate("switch-institution", inT3, t3.slug!)).status).not.toBe(200);
});

test("QA-F04-04 a session never moves into a place the person is not in, or one that is suspended", async ({ page }, testInfo) => {
  const d1 = (await mobileLogin(account("D1"))).session!;
  const foreign = await rotate("switch-institution", d1, tenant("T1").slug!);
  expect([foreign.status, foreign.code]).toEqual([403, "institution_not_available"]);
  const made = await rotate("switch-institution", d1, "no-such-place");
  expect(made.status).toBe(403);

  // O4 owns T4, which is suspended: sign-in offers no institution, and asking for it is refused.
  const o4 = (await mobileLogin(account("O4"))).session!;
  expect(o4.activeInstitution).toBeNull();
  expect(o4.institutions.map((i) => i.slug)).not.toContain(tenant("T4").slug!);
  const suspended = await rotate("switch-institution", o4, tenant("T4").slug!);
  expect([suspended.status, suspended.code]).toEqual([403, "institution_not_available"]);
  expect(await as(o4, async (api) => (await api.get("/api/institution/members")).status())).not.toBe(200);

  await signIn(page, account("O4"));
  await page.goto("/select-institution");
  await expect(page.getByText("You aren't a member of any institution.", { exact: false })).toBeVisible();
  await evidence(page, "QA-F04-04", testInfo);
});

test("QA-F04-05 deep links to staff and platform screens from the wrong session show nothing and the API refuses", async ({ page }) => {
  const m1 = account("M1");
  const personal = (await rotate("switch-personal", (await mobileLogin(m1)).session!)).session!;
  await as(personal, async (api) => {
    expect((await api.get("/api/institution/members")).status(), "a staff API from the own account").not.toBe(200);
    expect((await api.get("/api/admin/registrations/")).status(), "a platform API").toBe(403);
  });

  await signIn(page, account("D1"));
  await page.goto("/access/members");
  await expect(page.getByText("Ven. Ananda")).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Invite someone" })).toHaveCount(0);
  await page.goto("/admin/registrations");
  await expect(page.getByText("REQ-2026-000006")).toHaveCount(0);
  await page.goto("/bookings/calendar");
  await expect(page.getByRole("button", { name: /Approve/ })).toHaveCount(0);
});

test("QA-F04-06 taken out of a place mid-session, the person loses it at the next request and cannot switch back", async () => {
  const person = await twoHomes();
  const t3 = tenant("T3");
  const inT3 = (await rotate("switch-institution", (await mobileLogin(person)).session!, t3.slug!)).session!;
  expect(await as(inT3, async (api) => (await api.get("/api/institution/members")).status())).toBe(200);

  const owner = await signedIn(account("O3"));
  try {
    await stepUpApi(owner, account("O3"));
    const revoked = await owner.delete(`/api/institution/roles/MANAGER/members/${userRow(person.phone)!.id}`);
    expect(revoked.status(), await revoked.text()).toBe(204);
  } finally {
    await owner.dispose();
  }
  expect(await as(inT3, async (api) => (await api.get("/api/institution/members")).status()), "the live T3 token").toBe(403);
  const fresh = (await mobileLogin(person)).session!;
  expect(fresh.institutions.map((i) => i.slug)).not.toContain(t3.slug);
  const back = await rotate("switch-institution", fresh, t3.slug!);
  expect([back.status, back.code]).toEqual([403, "institution_not_available"]);
});
