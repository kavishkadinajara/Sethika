import { mkdirSync } from "node:fs";
import path from "node:path";
import type { Page } from "@playwright/test";
import { createAccount, grantPlatformRole, lastTextId, latestCode, newPhone, signIn, stepUp } from "../support/accounts";
import { approvedInstitution } from "../support/institutions";
import { clipsSideways, expect, scrollsSideways, test } from "../support/test";

/*  Plan 04, U4: the platform administrator. Inside any institution as platform support they hold
    every permission and outrank its owner (Y1-Y4); on the platform's own screens they see every
    institution and every account, make accounts for people who cannot sign up, and suspend them
    (Y5-Y6). A reviewer sees the lists and none of the powers.                                  */

const screens = path.join(__dirname, "..", ".screens");

async function shot(page: Page, project: string, name: string) {
  mkdirSync(screens, { recursive: true });
  await page.screenshot({ path: path.join(screens, `${project}-${name}.png`), fullPage: true });
}

test("an administrator works inside an institution as platform support, outranking its owner @phone", async ({ page, baseURL }, testInfo) => {
  const institution = await approvedInstitution(baseURL!, `E2E Support Church ${Date.now()}`);
  const admin = await createAccount(baseURL!, "E2E Support Admin");
  grantPlatformRole(admin, "PLATFORM_ADMIN");
  await signIn(page, admin);

  await page.goto("/admin/institutions");
  await expect(page.getByRole("heading", { level: 1, name: "Institutions" })).toBeVisible();
  await page.getByLabel(/^Search/).fill(institution.name);
  const card = page.getByRole("listitem").filter({ hasText: institution.name });
  await expect(card.getByText("Owner: E2E Owner")).toBeVisible();
  expect(await scrollsSideways(page)).toBe(false);
  expect(await clipsSideways(page)).toEqual([]);
  await shot(page, testInfo.project.name, "en-admin-institutions");

  const before = lastTextId(admin.phone);
  await card.getByRole("button", { name: "Open as platform support" }).click();
  await stepUp(page, admin, before);
  await expect(page.getByText(`Here is ${institution.name} at a glance.`)).toBeVisible();
  const banner = page.getByRole("status").filter({ hasText: "as platform support" });
  await expect(banner).toContainText(institution.name);

  // Every permission, and senior to the owner: the Owner role is not locked for them.
  await page.goto("/access/roles");
  const list = page.getByRole("navigation", { name: "Roles in your institution" });
  await list.getByRole("button", { name: /Owner/ }).click();
  await expect(page.getByRole("heading", { name: "What Owner may do" })).toBeVisible();
  await expect(page.getByRole("group", { name: "Booking calendar", exact: true }).getByRole("checkbox").first()).toBeEnabled();
  await page.goto("/access/members");
  await expect(page.getByRole("button", { name: /^Remove Owner from E2E Owner/ })).toBeVisible();
  await shot(page, testInfo.project.name, "en-admin-support-members");

  // The platform's own screens are closed while inside; the banner leads back out.
  await page.getByRole("status").filter({ hasText: "as platform support" }).getByRole("button", { name: "Leave" }).click();
  await expect(page).toHaveURL(/\/admin\/institutions$/);
  await expect(page.getByRole("status").filter({ hasText: "as platform support" })).toHaveCount(0);
  await expect(page.getByRole("heading", { level: 1, name: "Institutions" })).toBeVisible();
});

test("an administrator makes an account that signs in with a texted code, and suspends it", async ({ page, baseURL, browser }) => {
  const admin = await createAccount(baseURL!, "E2E Users Admin");
  grantPlatformRole(admin, "PLATFORM_ADMIN");
  await signIn(page, admin);
  const phone = newPhone();
  const name = `E2E Made By Admin ${Date.now()}`;

  await page.goto("/admin/users");
  await page.getByRole("button", { name: "Create an account" }).click();
  const sheet = page.getByRole("dialog");
  await sheet.getByLabel(/^Full name/).fill(name);
  await sheet.getByLabel(/^Phone number/).fill(phone);
  let before = lastTextId(admin.phone);
  await sheet.getByRole("button", { name: "Create account" }).click();
  await stepUp(page, admin, before);
  await expect(page.getByText(`Account created for ${name}.`, { exact: false }).first()).toBeVisible();
  await page.getByLabel(/^Search/).fill(name);
  const row = page.getByRole("listitem").filter({ hasText: name });
  await expect(row.getByText("Not verified")).toBeVisible();

  // They sign in with a code: the first one proves the number and makes the account active.
  const context = await browser.newContext();
  const theirs = await context.newPage();
  await theirs.goto("/login/otp");
  const sent = lastTextId(phone);
  await theirs.getByLabel(/^Mobile number/).fill(phone);
  await theirs.getByRole("button", { name: "Send code" }).click();
  const { code } = await latestCode(phone, sent);
  await theirs.getByLabel("6-digit code").fill(code);
  await theirs.waitForURL((url) => !url.pathname.includes("/login"));
  await expect(theirs.getByText(/^Welcome back, E2E Made By Admin/)).toBeVisible();
  await context.close();

  await page.reload();
  await page.getByLabel(/^Search/).fill(name);
  await expect(row.getByText("Active", { exact: true })).toBeVisible();
  before = lastTextId(admin.phone);
  await row.getByRole("button", { name: "Suspend" }).click();
  await stepUp(page, admin, before); // a new token after the reload, so a new step-up
  await expect(row.getByText("Suspended", { exact: true })).toBeVisible();
});

test("a reviewer sees the lists but cannot open an institution or change an account", async ({ page, baseURL }) => {
  const reviewer = await createAccount(baseURL!, "E2E Platform Reviewer");
  grantPlatformRole(reviewer, "PLATFORM_REVIEWER");
  await signIn(page, reviewer);

  await page.goto("/admin/institutions");
  await expect(page.getByRole("heading", { level: 1, name: "Institutions" })).toBeVisible();
  // an institution's card first (the sidebar's items are list items too): before it there is nothing to count
  await expect(page.getByText(/^(Owner: .+|No owner yet)$/).first()).toBeVisible();
  await expect(page.getByRole("button", { name: "Open as platform support" })).toHaveCount(0);

  await page.goto("/admin/users");
  // the rows first: before they load there is no Suspend button to count
  await expect(page.getByText(reviewer.phone.slice(1)).first()).toBeVisible(); // shown as +94…
  await expect(page.getByRole("button", { name: "Create an account" })).toHaveCount(0);
  // exact: the "Suspended" status filter is a button too
  await expect(page.getByRole("button", { name: "Suspend", exact: true })).toHaveCount(0);
});
