import type { APIRequestContext } from "@playwright/test";
import { grantPlatformRole, lastTextId, latestCode, signIn, stepUp, type Account } from "../support/accounts";
import { sql } from "../support/sql";
import { clipsSideways, expect, scrollsSideways, test } from "../support/test";
import { anonymous, bearer, codeOf, freePhone, mobileLogin, signedIn, stepUpApi, throwaway, userRow, type MobileSession } from "./support/api";
import { answer, account, evidence, tenant, type PersonaKey } from "./support/world";

/*  F07 - Platform: institutions, users, support access, template sync (P2). Cases:
    docs/qa/cases/F07-platform.md. P1 (Anjali Herath) is the administrator, P2 (Tharindu Bandara)
    the reviewer. Suspending accounts and changing platform roles is done to throwaways only.
    Suspending / reactivating an institution has no product path (OBS-02): QA-F07-08 is N/A.   */

async function with_<T>(key: PersonaKey | Account, work: (api: APIRequestContext) => Promise<T>): Promise<T> {
  const api = await signedIn(typeof key === "string" ? account(key) : key);
  try {
    return await work(api);
  } finally {
    await api.dispose();
  }
}

async function rotate(path: "switch-institution" | "switch-personal", session: MobileSession, institution?: string) {
  const api = await anonymous();
  try {
    const response = await api.post(`/api/auth/mobile/session/${path}`, { data: { refreshToken: session.refreshToken, institution } });
    return { status: response.status(), code: response.ok() ? null : await codeOf(response), session: response.ok() ? ((await response.json()) as MobileSession) : null };
  } finally {
    await api.dispose();
  }
}

const one = (query: string, vars: Record<string, string> = {}) => sql(query, vars)[0];
/*  The platform's own changes are recorded as "PlatformAdministration" outbox rows (DECISIONS,
    access audit: "the audit row is committed first"), not in audit.AuditLogs, where approvals,
    rejections and template syncs go - OBS-07. The case reads them where they are.            */
const platformRecords = (change: string, aggregate: string) =>
  Number(one("SELECT COUNT(*) FROM core.OutboxMessages WHERE EventType = 'PlatformAdministration' AND AggregateId = '$(a)' AND JSON_VALUE(Payload, '$.change') = '$(c)';", { a: aggregate, c: change }));

test("QA-F07-01 P1 sees every institution with its status and owner; the filters hold; applications are not institutions @phone", async ({ page }, testInfo) => {
  await with_("P1", async (api) => {
    const list = async (query: string) => answer(await api.get(`/api/admin/institutions?pageSize=100&${query}`));
    const all = JSON.stringify((await list("")).body);
    for (const key of ["T1", "T2", "T3", "T4"] as const) expect(all, key).toContain(tenant(key).slug!);
    for (const key of ["T5", "T6"] as const) expect(all, `${key} is an application, not an institution`).not.toContain(tenant(key).name);
    const suspended = JSON.stringify((await list("status=Suspended")).body);
    expect(suspended).toContain(tenant("T4").slug!);
    for (const key of ["T1", "T2", "T3"] as const) expect(suspended).not.toContain(tenant(key).slug!);
    const kandy = JSON.stringify((await list("district=KDY")).body);
    expect(kandy).toContain(tenant("T1").slug!);
    expect(kandy).not.toContain(tenant("T2").slug!);
    for (const bad of ["status=Closed", "sort=random", "pageSize=101", "page=0", `type=${"X".repeat(31)}`]) expect((await list(bad)).status, bad).toBe(400);
    // Ids never leave: slugs and codes do.
    expect(all).not.toMatch(/"id"\s*:\s*\d+/);
  });
  await with_("P2", async (api) => expect((await api.get("/api/admin/institutions")).status(), "a reviewer sees the list too").toBe(200));

  await signIn(page, account("P1"));
  await page.goto("/admin/institutions");
  await expect(page.getByRole("heading", { level: 1, name: "Institutions" })).toBeVisible();
  await page.getByLabel(/^Search/).fill("Sri Bodhi");
  const card = page.getByRole("listitem").filter({ hasText: tenant("T1").name });
  await expect(card.getByText("Owner: Ven. Ananda")).toBeVisible();
  expect(await scrollsSideways(page)).toBe(false);
  expect(await clipsSideways(page)).toEqual([]);
  await evidence(page, "QA-F07-01", testInfo);
});

test("QA-F07-02 P1 enters T1 as platform support only after a step-up; it is audited, shown, and ends on Leave", async ({ page }, testInfo) => {
  const t1 = tenant("T1");
  const t1Id = one("SELECT Id FROM org.Institutions WHERE Slug = '$(slug)';", { slug: t1.slug! });
  const audited = () => platformRecords("SupportTicketIssued", t1Id);
  const auditBefore = audited();

  await signIn(page, account("P1"));
  await page.goto("/admin/institutions");
  await page.getByLabel(/^Search/).fill("Sri Bodhi");
  const card = page.getByRole("listitem").filter({ hasText: t1.name });
  const before = lastTextId(account("P1").phone);
  await card.getByRole("button", { name: "Open as platform support" }).click();
  await stepUp(page, account("P1"), before);
  const banner = page.getByRole("status").filter({ hasText: "as platform support" });
  await expect(banner).toContainText(t1.name);
  expect(audited(), "one audit row for the entry").toBe(auditBefore + 1);
  await page.goto("/access/members");
  await expect(page.getByText("Dilani Karunaratne").first()).toBeVisible();
  await evidence(page, "QA-F07-02", testInfo, "inside");
  // While inside, the platform's own screens are closed.
  await page.goto("/admin/users");
  await expect(page.getByRole("button", { name: "Create an account" })).toHaveCount(0);
  await banner.getByRole("button", { name: "Leave" }).click();
  await expect(page).toHaveURL(/\/admin\/institutions$/);
  await expect(page.getByRole("status").filter({ hasText: "as platform support" })).toHaveCount(0);

  // P2 is platform staff too, but has no support permission: even stepped up, no ticket.
  await with_("P2", async (api) => {
    await stepUpApi(api, account("P2"));
    expect((await api.post(`/api/admin/institutions/${t1.slug}/support`)).status()).toBe(403);
  });
  expect(audited()).toBe(auditBefore + 1);
});

test("QA-F07-03 a support ticket is one-use, for one institution, live ones only, and nothing without it", async () => {
  const p1 = account("P1");
  const [t1, t3, t4] = [tenant("T1"), tenant("T3"), tenant("T4")];
  const start = (await mobileLogin(p1)).session!;
  expect(start.activeInstitution).toBeNull();
  const noTicket = await rotate("switch-institution", start, t1.slug!);
  expect([noTicket.status, noTicket.code], "no ticket").toEqual([403, "institution_not_available"]);

  const api = await bearer(noTicket.session?.accessToken ?? start.accessToken);
  let session = start;
  try {
    // The refused switch did not spend the session's token; sign in again to be sure of a live one.
    session = (await mobileLogin(p1)).session!;
    const issuer = await bearer(session.accessToken);
    try {
      await stepUpApi(issuer, p1);
      expect((await issuer.post(`/api/admin/institutions/${t4.slug}/support`)).status(), "suspended T4").toBe(404);
      expect((await issuer.post("/api/admin/institutions/no-such-place/support")).status()).toBe(404);
      expect((await issuer.post(`/api/admin/institutions/${t1.slug}/support`)).status()).toBe(204);
    } finally {
      await issuer.dispose();
    }
    // A ticket for T1 does not open T3.
    const wrong = await rotate("switch-institution", session, t3.slug!);
    expect(wrong.status, "T1's ticket into T3").toBe(403);
    session = (await mobileLogin(p1)).session!;
    const inside = await rotate("switch-institution", session, t1.slug!);
    expect([inside.status, inside.session?.activeInstitution]).toEqual([200, t1.slug]);
    const members = await bearer(inside.session!.accessToken);
    try {
      expect((await members.get("/api/institution/members")).status(), "support sees T1's people").toBe(200);
    } finally {
      await members.dispose();
    }
    // Out and back in: the ticket is spent.
    const out = await rotate("switch-personal", inside.session!);
    expect(out.status).toBe(200);
    const again = await rotate("switch-institution", out.session!, t1.slug!);
    expect([again.status, again.code], "spent").toEqual([403, "institution_not_available"]);
  } finally {
    await api.dispose();
  }
});

test("QA-F07-04 P1 makes an account for someone who cannot sign up; it signs in with a texted code", async ({ browser }) => {
  const phone = freePhone("07702");
  await with_("P1", async (api) => {
    const create = (data: unknown) => api.post("/api/admin/users", { data });
    expect(await codeOf(await create({ fullName: "QA Sunil Gamage", phone, language: "si" })), "needs a step-up").toBe("stepup_required");
    await stepUpApi(api, account("P1"));
    const refused: [unknown, number, string][] = [
      [{ fullName: " ", phone, language: "si" }, 400, "invalid_full_name"],
      [{ fullName: "QA Sunil Gamage", phone: "0112345678", language: "si" }, 400, "invalid_phone_number"],
      [{ fullName: "QA Sunil Gamage", phone: "12345", language: "si" }, 400, "invalid_phone_number"],
      [{ fullName: "QA Sunil Gamage", phone, language: "fr" }, 400, "unsupported_language"],
      [{ fullName: "QA Someone", phone: account("D1").phone, language: "en" }, 409, "already_registered"],
    ];
    for (const [data, status, code] of refused) {
      const r = await answer(await create(data));
      expect([r.status, r.body?.code], JSON.stringify(data)).toEqual([status, code]);
    }
    const made = await create({ fullName: "QA Sunil Gamage", phone: `+94 ${phone.slice(1, 3)} ${phone.slice(3)}`, language: "si" });
    expect(made.status()).toBe(201);
    expect(JSON.stringify(await made.json())).not.toMatch(/"id"\s*:\s*\d+/);
    expect(await codeOf(await create({ fullName: "QA Sunil again", phone, language: "si" })), "the same number again").toBe("already_registered");
  });
  const row = userRow(phone)!;
  expect([row.status, row.language, row.phone]).toEqual(["PendingVerification", "si", `+94${phone.slice(1)}`]);
  expect(platformRecords("UserCreated", row.id), "recorded with who made it").toBe(1);

  // They sign in with a texted code (no password was ever set).
  const context = await browser.newContext();
  const theirs = await context.newPage();
  try {
    await theirs.goto("/login/otp");
    const before = lastTextId(phone);
    await theirs.getByLabel(/^Mobile number/).fill(phone);
    await theirs.getByRole("button", { name: "Send code" }).click();
    const { code } = await latestCode(phone, before);
    await theirs.getByLabel("6-digit code").fill(code);
    await theirs.waitForURL((url) => !url.pathname.includes("/login"), { timeout: 30_000 });
  } finally {
    await context.close();
  }
  expect(userRow(phone)!.status).toBe("Active");
});

test("QA-F07-05 suspending an account ends its live session at the next request and its sign-in; reactivating restores both", async () => {
  const person = await throwaway("suspend");
  const live = (await mobileLogin(person)).session!;
  const theirs = await bearer(live.accessToken);
  const id = userRow(person.phone)!.id;
  try {
    expect((await theirs.get("/api/bookings/mine")).status()).toBe(200);
    await with_("P1", async (api) => {
      expect(await codeOf(await api.post(`/api/admin/users/${id}/suspend`)), "needs a step-up").toBe("stepup_required");
      await stepUpApi(api, account("P1"));
      expect((await api.post(`/api/admin/users/${id}/suspend`)).status()).toBe(204);
      expect((await api.post("/api/admin/users/00000000-0000-0000-0000-000000000000/suspend")).status()).toBe(404);
      const self = await answer(await api.post(`/api/admin/users/${userRow(account("P1").phone)!.id}/suspend`));
      expect([self.status, self.body?.code], "own account").toEqual([403, "own_account"]);
    });
    expect(userRow(person.phone)!.status).toBe("Suspended");
    expect((await theirs.get("/api/bookings/mine")).status(), "the live token, next request").not.toBe(200);
    expect((await rotate("switch-personal", live)).status, "its refresh token").not.toBe(200);
    expect((await mobileLogin(person)).status, "sign-in").not.toBe(200);
    expect(platformRecords("UserSuspended", id)).toBe(1);

    await with_("P1", async (api) => {
      await stepUpApi(api, account("P1"));
      expect((await api.post(`/api/admin/users/${id}/reactivate`)).status()).toBe(204);
    });
    expect(userRow(person.phone)!.status).toBe("Active");
    expect((await mobileLogin(person)).status, "signs in again").toBe(200);
    expect(platformRecords("UserReactivated", id)).toBe(1);
  } finally {
    await theirs.dispose();
  }
});

test("QA-F07-06 platform roles: P1 hands out and takes back only what is junior to their own; P2 hands out nothing", async () => {
  const person = await throwaway("reviewer");
  const id = userRow(person.phone)!.id;
  await with_("P1", async (api) => {
    await stepUpApi(api, account("P1"));
    const grant = async (role: string) => answer(await api.post(`/api/admin/users/${id}/platform-roles`, { data: { role } }));
    expect(await grant("PLATFORM_ADMIN").then((r) => [r.status, r.body?.code]), "a peer's role").toEqual([403, "rank_not_junior"]);
    expect(await grant("NO_SUCH_ROLE").then((r) => [r.status, r.body?.code])).toEqual([404, "role_not_found"]);
    expect((await grant("PLATFORM_REVIEWER")).status).toBe(204);
  });
  // A reviewer now: the queue opens, and an OTP alone no longer signs them in (platform staff).
  await with_(person, async (api) => expect((await api.get("/api/admin/registrations/")).status()).toBe(200));
  await with_("P2", async (api) => {
    await stepUpApi(api, account("P2"));
    expect((await api.post(`/api/admin/users/${id}/platform-roles`, { data: { role: "PLATFORM_REVIEWER" } })).status(), "P2 grants").toBe(403);
    expect((await api.post(`/api/admin/users/${id}/suspend`)).status(), "P2 suspends").toBe(403);
    expect((await api.post("/api/admin/users", { data: { fullName: "QA x", phone: freePhone("07702"), language: "en" } })).status(), "P2 creates").toBe(403);
  });
  const reviewerSession = await signedIn(person);
  try {
    await with_("P1", async (api) => {
      await stepUpApi(api, account("P1"));
      expect((await api.delete(`/api/admin/users/${id}/platform-roles/PLATFORM_REVIEWER`)).status()).toBe(204);
    });
    expect((await reviewerSession.get("/api/admin/registrations/")).status(), "taken back: the live token loses it").toBe(403);
  } finally {
    await reviewerSession.dispose();
  }

  // Nobody suspends a peer: an administrator (made in SQL, as the e2e helpers do) is not P1's junior.
  const peer = await throwaway("peer-admin");
  grantPlatformRole(peer, "PLATFORM_ADMIN");
  await with_("P1", async (api) => {
    await stepUpApi(api, account("P1"));
    const r = await answer(await api.post(`/api/admin/users/${userRow(peer.phone)!.id}/suspend`));
    expect([r.status, r.body?.code]).toEqual([403, "rank_not_junior"]);
  });
});

test("QA-F07-07 template sync adds what an institution is missing, and nothing twice", async () => {
  await with_("P1", async (api) => {
    expect(await codeOf(await api.post(`/api/admin/institutions/${tenant("T1").slug}/sync-templates`)), "needs a step-up").toBe("stepup_required");
    await stepUpApi(api, account("P1"));
    const roles = () => one("SELECT COUNT(*) FROM auth.Roles r JOIN org.Institutions i ON i.Id = r.InstitutionId WHERE i.Slug = '$(s)';", { s: tenant("T2").slug! });
    const grants = () => one("SELECT COUNT(*) FROM auth.RolePermissions rp JOIN auth.Roles r ON r.Id = rp.RoleId JOIN org.Institutions i ON i.Id = r.InstitutionId WHERE i.Slug = '$(s)';", { s: tenant("T2").slug! });
    const [r0, g0] = [roles(), grants()];
    const first = await answer(await api.post(`/api/admin/institutions/${tenant("T2").slug}/sync-templates`));
    expect(first.status).toBe(200);
    expect(first.body, "T2 is up to date with the templates").toEqual({ modules: 0, roles: 0, grants: 0 });
    expect([roles(), grants()]).toEqual([r0, g0]);
    const second = await answer(await api.post(`/api/admin/institutions/${tenant("T2").slug}/sync-templates`));
    expect(second.body).toEqual({ modules: 0, roles: 0, grants: 0 });
    expect((await api.post("/api/admin/institutions/no-such-place/sync-templates")).status()).toBe(404);
  });
  await with_("P2", async (api) => {
    await stepUpApi(api, account("P2"));
    expect((await api.post(`/api/admin/institutions/${tenant("T2").slug}/sync-templates`)).status()).toBe(403);
  });
});

test("QA-F07-09 no one outside the platform reaches its routes", async ({ page }) => {
  const routes: [string, (api: APIRequestContext) => Promise<number>][] = [
    ["institutions", async (api) => (await api.get("/api/admin/institutions")).status()],
    ["statuses", async (api) => (await api.get("/api/admin/institutions/statuses")).status()],
    ["users", async (api) => (await api.get("/api/admin/users")).status()],
    ["support", async (api) => (await api.post(`/api/admin/institutions/${tenant("T1").slug}/support`)).status()],
    ["sync", async (api) => (await api.post(`/api/admin/institutions/${tenant("T1").slug}/sync-templates`)).status()],
    ["create user", async (api) => (await api.post("/api/admin/users", { data: {} })).status()],
  ];
  for (const key of ["O1", "M1", "D1", "O2", "A1"] as const) {
    await with_(key, async (api) => {
      for (const [what, call] of routes) expect(await call(api), `${key} ${what}`).toBe(403);
    });
  }
  const nobody = await anonymous();
  try {
    for (const [what, call] of routes) expect(await call(nobody), `anonymous ${what}`).toBe(401);
  } finally {
    await nobody.dispose();
  }
  await signIn(page, account("O1"));
  await page.goto("/si/admin/users");
  await expect(page.getByText("Imran Farook")).toHaveCount(0);
  await expect(page.getByText(account("D1").phone)).toHaveCount(0);
});
