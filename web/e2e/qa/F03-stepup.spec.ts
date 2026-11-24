import type { APIRequestContext } from "@playwright/test";
import si from "../../messages/si.json";
import { lastTextId, latestCode, signIn, stepUp } from "../support/accounts";
import { sql } from "../support/sql";
import { expect, scrollsSideways, test } from "../support/test";
import { bearer, codeOf, freshStepUp, mobileLogin, signedIn, stepUpApi, throwaway, userRow } from "./support/api";
import { e164, redis } from "./support/redis-cli";
import { account, evidence, persona, type PersonaKey } from "./support/world";

/*  F03 - Step-up verification for RequiresMfa permissions (P1). Cases: docs/qa/cases/F03-stepup.md.
    Every call aims at something that does not exist (REQ-2026-999999, a zero GUID, no-such-slug),
    so an authorisation that wrongly let one through would still change nothing - and the answer
    after a step-up (404 / 400 instead of 403) proves the request got past the check.          */

type Call = { permission: string; actor: PersonaKey; method: "post" | "put"; path: string; data?: unknown };

const zero = "00000000-0000-0000-0000-000000000000";
const mfaCalls: Call[] = [
  { permission: "ACCESS.MEMBERS.EDIT", actor: "O1", method: "post", path: `/api/institution/invitations/${zero}/cancel` },
  { permission: "ACCESS.ROLES.EDIT", actor: "O1", method: "put", path: "/api/institution/roles/NO_SUCH_ROLE/permissions", data: { permissions: [], version: null } },
  { permission: "GIVING.DONATIONS.DELETE", actor: "O1", method: "post", path: "/api/institution/donations/RCP-2026-999999/void", data: { reason: "QA" } },
  { permission: "GIVING.PAYMENTS.EDIT", actor: "O1", method: "put", path: "/api/institution/payments/merchant", data: {} },
  { permission: "ONBOARDING.REQUESTS.APPROVE", actor: "P1", method: "post", path: "/api/admin/registrations/REQ-2026-999999/approve" },
  { permission: "ONBOARDING.REQUESTS.REJECT", actor: "P1", method: "post", path: "/api/admin/registrations/REQ-2026-999999/reject", data: { reason: "OTHER", note: "QA" } },
  { permission: "PLATFORM.INSTITUTIONS.EDIT", actor: "P1", method: "post", path: "/api/admin/institutions/no-such-slug/sync-templates" },
  { permission: "PLATFORM.INSTITUTIONS.EXECUTE", actor: "P1", method: "post", path: "/api/admin/institutions/no-such-slug/support" },
  { permission: "PLATFORM.USERS.CREATE", actor: "P1", method: "post", path: "/api/admin/users", data: {} },
  { permission: "PLATFORM.USERS.EDIT", actor: "P1", method: "post", path: `/api/admin/users/${zero}/suspend` },
];

const send = (api: APIRequestContext, call: Call) => (call.method === "post" ? api.post(call.path, { data: call.data }) : api.put(call.path, { data: call.data }));

test("QA-F03-01 every RequiresMfa permission asks for a step-up, and passes once it is given", async () => {
  // The list is the database's, not this file's: a new MFA permission without a case fails here.
  const fromDb = sql("SELECT Code FROM auth.Permissions WHERE RequiresMfa = 1 ORDER BY Code;");
  expect(fromDb).toEqual(mfaCalls.map((c) => c.permission).sort());

  for (const actor of ["O1", "P1"] as const) {
    const api = await signedIn(account(actor));
    try {
      const calls = mfaCalls.filter((c) => c.actor === actor);
      for (const call of calls) {
        const response = await send(api, call);
        expect([response.status(), await codeOf(response)], `${call.permission} before`).toEqual([403, "stepup_required"]);
      }
      await stepUpApi(api, account(actor));
      for (const call of calls) {
        const response = await send(api, call);
        expect(await codeOf(response), `${call.permission} after: ${response.status()}`).not.toBe("stepup_required");
        expect(response.status(), `${call.permission} after`).not.toBe(403);
      }
      // Control: a permission without RequiresMfa never asks.
      const roles = await api.get(actor === "O1" ? "/api/institution/roles" : "/api/admin/users");
      expect(roles.status()).toBe(200);
    } finally {
      await api.dispose();
    }
  }
});

test("QA-F03-02 decision table: no permission or a deny override is a plain refusal, never an offer to step up", async () => {
  const rows: { who: PersonaKey; call: Call }[] = [
    { who: "S1", call: mfaCalls[0] }, // staff: no ACCESS.MEMBERS.EDIT
    { who: "C1", call: mfaCalls[2] }, // treasurer: role grants DELETE, the override denies it
    { who: "P2", call: mfaCalls[4] }, // reviewer: no APPROVE
    { who: "O2", call: mfaCalls[2] }, // another tenant's owner: holds DELETE, but for T2 - nothing of T1 is reachable
  ];
  for (const { who, call } of rows) {
    const api = await signedIn(account(who));
    try {
      const before = await send(api, call);
      if (who === "O2") {
        // O2 holds the permission in T2, so the step-up question is fair; the receipt is not T2's anyway.
        expect(before.status()).toBe(403);
        continue;
      }
      expect([before.status(), await codeOf(before)], `${who} ${call.permission}`).toEqual([403, null]);
      // Even after a step-up the refusal stands: step-up proves who you are, not what you may do.
      await stepUpApi(api, account(who));
      const after = await send(api, call);
      expect(after.status(), `${who} ${call.permission} after step-up`).toBe(403);
    } finally {
      await api.dispose();
    }
  }
});

test("QA-F03-03 the owner's invitation asks for the texted code on screen; cancelling it changes nothing @phone", async ({ page }, testInfo) => {
  const o1 = account("O1");
  const invitee = await throwaway("invitee");
  freshStepUp(o1.phone); // past earlier cases' cooldown and hourly cap (QA-F03-08 tests the cooldown)
  const pending = () => Number(sql("SELECT COUNT(*) FROM org.StaffInvitations WHERE Status = 'Pending' AND RIGHT(InviteePhone, 9) = '$(last9)';", { last9: invitee.phone.slice(1) })[0]);
  await signIn(page, o1);
  // O1 works in Sinhala: signing in takes the app to /si, so the screen is driven by its Sinhala labels.
  await page.goto("/si/access/members");
  await page.getByRole("button", { name: si.Access.members.invite }).click();
  const invite = page.getByRole("dialog");
  await invite.getByLabel(new RegExp(`^${si.Access.invite.phone}`)).fill(invitee.phone);
  await invite.getByLabel(new RegExp(`^${si.Access.invite.role}`)).selectOption("STAFF");
  const firstCode = lastTextId(o1.phone);
  await invite.getByRole("button", { name: si.Access.invite.send }).click();

  // First time: cancel the check. Nothing is sent, and the screen says why.
  const check = page.getByRole("dialog").filter({ has: page.getByRole("heading", { name: si.StepUp.title }) });
  await expect(check).toBeVisible();
  expect(await scrollsSideways(page)).toBe(false);
  await evidence(page, "QA-F03-03", testInfo, "dialog");
  await check.getByRole("button", { name: si.StepUp.cancel }).click();
  await expect(page.getByText(si.Errors.stepup_required).first()).toBeVisible();
  expect(pending(), "cancelled: no invitation").toBe(0);

  // Second time, inside the minute: no new code may be sent (cooldown), and the first one still works.
  await invite.getByRole("button", { name: si.Access.invite.send }).click();
  await stepUp(page, o1, firstCode, "si");
  await expect.poll(pending, { message: "the invitation after the code" }).toBe(1);
  await evidence(page, "QA-F03-03", testInfo, "sent");
});

test("QA-F03-04 a step-up code dies after 5 minutes and after 5 wrong tries", async () => {
  const p1 = account("P1");
  const api = await signedIn(p1);
  try {
    freshStepUp(p1.phone);
    let before = lastTextId(p1.phone);
    expect((await api.post("/api/auth/stepup/request")).status()).toBe(202);
    const { code } = await latestCode(p1.phone, before);
    redis("PEXPIRE", `otp:stepup:${e164(p1.phone)}`, "200");
    await new Promise((r) => setTimeout(r, 500));
    const late = await api.post("/api/auth/stepup/verify", { data: { code } });
    expect([late.status(), await codeOf(late)]).toEqual([400, "otp_not_found"]);

    before = lastTextId(p1.phone);
    expect((await api.post("/api/auth/stepup/request")).status()).toBe(202);
    const { code: fresh } = await latestCode(p1.phone, before);
    const wrong = fresh === "000000" ? "111111" : "000000";
    const answers: (string | null)[] = [];
    for (let i = 0; i < 5; i++) answers.push(await codeOf(await api.post("/api/auth/stepup/verify", { data: { code: wrong } })));
    expect(answers).toEqual(["otp_incorrect", "otp_incorrect", "otp_incorrect", "otp_incorrect", "otp_attempts_exhausted"]);
    const right = await api.post("/api/auth/stepup/verify", { data: { code: fresh } });
    expect(right.status()).toBe(400);
    const approve = await api.post("/api/admin/registrations/REQ-2026-999999/approve");
    expect(await codeOf(approve), "still not stepped up").toBe("stepup_required");
  } finally {
    await api.dispose();
  }
});

test("QA-F03-05 a step-up belongs to one person and one token: not to another user, not to another session", async () => {
  const p1 = account("P1");
  const o1 = account("O1");
  const admin = await signedIn(p1);
  const owner = await signedIn(o1);
  try {
    // P1's code typed into O1's session.
    freshStepUp(p1.phone);
    freshStepUp(o1.phone);
    const before = lastTextId(p1.phone);
    expect((await admin.post("/api/auth/stepup/request")).status()).toBe(202);
    const { code } = await latestCode(p1.phone, before);
    const borrowed = await owner.post("/api/auth/stepup/verify", { data: { code } });
    expect(borrowed.status(), "someone else's code").toBe(400);
    expect(await codeOf(await owner.post(`/api/institution/invitations/${zero}/cancel`))).toBe("stepup_required");

    // P1 steps up on one token; a second session of P1 is still asked.
    expect((await admin.post("/api/auth/stepup/verify", { data: { code } })).status()).toBe(204);
    expect(await codeOf(await admin.post("/api/admin/registrations/REQ-2026-999999/approve"))).not.toBe("stepup_required");
    const second = await signedIn(p1);
    try {
      expect(await codeOf(await second.post("/api/admin/registrations/REQ-2026-999999/approve")), "another session of the same person").toBe("stepup_required");
    } finally {
      await second.dispose();
    }
  } finally {
    await admin.dispose();
    await owner.dispose();
  }
});

test("QA-F03-06 a step-up lasts 15 minutes, then the next sensitive action asks again", async () => {
  const p1 = account("P1");
  const { session } = await mobileLogin(p1);
  const api = await bearer(session!.accessToken);
  try {
    await stepUpApi(api, p1);
    const jti = (JSON.parse(Buffer.from(session!.accessToken.split(".")[1], "base64url").toString("utf8")) as { jti: string }).jti;
    const key = `stepup:${userRow(p1.phone)!.id}:${jti}`;
    const ttl = Number(redis("TTL", key));
    expect(ttl, "grant lifetime").toBeGreaterThan(14 * 60);
    expect(ttl).toBeLessThanOrEqual(15 * 60);
    expect(await codeOf(await api.post("/api/admin/registrations/REQ-2026-999999/approve"))).not.toBe("stepup_required");
    redis("PEXPIRE", key, "200");
    await new Promise((r) => setTimeout(r, 500));
    expect(await codeOf(await api.post("/api/admin/registrations/REQ-2026-999999/approve")), "after the grant expired").toBe("stepup_required");
  } finally {
    await api.dispose();
  }
});

test("QA-F03-07 a step-up code is texted to the account's own phone in the account's language", async () => {
  const o1 = persona("O1");
  const api = await signedIn(account("O1"));
  try {
    freshStepUp(o1.phone);
    const before = lastTextId(o1.phone);
    expect((await api.post("/api/auth/stepup/request")).status()).toBe(202);
    await latestCode(o1.phone, before);
    const [row] = sql("SELECT TOP 1 CONCAT(Recipient, '|', CONVERT(varchar(max), CAST(Body AS varbinary(max)), 2)) FROM notify.Notifications WHERE Id > $(id) AND Channel = 'SMS' ORDER BY Id DESC;", { id: String(before) });
    const [recipient, hex] = row.split("|");
    expect(recipient).toBe(e164(o1.phone));
    expect(Buffer.from(hex, "hex").toString("utf16le"), "O1's language is si").toMatch(/[඀-෿]/);
  } finally {
    await api.dispose();
  }
});

test("QA-F03-08 asked again within the minute, the check says the code already sent still works - not 'too many tries'", async ({ page }, testInfo) => {
  const o1 = account("O1");
  freshStepUp(o1.phone);
  await signIn(page, o1);
  await page.goto("/si/access/members");
  await page.getByRole("button", { name: si.Access.members.invite }).click();
  const invite = page.getByRole("dialog");
  const invitee = await throwaway("invitee2");
  const pending = () => Number(sql("SELECT COUNT(*) FROM org.StaffInvitations WHERE Status = 'Pending' AND RIGHT(InviteePhone, 9) = '$(last9)';", { last9: invitee.phone.slice(1) })[0]);
  await invite.getByLabel(new RegExp(`^${si.Access.invite.phone}`)).fill(invitee.phone);
  await invite.getByLabel(new RegExp(`^${si.Access.invite.role}`)).selectOption("STAFF");
  const firstCode = lastTextId(o1.phone);
  await invite.getByRole("button", { name: si.Access.invite.send }).click();
  const check = page.getByRole("dialog").filter({ has: page.getByRole("heading", { name: si.StepUp.title }) });
  await expect(check.getByText(si.StepUp.sent)).toBeVisible();
  await expect.poll(() => lastTextId(o1.phone), { message: "the first code is texted" }).toBeGreaterThan(firstCode);
  const afterFirst = lastTextId(o1.phone);
  await check.getByRole("button", { name: si.StepUp.cancel }).click();
  await invite.getByRole("button", { name: si.Access.invite.send }).click();
  await expect(check).toBeVisible();
  await page.waitForTimeout(1500);
  await evidence(page, "QA-F03-08", testInfo);
  // DEF-003 (fixed): it was a red "too many tries, wait 1:00" beside "code sent" and a 0:4x countdown.
  const tooMany = si.Errors.tooManyRequests.split("{time}")[0].trim();
  await expect(check.getByText(tooMany, { exact: false }), "the cooldown is shown as an error").toHaveCount(0);
  await expect(check.getByText(si.StepUp.sent)).toBeVisible();
  await expect(check.getByText(si.StepUp.resendIn.split("{time}")[1].trim(), { exact: false }), "a countdown to a new code").toBeVisible();
  expect(lastTextId(o1.phone), "no second code was texted").toBe(afterFirst);
  // The code texted at the first opening still finishes the invitation.
  await stepUp(page, o1, firstCode, "si");
  await expect.poll(pending, { message: "the invitation after the earlier code" }).toBe(1);
});
