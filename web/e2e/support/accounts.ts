import { expect, request, type APIRequestContext, type Page } from "@playwright/test";
import { sql } from "./sql";

/*  Accounts are made the way a person makes one - OTP, ticket, signup - through the same /api
    proxy the browser uses, with the code read back from notify.Notifications (the development
    SMS sender records every message it "sends"). Every test gets its own account, so tests can
    run in parallel and in any order. Their names start with "E2E " so the setup can find and
    clear what they leave behind.                                                             */

export const password = "E2e-Sethika-1";

/*  Every test runs from 127.0.0.1, and a run makes more accounts than the API's 20 signup codes
    per client per hour. People signing up are on different phones, so each account made here
    goes to the API directly with an address of its own in X-Forwarded-For, which the API believes
    from loopback (Program.cs, ForwardedHeaders) - the limit stays on, and each "person" has
    their own budget, as in life. Sign-ins through the screens still come from 127.0.0.1.      */
const apiURL = process.env.E2E_API_URL ?? "http://localhost:5182";

function anotherPhoneAddress(): string {
  const octet = () => 1 + Math.floor(Math.random() * 254);
  return `10.${octet()}.${octet()}.${octet()}`;
}

export type Account = { phone: string; fullName: string };

/**
 * A fresh mobile number, in the local format a person types: 0779 plus six random digits, and not
 * one an account already holds. Earlier runs leave thousands of accounts in that million, so a number
 * picked blindly was taken often enough that a full run usually met one "already_registered".
 */
export function newPhone(): string {
  for (;;) {
    const phone = "0779" + String(Math.floor(Math.random() * 1_000_000)).padStart(6, "0");
    const last9 = phone.slice(-9);
    const [taken] = sql("SELECT COUNT(*) FROM auth.Users WHERE RIGHT(PhoneNumber, 9) = '$(last9)';", { last9 });
    if (taken?.trim() === "0") return phone;
  }
}

/** The last code texted to <phone>, waiting for it to arrive. */
export async function latestCode(phone: string, after = 0): Promise<{ code: string; id: number }> {
  const last9 = phone.replace(/\D/g, "").slice(-9);
  let found: { code: string; id: number } | null = null;
  await expect
    .poll(
      () => {
        const [row] = sql(
          "SELECT TOP 1 CONCAT(Id, '|', Body) FROM notify.Notifications WHERE Channel = 'SMS' AND RIGHT(Recipient, 9) = '$(last9)' ORDER BY Id DESC;",
          { last9 },
        );
        const [id, body] = row ? [Number(row.split("|")[0]), row.slice(row.indexOf("|") + 1)] : [0, ""];
        const code = /\b(\d{6})\b/.exec(body)?.[1];
        found = code && id > after ? { code, id } : null;
        return found !== null;
      },
      { message: `a code texted to ${phone}`, timeout: 15_000 },
    )
    .toBe(true);
  return found!;
}

async function ok(response: Awaited<ReturnType<APIRequestContext["post"]>>, what: string) {
  if (!response.ok()) throw new Error(`${what}: ${response.status()} ${await response.text()}`);
  return response;
}

/** Signs a new donor up through the API and returns how to sign in as them. */
export async function createAccount(_baseURL: string, fullName = "E2E Applicant", language: "en" | "si" | "ta" = "en"): Promise<Account> {
  const phone = newPhone();
  const api = await request.newContext({ baseURL: apiURL, extraHTTPHeaders: { "X-Forwarded-For": anotherPhoneAddress() } });
  try {
    await ok(await api.post("/api/auth/otp/request", { data: { phoneNumber: phone, purpose: "signup", language } }), "OTP request");
    const { code } = await latestCode(phone);
    const verified = await ok(await api.post("/api/auth/otp/verify", { data: { phoneNumber: phone, purpose: "signup", code } }), "OTP verify");
    const { ticket } = (await verified.json()) as { ticket: string };
    const documents = (await (await ok(await api.get("/api/legal/current?language=en"), "Legal documents")).json()) as { contentSha256: string }[];
    await ok(
      await api.post("/api/auth/signup", {
        data: {
          phoneNumber: phone,
          verificationTicket: ticket,
          fullName,
          email: null,
          password,
          preferredLanguage: language,
          acceptedDocumentHashes: documents.map((d) => d.contentSha256),
        },
      }),
      "Signup",
    );
    return { phone, fullName };
  } finally {
    await api.dispose();
  }
}

/** Signs in on the login page, as a person would, and waits to leave it. */
export async function signIn(page: Page, account: Account, locale: "" | "si" | "ta" = "") {
  await page.goto(`${locale ? `/${locale}` : ""}/login`);
  // By type, not label, so it works in every language: the login form has one text field and a password.
  const login = page.locator('main form input:not([type="password"]):not([type="hidden"])').first();
  await login.waitFor();
  await login.fill(account.phone);
  await page.locator('input[type="password"]').fill(password);
  await page.locator('button[type="submit"]').click();
  await page.waitForURL((url) => !url.pathname.endsWith("/login"));
}

/** Makes <account> platform staff holding <roleCode>, as the integration tests do. */
export function grantPlatformRole(account: Account, roleCode: "PLATFORM_REVIEWER" | "PLATFORM_ADMIN") {
  const last9 = account.phone.replace(/\D/g, "").slice(-9);
  sql(
    `DECLARE @user uniqueidentifier = (SELECT Id FROM auth.Users WHERE RIGHT(PhoneNumber, 9) = '$(last9)');
     UPDATE auth.Users SET UserType = 'PlatformStaff' WHERE Id = @user;
     INSERT auth.UserRoles (UserId, RoleId, InstitutionId, IsActive)
     SELECT @user, r.Id, NULL, 1 FROM auth.Roles r WHERE r.Code = '$(role)' AND r.InstitutionId IS NULL;
     UPDATE auth.Users SET PermissionsVersion = PermissionsVersion + 1 WHERE Id = @user;`,
    { last9, role: roleCode },
  );
}

/** An API client signed in as <account>, for the steps of a scenario that have no screen yet. */
export async function apiAs(baseURL: string, account: Account): Promise<APIRequestContext> {
  const login = await request.newContext({ baseURL });
  const response = await ok(await login.post("/api/auth/login", { data: { login: account.phone, password } }), "Login");
  const { accessToken } = (await response.json()) as { accessToken: string };
  await login.dispose();
  return request.newContext({ baseURL, extraHTTPHeaders: { Authorization: `Bearer ${accessToken}` } });
}

/** The id of the last text sent to <phone>, 0 if none: a later code is one with a higher id. */
export function lastTextId(phone: string): number {
  const last9 = phone.replace(/\D/g, "").slice(-9);
  const [row] = sql("SELECT ISNULL(MAX(Id), 0) FROM notify.Notifications WHERE Channel = 'SMS' AND RIGHT(Recipient, 9) = '$(last9)';", { last9 });
  return Number(row ?? 0);
}

/** Answers the step-up dialog with the code texted to <account> after text <after>. */
export async function stepUp(page: Page, account: Account, after: number) {
  // By its heading: the step-up can open over another dialog (a sheet whose save needs it).
  const dialog = page.getByRole("dialog").filter({ has: page.getByRole("heading", { name: "Confirm it's you" }) });
  await expect(dialog).toBeVisible();
  const { code } = await latestCode(account.phone, after);
  await dialog.getByLabel("6-digit code").fill(code);
  await expect(dialog).toBeHidden();
}
