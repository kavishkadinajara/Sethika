import { latestCode, password } from "../support/accounts";
import { sql } from "../support/sql";
import { expect, scrollsSideways, test } from "../support/test";
import { anonymous, clientAddress, codeOf, freePhone, userRow } from "./support/api";
import { e164, redis } from "./support/redis-cli";
import { account, evidence, persona } from "./support/world";

/*  F01 - Sign-up with phone OTP (P1). Cases: docs/qa/cases/F01-signup.md.
    The screens are driven where a person would use them; the rules (formats, codes, limits) are
    attacked at the API too, from addresses of their own so one case cannot spend another's budget. */

const legalHashes = async () => {
  const api = await anonymous();
  try {
    return ((await (await api.get("/api/legal/current?language=en")).json()) as { contentSha256: string }[]).map((d) => d.contentSha256);
  } finally {
    await api.dispose();
  }
};

/** A signup ticket for <phone>: code requested and verified through the API. */
async function ticketFor(phone: string, address = clientAddress()): Promise<string> {
  const api = await anonymous(address);
  try {
    expect((await api.post("/api/auth/otp/request", { data: { phoneNumber: phone, purpose: "signup", language: "en" } })).status()).toBe(202);
    const { code } = await latestCode(phone);
    const verified = await api.post("/api/auth/otp/verify", { data: { phoneNumber: phone, purpose: "signup", code } });
    expect(verified.status()).toBe(200);
    return ((await verified.json()) as { ticket: string }).ticket;
  } finally {
    await api.dispose();
  }
}

test("QA-F01-01 a first-time donor signs up on the screen and is signed in as a donor @phone", async ({ page }, testInfo) => {
  // D3 is the persona; the phone widths need numbers of their own (one person signs up once).
  const d3 = persona("D3");
  const phone = testInfo.project.name === "qa" && !userRow(d3.phone) ? d3.phone : freePhone("07703");
  const name = phone === d3.phone ? d3.name : `QA tmp signup ${testInfo.project.name}`;

  await page.goto("/signup");
  await page.getByLabel("Mobile number").fill(phone);
  await page.getByRole("button", { name: "Send code" }).click();
  await expect(page.getByText(`We sent a code to ${phone}.`)).toBeVisible();
  const { code } = await latestCode(phone);
  await page.getByLabel("6-digit code").fill(code);
  await expect(page.getByRole("heading", { name: "About you" })).toBeVisible();
  await page.getByLabel("Full name").fill(name);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Continue" }).click();
  await expect(page.getByRole("heading", { name: "Before you join" })).toBeVisible();
  for (const box of await page.getByRole("checkbox").all()) await box.check();
  expect(await scrollsSideways(page), "consent step fits the width").toBe(false);
  await evidence(page, "QA-F01-01", testInfo, "consent");
  await page.getByRole("button", { name: "Create account" }).click();
  await page.waitForURL((url) => !url.pathname.endsWith("/signup"));

  // Stored as the platform needs it: E.164, active, a donor, every current document accepted.
  const row = userRow(phone)!;
  expect(row).toMatchObject({ status: "Active", phone: e164(phone), language: "en" });
  const [roles] = sql("SELECT STRING_AGG(r.Code, ',') FROM auth.UserRoles ur JOIN auth.Roles r ON r.Id = ur.RoleId WHERE ur.UserId = '$(id)' AND ur.IsActive = 1;", { id: row.id });
  expect(roles).toBe("DONOR");
  const [consents] = sql("SELECT COUNT(*) FROM auth.UserConsents WHERE UserId = '$(id)';", { id: row.id });
  expect(Number(consents)).toBe((await legalHashes()).length);
  expect(await scrollsSideways(page)).toBe(false);
  await evidence(page, "QA-F01-01", testInfo, "signed-in");
});

test("QA-F01-02 every way a person writes a Sri Lankan mobile is accepted and stored alike; others are refused", async ({ page }) => {
  const api = await anonymous();
  try {
    const writings = [(n: string) => n, (n: string) => `${n.slice(0, 3)} ${n.slice(3, 6)} ${n.slice(6)}`, (n: string) => `${n.slice(0, 3)}-${n.slice(3, 6)}-${n.slice(6)}`, (n: string) => e164(n), (n: string) => e164(n).slice(1), (n: string) => ` ${n} `];
    for (const write of writings) {
      const phone = freePhone("07703");
      const response = await api.post("/api/auth/otp/request", { data: { phoneNumber: write(phone), purpose: "signup", language: "en" } });
      expect(response.status(), `"${write(phone)}"`).toBe(202);
      const [recipient] = sql("SELECT TOP 1 Recipient FROM notify.Notifications WHERE RIGHT(Recipient, 9) = '$(last9)' ORDER BY Id DESC;", { last9: phone.slice(1) });
      expect(recipient, `"${write(phone)}" texted as`).toBe(e164(phone));
    }
    // A diaspora donor's own mobile is accepted by design (PhoneNumberNormalizer: an explicit + wins).
    const abroad = await api.post("/api/auth/otp/request", { data: { phoneNumber: `+447911${String(Math.floor(Math.random() * 900000) + 100000)}`, purpose: "signup", language: "en" } });
    expect(abroad.status(), "a UK mobile").toBe(202);
    for (const wrong of ["", "077010012", "07701001234", "abcdefghij", "0791234567", "+94001234567"]) {
      const response = await api.post("/api/auth/otp/request", { data: { phoneNumber: wrong, purpose: "signup", language: "en" } });
      expect(response.status(), `"${wrong}"`).toBe(400);
      expect(await codeOf(response), `"${wrong}"`).toBe("invalid_phone_number");
    }
  } finally {
    await api.dispose();
  }

  // The screen says what is wrong in words, not a code.
  await page.goto("/signup");
  await page.getByLabel("Mobile number").fill("077010012");
  await page.getByRole("button", { name: "Send code" }).click();
  await expect(page.getByText("Enter a Sri Lankan mobile number, for example 077 123 4567.")).toBeVisible();
});

test("QA-F01-03 a wrong code counts down the tries; the fifth wrong one spends the code, even for the right one after", async () => {
  const phone = freePhone("07703");
  const api = await anonymous();
  try {
    expect((await api.post("/api/auth/otp/request", { data: { phoneNumber: phone, purpose: "signup", language: "en" } })).status()).toBe(202);
    const { code } = await latestCode(phone);
    const wrong = code === "000000" ? "111111" : "000000";
    const seen: (string | number | null)[] = [];
    for (let i = 0; i < 5; i++) {
      const response = await api.post("/api/auth/otp/verify", { data: { phoneNumber: phone, purpose: "signup", code: wrong } });
      expect(response.status()).toBe(400);
      const body = (await response.json()) as { code: string; attemptsRemaining?: number };
      seen.push(body.code === "otp_incorrect" ? (body.attemptsRemaining ?? null) : body.code);
    }
    expect(seen).toEqual([4, 3, 2, 1, "otp_attempts_exhausted"]);
    const right = await api.post("/api/auth/otp/verify", { data: { phoneNumber: phone, purpose: "signup", code } });
    expect(right.status()).toBe(400);
    expect(["otp_attempts_exhausted", "otp_not_found"]).toContain(await codeOf(right));
  } finally {
    await api.dispose();
  }
});

test("QA-F01-04 a code is single-use and dies after its 5 minutes", async () => {
  const api = await anonymous();
  try {
    // Reused: the right code a second time.
    const phone = freePhone("07703");
    expect((await api.post("/api/auth/otp/request", { data: { phoneNumber: phone, purpose: "signup", language: "en" } })).status()).toBe(202);
    const { code } = await latestCode(phone);
    expect((await api.post("/api/auth/otp/verify", { data: { phoneNumber: phone, purpose: "signup", code } })).status()).toBe(200);
    const again = await api.post("/api/auth/otp/verify", { data: { phoneNumber: phone, purpose: "signup", code } });
    expect([again.status(), await codeOf(again)]).toEqual([400, "otp_not_found"]);

    // Expired: the key's TTL is the code's life; one second left, then gone.
    const late = freePhone("07703");
    expect((await api.post("/api/auth/otp/request", { data: { phoneNumber: late, purpose: "signup", language: "en" } })).status()).toBe(202);
    const { code: lateCode } = await latestCode(late);
    const ttl = Number(redis("TTL", `otp:signup:${e164(late)}`));
    expect(ttl, "a fresh code lives 5 minutes").toBeGreaterThan(290);
    expect(ttl).toBeLessThanOrEqual(300);
    redis("PEXPIRE", `otp:signup:${e164(late)}`, "300");
    await new Promise((r) => setTimeout(r, 600));
    const expired = await api.post("/api/auth/otp/verify", { data: { phoneNumber: late, purpose: "signup", code: lateCode } });
    expect([expired.status(), await codeOf(expired)]).toEqual([400, "otp_not_found"]);

    // A code for another purpose is not a signup code.
    const other = freePhone("07703");
    expect((await api.post("/api/auth/otp/request", { data: { phoneNumber: other, purpose: "signup", language: "en" } })).status()).toBe(202);
    const { code: signupCode } = await latestCode(other);
    const asReset = await api.post("/api/auth/otp/verify", { data: { phoneNumber: other, purpose: "reset", code: signupCode } });
    expect(asReset.status()).toBe(400);
  } finally {
    await api.dispose();
  }
});

test("QA-F01-05 a new code waits out the 60-second cooldown, on the API and on the screen", async ({ page }) => {
  const phone = freePhone("07703");
  const api = await anonymous();
  try {
    expect((await api.post("/api/auth/otp/request", { data: { phoneNumber: phone, purpose: "signup", language: "en" } })).status()).toBe(202);
    const again = await api.post("/api/auth/otp/request", { data: { phoneNumber: phone, purpose: "signup", language: "en" } });
    expect(again.status()).toBe(429);
    const wait = Number(again.headers()["retry-after"]);
    expect(wait).toBeGreaterThan(50);
    expect(wait).toBeLessThanOrEqual(60);
    const [texts] = sql("SELECT COUNT(*) FROM notify.Notifications WHERE RIGHT(Recipient, 9) = '$(last9)';", { last9: phone.slice(1) });
    expect(texts, "the refused request sent nothing").toBe("1");

    // Boundary: one second before the cooldown ends it is still refused; at its end a new code goes.
    redis("EXPIRE", `otp:signup:${e164(phone)}`, "241");
    expect((await api.post("/api/auth/otp/request", { data: { phoneNumber: phone, purpose: "signup", language: "en" } })).status()).toBe(429);
    redis("EXPIRE", `otp:signup:${e164(phone)}`, "240");
    expect((await api.post("/api/auth/otp/request", { data: { phoneNumber: phone, purpose: "signup", language: "en" } })).status()).toBe(202);
  } finally {
    await api.dispose();
  }

  const onScreen = freePhone("07703");
  await page.goto("/signup");
  await page.getByLabel("Mobile number").fill(onScreen);
  await page.getByRole("button", { name: "Send code" }).click();
  await expect(page.getByText(/New code in \d/)).toBeVisible();
  await expect(page.getByRole("button", { name: "Send a new code" })).toBeHidden();
});

test("QA-F01-06 a number gets at most the window's sends (10 an hour in Development), then a wait", async () => {
  const phone = freePhone("07703");
  const api = await anonymous();
  try {
    // Boundary at the cap: 9 sent already -> the 10th goes; 10 sent -> the 11th is refused.
    redis("SET", `otp:sends:signup:${e164(phone)}`, "9", "EX", "3600");
    expect((await api.post("/api/auth/otp/request", { data: { phoneNumber: phone, purpose: "signup", language: "en" } })).status()).toBe(202);
    redis("EXPIRE", `otp:signup:${e164(phone)}`, "240");
    const capped = await api.post("/api/auth/otp/request", { data: { phoneNumber: phone, purpose: "signup", language: "en" } });
    expect(capped.status()).toBe(429);
    expect(Number(capped.headers()["retry-after"])).toBeGreaterThan(60);
  } finally {
    await api.dispose();
  }
});

test("QA-F01-07 a number that already has an account cannot make a second one, and the screen says so", async ({ page }, testInfo) => {
  const d1 = account("D1");
  const ticket = await ticketFor(d1.phone);
  const api = await anonymous();
  try {
    const response = await api.post("/api/auth/signup", {
      data: { phoneNumber: d1.phone, verificationTicket: ticket, fullName: "Someone Else", email: null, password, preferredLanguage: "en", acceptedDocumentHashes: await legalHashes() },
    });
    expect([response.status(), await codeOf(response)]).toEqual([409, "already_registered"]);
    const [count] = sql("SELECT COUNT(*) FROM auth.Users WHERE RIGHT(PhoneNumber, 9) = '$(last9)';", { last9: d1.phone.slice(1) });
    expect(count).toBe("1");
    expect(userRow(d1.phone)!.status).toBe("Active");
  } finally {
    await api.dispose();
  }

  // On the screen, written the international way.
  await page.goto("/signup");
  await page.getByLabel("Mobile number").fill(e164(d1.phone));
  await page.getByRole("button", { name: "Send code" }).click();
  redis("EXPIRE", `otp:signup:${e164(d1.phone)}`, "240"); // the code above is still cooling down
  const sent = page.getByText(`We sent a code to ${e164(d1.phone)}.`);
  if (!(await sent.isVisible())) {
    await page.getByRole("button", { name: "Send code" }).click();
  }
  await expect(sent).toBeVisible();
  const { code } = await latestCode(d1.phone);
  await page.getByLabel("6-digit code").fill(code);
  await page.getByLabel("Full name").fill("Someone Else");
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Continue" }).click();
  await expect(page.getByRole("heading", { name: "Before you join" })).toBeVisible();
  for (const box of await page.getByRole("checkbox").all()) await box.check();
  await page.getByRole("button", { name: "Create account" }).click();
  await expect(page.getByText("An account already uses this phone number or email.")).toBeVisible();
  await evidence(page, "QA-F01-07", testInfo);
});

test("QA-F01-08 a verification ticket works once, for its own number only", async () => {
  const phone = freePhone("07703");
  const other = freePhone("07703");
  const ticket = await ticketFor(phone);
  const hashes = await legalHashes();
  const api = await anonymous();
  try {
    const body = (n: string) => ({ phoneNumber: n, verificationTicket: ticket, fullName: "QA tmp ticket", email: null, password, preferredLanguage: "en", acceptedDocumentHashes: hashes });
    const stolen = await api.post("/api/auth/signup", { data: body(other) });
    expect([stolen.status(), await codeOf(stolen)]).toEqual([400, "phone_not_verified"]);
    expect(userRow(other)).toBeNull();

    const first = await api.post("/api/auth/signup", { data: body(phone) });
    expect(first.status()).toBe(201);
    const replay = await api.post("/api/auth/signup", { data: body(phone) });
    expect(replay.status()).toBe(400);
    expect(await codeOf(replay)).toBe("phone_not_verified");
  } finally {
    await api.dispose();
  }
});

test("QA-F01-09 sign-up needs every current document accepted and a password the policy takes", async () => {
  const hashes = await legalHashes();
  const api = await anonymous();
  try {
    const attempt = async (overrides: Record<string, unknown>) => {
      const phone = freePhone("07703");
      const ticket = await ticketFor(phone);
      const response = await api.post("/api/auth/signup", {
        data: { phoneNumber: phone, verificationTicket: ticket, fullName: "QA tmp policy", email: null, password, preferredLanguage: "en", acceptedDocumentHashes: hashes, ...overrides },
      });
      return { phone, status: response.status(), body: (await response.json().catch(() => null)) as { code?: string; failures?: string[] } | null };
    };

    const none = await attempt({ acceptedDocumentHashes: [] });
    expect([none.status, none.body?.code]).toEqual([400, "consent_missing"]);
    const one = await attempt({ acceptedDocumentHashes: hashes.slice(0, 1) });
    expect([one.status, one.body?.code]).toEqual([400, "consent_missing"]);
    const stale = await attempt({ acceptedDocumentHashes: hashes.map(() => "0".repeat(64)) });
    expect([stale.status, stale.body?.code]).toEqual([400, "consent_missing"]);

    const weak = await attempt({ password: "short" });
    expect([weak.status, weak.body?.code]).toEqual([400, "password_rejected"]);
    expect(weak.body?.failures).toEqual(expect.arrayContaining(["TooShort"]));
    const personal = await attempt({ password: "QAtmppolicy1!", fullName: "QAtmppolicy" });
    expect(personal.body?.code, "a password made of the name").toBe("password_rejected");
    const name = await attempt({ fullName: "x".repeat(151) });
    expect([name.status, name.body?.code]).toEqual([400, "invalid_full_name"]);
    for (const refused of [none, one, stale, weak, personal, name]) expect(userRow(refused.phone), refused.phone).toBeNull();
  } finally {
    await api.dispose();
  }
});

test("QA-F01-10 one client gets 20 sign-up codes an hour; the 21st is refused and texts nobody", async () => {
  const address = clientAddress();
  const api = await anonymous(address);
  try {
    const phones = Array.from({ length: 21 }, () => freePhone("07703"));
    const statuses: number[] = [];
    for (const phone of phones) statuses.push((await api.post("/api/auth/otp/request", { data: { phoneNumber: phone, purpose: "signup", language: "en" } })).status());
    expect(statuses.slice(0, 20).every((s) => s === 202), `first 20: ${statuses.slice(0, 20)}`).toBe(true);
    expect(statuses[20]).toBe(429);
    const [texts] = sql("SELECT COUNT(*) FROM notify.Notifications WHERE RIGHT(Recipient, 9) = '$(last9)';", { last9: phones[20].slice(1) });
    expect(texts).toBe("0");
  } finally {
    await api.dispose();
  }
});

test("QA-F01-11 a Sinhala sign-up gets its code in Sinhala and keeps Sinhala as its language @phone", async ({ page }, testInfo) => {
  const phone = freePhone("07703");
  await page.goto("/si/signup");
  await page.locator('main form input[type="tel"], main form input[inputmode="tel"], main form input:not([type="hidden"])').first().fill(phone);
  await page.locator('main form button[type="submit"]').click();
  const { code } = await latestCode(phone);
  // As UTF-16 bytes: sqlcmd's console output would turn Sinhala letters into "?".
  const [hex] = sql("SELECT TOP 1 CONVERT(varchar(max), CAST(Body AS varbinary(max)), 2) FROM notify.Notifications WHERE RIGHT(Recipient, 9) = '$(last9)' ORDER BY Id DESC;", { last9: phone.slice(1) });
  const body = Buffer.from(hex, "hex").toString("utf16le");
  expect(body, "the text is in Sinhala script").toMatch(/[඀-෿]/);
  expect(body).toContain(code);
  expect(await scrollsSideways(page)).toBe(false);
  await evidence(page, "QA-F01-11", testInfo);
});

test("QA-F01-12 a Sri Lankan landline is refused: it can never receive the code", async () => {
  const api = await anonymous();
  try {
    for (const landline of ["0112345678", "0812234567", "+94 11 234 5678"]) {
      const last9 = landline.replace(/\D/g, "").slice(-9);
      redis("DEL", `otp:signup:+94${last9}`); // an earlier run's code would answer 429 first
      const response = await api.post("/api/auth/otp/request", { data: { phoneNumber: landline, purpose: "signup", language: "en" } });
      expect([response.status(), await codeOf(response)], `"${landline}"`).toEqual([400, "invalid_phone_number"]);
    }
  } finally {
    await api.dispose();
  }
});
