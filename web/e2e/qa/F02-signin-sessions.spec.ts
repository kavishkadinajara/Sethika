import { lastTextId, latestCode, password, signIn } from "../support/accounts";
import { field } from "../support/fields";
import { sql } from "../support/sql";
import { expect, scrollsSideways, test } from "../support/test";
import { anonymous, bearer, codeOf, freePhone, mobileLogin, signedIn, stepUpApi, throwaway, userRow, type MobileSession } from "./support/api";
import { e164, redis } from "./support/redis-cli";
import { account, evidence } from "./support/world";

/*  F02 - Sign-in, OTP sign-in, forgot / change password, sessions (P1). Cases: docs/qa/cases/F02-signin-sessions.md.
    Cases that lock, reset or end sessions use a throwaway account, so the personas stay usable
    for the features after this one; D4's suspension is undone at the end of its case.       */

async function refresh(token: string): Promise<{ status: number; code: string | null; session: MobileSession | null }> {
  const api = await anonymous();
  try {
    const response = await api.post("/api/auth/mobile/session/refresh", { data: { refreshToken: token } });
    return { status: response.status(), code: response.ok() ? null : await codeOf(response), session: response.ok() ? ((await response.json()) as MobileSession) : null };
  } finally {
    await api.dispose();
  }
}

async function mineStatus(accessToken: string): Promise<number> {
  const api = await bearer(accessToken);
  try {
    return (await api.get("/api/bookings/mine")).status();
  } finally {
    await api.dispose();
  }
}

const strong = "Qa-Second-Pass-7";

/** Seconds an access token has left: by design (JwtOptions) the longest a revoked session keeps working. */
function secondsLeft(accessToken: string): number {
  const claims = JSON.parse(Buffer.from(accessToken.split(".")[1], "base64url").toString("utf8")) as { exp: number };
  return claims.exp - Math.floor(Date.now() / 1000);
}

test("QA-F02-01 a donor signs in with a password, however the number is written @phone", async ({ page }, testInfo) => {
  const d1 = account("D1");
  for (const written of [d1.phone, e164(d1.phone), `${d1.phone.slice(0, 3)} ${d1.phone.slice(3, 6)} ${d1.phone.slice(6)}`]) {
    const { status } = await mobileLogin({ ...d1, phone: written });
    expect(status, `"${written}"`).toBe(200);
  }
  await signIn(page, d1);
  await expect(page.locator('input[type="password"]')).toHaveCount(0);
  const [history] = sql("SELECT TOP 1 CONCAT(Succeeded, '|', ISNULL(FailureReason, '')) FROM auth.LoginHistory WHERE UserId = '$(id)' ORDER BY Id DESC;", { id: userRow(d1.phone)!.id });
  expect(history).toBe("1|");
  expect(await scrollsSideways(page)).toBe(false);
  await evidence(page, "QA-F02-01", testInfo);
});

test("QA-F02-02 five wrong passwords lock the account for 15 minutes, and the answer never says so", async ({ page }) => {
  const person = await throwaway("lockout");
  const answers: string[] = [];
  for (let i = 0; i < 5; i++) {
    const { status, body } = await mobileLogin(person, "Wrong-Pass-1");
    answers.push(`${status} ${JSON.parse(body).code}`);
  }
  expect(answers).toEqual(Array(5).fill("401 invalid_credentials"));
  // The right password now gets the very same answer as a wrong one (no account confirmation).
  const locked = await mobileLogin(person);
  expect(`${locked.status} ${JSON.parse(locked.body).code}`).toBe("401 invalid_credentials");
  const [minutes] = sql("SELECT DATEDIFF(SECOND, SYSUTCDATETIME(), LockoutEnd) FROM auth.Users WHERE RIGHT(PhoneNumber, 9) = '$(last9)';", { last9: person.phone.slice(1) });
  expect(Number(minutes)).toBeGreaterThan(14 * 60);
  expect(Number(minutes)).toBeLessThanOrEqual(15 * 60);

  await page.goto("/login");
  await page.locator('main form input:not([type="password"]):not([type="hidden"])').first().fill(person.phone);
  await page.locator('input[type="password"]').fill(password);
  await page.locator('button[type="submit"]').click();
  await expect(page.getByText("That phone number, email or password is not right.")).toBeVisible();

  // An unknown number answers exactly like a locked or wrong one.
  const nobody = await mobileLogin({ phone: freePhone("07703"), fullName: "nobody" });
  expect(`${nobody.status} ${JSON.parse(nobody.body).code}`).toBe("401 invalid_credentials");
});

test("QA-F02-03 a donor signs in with a texted code; an unknown number is told the same and texted nothing", async ({ page }, testInfo) => {
  const d1 = account("D1");
  await page.goto("/login/otp");
  await page.getByLabel("Mobile number").fill(d1.phone);
  const before = Number(sql("SELECT ISNULL(MAX(Id), 0) FROM notify.Notifications;")[0]);
  await page.getByRole("button", { name: "Send code" }).click();
  await expect(page.getByText(`If ${d1.phone} is on a Sethika account, a code is on its way.`, { exact: false })).toBeVisible();
  const { code } = await latestCode(d1.phone, before);
  await page.getByLabel("6-digit code").fill(code);
  await page.waitForURL((url) => !url.pathname.startsWith("/login"));
  await evidence(page, "QA-F02-03", testInfo);

  const stranger = freePhone("07703");
  await page.context().clearCookies();
  await page.goto("/login/otp");
  await page.getByLabel("Mobile number").fill(stranger);
  await page.getByRole("button", { name: "Send code" }).click();
  await expect(page.getByText(`If ${stranger} is on a Sethika account, a code is on its way.`, { exact: false })).toBeVisible();
  await page.waitForTimeout(2000);
  const [texts] = sql("SELECT COUNT(*) FROM notify.Notifications WHERE RIGHT(Recipient, 9) = '$(last9)';", { last9: stranger.slice(1) });
  expect(texts).toBe("0");
});

test("QA-F02-04 forgot password: a texted code sets a new one, every session ends, the old password and an old one again are refused", async ({ page }, testInfo) => {
  const person = await throwaway("reset");
  const other = (await mobileLogin(person)).session!;

  await page.goto("/forgot-password");
  await page.getByLabel("Mobile number").fill(person.phone);
  await page.getByRole("button", { name: "Send code" }).click();
  const { code } = await latestCode(person.phone);
  await page.getByLabel("6-digit code").fill(code);
  await expect(field(page, "New password")).toBeVisible();
  await field(page, "New password").fill(strong);
  await field(page, "Type the new password again").fill(`${strong}x`);
  await page.getByRole("button", { name: "Change password" }).click();
  await expect(page.getByText("The two passwords don't match.")).toBeVisible();
  await field(page, "Type the new password again").fill(strong);
  await page.getByRole("button", { name: "Change password" }).click();
  await expect(page.getByText("Your password was changed").first()).toBeVisible();
  await evidence(page, "QA-F02-04", testInfo);

  expect((await mobileLogin(person)).status, "the old password").toBe(401);
  expect((await mobileLogin(person, strong)).status, "the new password").toBe(200);
  expect((await refresh(other.refreshToken)).status, "a session from before the reset").toBe(401);
  // OBS-01: the stamp is checked on refresh, so the other device's access token lives out its <= 15 minutes.
  expect(secondsLeft(other.accessToken)).toBeLessThanOrEqual(15 * 60);
  test.info().annotations.push({ type: "OBS-01", description: `live access token after reset: ${await mineStatus(other.accessToken)}` });

  // Resetting back to the password used before is refused (recently used).
  const api = await anonymous();
  try {
    const before = lastTextId(person.phone);
    redis("DEL", `otp:reset:${e164(person.phone)}`); // the first reset code's cooldown
    expect((await api.post("/api/auth/otp/request", { data: { phoneNumber: person.phone, purpose: "reset", language: "en" } })).status()).toBe(202);
    const { code: again } = await latestCode(person.phone, before);
    const verified = await api.post("/api/auth/otp/verify", { data: { phoneNumber: person.phone, purpose: "reset", code: again } });
    const { ticket } = (await verified.json()) as { ticket: string };
    const back = await api.post("/api/auth/password/reset", { data: { ticket, newPassword: password } });
    expect(back.status()).toBe(400);
    expect(JSON.stringify(await back.json())).toContain("RecentlyUsed");
  } finally {
    await api.dispose();
  }
});

test("QA-F02-05 change password: the current one is checked, this browser stays signed in, other devices are signed out @phone", async ({ page }, testInfo) => {
  const person = await throwaway("change");
  const other = (await mobileLogin(person)).session!;
  await signIn(page, person);
  await page.goto("/account/password");
  await field(page, "Current password").fill("Not-My-Pass-1");
  await field(page, "New password").fill(strong);
  await field(page, "Confirm new password").fill(strong);
  await page.getByRole("button", { name: "Change password" }).click();
  await expect(page.getByText("That isn't your current password.")).toBeVisible();

  await field(page, "Current password").fill(password);
  await field(page, "New password").fill(password);
  await field(page, "Confirm new password").fill(password);
  await page.getByRole("button", { name: "Change password" }).click();
  await expect(page.getByText("Choose a password that is different from your current one.")).toBeVisible();

  await field(page, "New password").fill(strong);
  await field(page, "Confirm new password").fill(strong);
  await page.getByRole("button", { name: "Change password" }).click();
  await expect(page.getByText("Your other devices have been signed out.").first()).toBeVisible();
  expect(await scrollsSideways(page)).toBe(false);
  await evidence(page, "QA-F02-05", testInfo);

  expect((await refresh(other.refreshToken)).status, "the other device").toBe(401);
  await page.goto("/bookings/mine");
  await expect(page).not.toHaveURL(/\/login/);
  expect((await mobileLogin(person, strong)).status).toBe(200);
});

test("QA-F02-06 a refresh token rotates; a copy used again after the 10-second grace kills the whole family", async () => {
  const person = await throwaway("rotation");
  const first = (await mobileLogin(person)).session!;
  const second = await refresh(first.refreshToken);
  expect(second.status).toBe(200);
  expect(second.session!.refreshToken).not.toBe(first.refreshToken);

  // Inside the grace a second tab's copy is "superseded", not an attack.
  expect((await refresh(first.refreshToken)).code).toBe("session_superseded");

  await new Promise((r) => setTimeout(r, 11_000));
  const replay = await refresh(first.refreshToken);
  expect([replay.status, replay.code]).toEqual([401, "session_expired"]);
  const successor = await refresh(second.session!.refreshToken);
  expect(successor.status, "the legitimate successor dies with its family").toBe(401);
  const [reasons] = sql(
    "SELECT STRING_AGG(RevokedReason, ',') WITHIN GROUP (ORDER BY Id) FROM auth.RefreshTokens WHERE UserId = '$(id)';",
    { id: userRow(person.phone)!.id },
  );
  expect(reasons).toBe("Rotated,ReuseDetected");
});

test("QA-F02-07 two tabs refreshing the same session at once both keep the person signed in", async () => {
  const person = await throwaway("tabs");
  const start = (await mobileLogin(person)).session!;
  const [a, b] = await Promise.all([refresh(start.refreshToken), refresh(start.refreshToken)]);
  const outcomes = [a.status, b.status].sort();
  expect(outcomes, `${a.code} / ${b.code}`).toEqual([200, 409]);
  const winner = a.status === 200 ? a : b;
  expect((await refresh(winner.session!.refreshToken)).status, "the surviving session").toBe(200);
});

test("QA-F02-08 sign out ends one device; sign out on all devices ends every one, from the menu @phone", async ({ page }, testInfo) => {
  const person = await throwaway("logout");
  const phoneA = (await mobileLogin(person)).session!;
  const phoneB = (await mobileLogin(person)).session!;
  const api = await anonymous();
  try {
    expect((await api.post("/api/auth/mobile/session/logout", { data: { refreshToken: phoneA.refreshToken } })).status()).toBe(204);
  } finally {
    await api.dispose();
  }
  expect((await refresh(phoneA.refreshToken)).status, "signed-out device").toBe(401);
  const stillB = await refresh(phoneB.refreshToken);
  expect(stillB.status, "the other device").toBe(200);

  await signIn(page, person);
  // The account menu is in the top bar at every width (the phone's menu sheet holds the navigation).
  await page.getByRole("button", { name: "Your account" }).first().click();
  await page.getByRole("menuitem", { name: "Sign out on all devices" }).click();
  await expect(page.getByRole("link", { name: "Sign in" }).first()).toBeVisible();
  await evidence(page, "QA-F02-08", testInfo);
  expect((await refresh(stillB.session!.refreshToken)).status, "every device").toBe(401);
  expect(secondsLeft(stillB.session!.accessToken), "OBS-01: the other device's token ends within 15 minutes").toBeLessThanOrEqual(15 * 60);
  test.info().annotations.push({ type: "OBS-01", description: `live access token after sign-out everywhere: ${await mineStatus(stillB.session!.accessToken)}` });
  await page.goto("/bookings/mine");
  await expect(page.getByRole("link", { name: "Sign in" }).or(page.locator('input[type="password"]')).first()).toBeVisible();
});

test("QA-F02-09 a donor suspended mid-session is refused at once: live token, refresh, sign-in; reactivated, they are back", async ({ page }, testInfo) => {
  const d4 = account("D4");
  const live = (await mobileLogin(d4)).session!;
  expect(await mineStatus(live.accessToken)).toBe(200);
  await signIn(page, d4);

  const admin = await signedIn(account("P1"));
  const id = userRow(d4.phone)!.id;
  try {
    await stepUpApi(admin, account("P1"));
    expect((await admin.post(`/api/admin/users/${id}/suspend`)).status()).toBe(204);
    expect(userRow(d4.phone)!.status).toBe("Suspended");

    expect([401, 403], "the live access token is refused").toContain(await mineStatus(live.accessToken));
    expect((await refresh(live.refreshToken)).status, "its refresh").toBe(401);
    const again = await mobileLogin(d4);
    expect(`${again.status} ${JSON.parse(again.body).code}`).toBe("403 account_not_active");

    // The browser session already open: its next page needs a refresh and lands on sign-in.
    await page.goto("/bookings/mine");
    await expect(page.getByRole("link", { name: "Sign in" }).or(page.locator('input[type="password"]')).first()).toBeVisible({ timeout: 20_000 });
    await page.goto("/login");
    await page.locator('main form input:not([type="password"]):not([type="hidden"])').first().fill(d4.phone);
    await page.locator('input[type="password"]').fill(password);
    await page.locator('button[type="submit"]').click();
    await expect(page.getByText("This account is not active. Please contact Sethika support.")).toBeVisible();
    await evidence(page, "QA-F02-09", testInfo);
  } finally {
    expect((await admin.post(`/api/admin/users/${id}/reactivate`)).status()).toBe(204);
    await admin.dispose();
  }
  expect((await mobileLogin(d4)).status, "reactivated").toBe(200);
});
