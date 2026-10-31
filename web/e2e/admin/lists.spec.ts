import { mkdirSync } from "node:fs";
import path from "node:path";
import type { Page } from "@playwright/test";
import { createAccount, grantPlatformRole, signIn } from "../support/accounts";
import { approvedInstitution } from "../support/institutions";
import { sql } from "../support/sql";
import { expect, scrollsSideways, test, widerThanWindow } from "../support/test";

/*  Plan 05, X4: the platform's lists a page at a time. Search, filters, order and page are kept in
    the address, so a reload keeps them and the back button undoes them; each change goes back to
    the first page. A step-up given up says that nothing changed, not that something went wrong. */

const screens = path.join(__dirname, "..", ".screens");

async function shot(page: Page, project: string, name: string) {
  mkdirSync(screens, { recursive: true });
  await page.screenshot({ path: path.join(screens, `${project}-${name}.png`), fullPage: true });
}

const last9 = (phone: string) => phone.replace(/\D/g, "").slice(-9);
/** The list's own cards, not the lists inside a card (an account's institutions and roles). */
const cards = (page: Page) => page.getByRole("list", { name: /^(Accounts|Institutions)$/ }).locator(":scope > li");
const names = (page: Page) => cards(page).locator("span.font-semibold.text-fg");

test("the accounts list filters by kind and status, orders by name, and keeps it all in the address @phone", async ({ page, baseURL }, testInfo) => {
  const admin = await createAccount(baseURL!, "E2E Lists Admin");
  grantPlatformRole(admin, "PLATFORM_ADMIN");
  const stamp = String(Date.now());
  const cara = await createAccount(baseURL!, `E2E Lister ${stamp} Cara`);
  const amal = await createAccount(baseURL!, `E2E Lister ${stamp} Amal`);
  const bimal = await createAccount(baseURL!, `E2E Lister ${stamp} Bimal`);
  grantPlatformRole(bimal, "PLATFORM_REVIEWER");
  sql(`UPDATE auth.Users SET Status = 'Suspended' WHERE RIGHT(PhoneNumber, 9) = '$(p)';`, { p: last9(amal.phone) });

  await signIn(page, admin);
  await page.goto("/admin/users");
  await page.getByLabel(/^Search/).fill(`E2E Lister ${stamp}`);
  await expect(page).toHaveURL(/[?&]q=E2E\+Lister\+\d+/);
  await expect(page.getByRole("status").filter({ hasText: /of 3$/ })).toHaveText("1–3 of 3");
  // Newest first: the last made leads.
  await expect(names(page)).toHaveText([bimal.fullName, amal.fullName, cara.fullName]);

  // A kind, then a status: each a chip, each in the address.
  const kinds = page.getByRole("group", { name: "Kind of account" });
  await kinds.getByRole("button", { name: "Donor" }).click();
  await expect(page).toHaveURL(/kind=Donor/);
  await expect(cards(page)).toHaveCount(2);
  await page.getByRole("group", { name: "Status" }).getByRole("button", { name: "Suspended" }).click();
  await expect(page).toHaveURL(/status=Suspended/);
  await expect(cards(page)).toHaveCount(1);
  await expect(cards(page)).toContainText(amal.fullName);
  await page.waitForLoadState("networkidle");
  expect(await widerThanWindow(page)).toEqual([]);
  expect(await scrollsSideways(page)).toBe(false);
  await shot(page, testInfo.project.name, "en-admin-users-filtered");

  // A reload keeps it all; the back button takes the status away again.
  await page.reload();
  await expect(page.getByLabel(/^Search/)).toHaveValue(`E2E Lister ${stamp}`);
  await expect(kinds.getByRole("button", { name: "Donor" })).toHaveAttribute("aria-pressed", "true");
  await expect(cards(page)).toHaveCount(1);
  await page.goBack();
  await expect(page).not.toHaveURL(/status=/);
  await expect(cards(page)).toHaveCount(2);

  // By name, and every kind again.
  await kinds.getByRole("button", { name: "All" }).click();
  await page.getByLabel("Order").selectOption("name");
  await expect(page).toHaveURL(/sort=name/);
  await expect(names(page)).toHaveText([amal.fullName, bimal.fullName, cara.fullName]);
});

test("the whole accounts list goes page by page, and a filter starts again at the first @phone", async ({ page, baseURL }) => {
  const admin = await createAccount(baseURL!, "E2E Pages Admin");
  grantPlatformRole(admin, "PLATFORM_ADMIN");
  await signIn(page, admin);
  await page.goto("/admin/users");

  // The test accounts of every earlier run fill many pages.
  const count = page.getByRole("status").filter({ hasText: /^\d+–\d+ of \d+$/ });
  await expect(count).toHaveText(/^1–24 of \d+$/);
  const pages = page.getByRole("navigation", { name: "Pages" });
  await expect(pages.getByRole("button", { name: "Previous" })).toBeDisabled();
  await pages.getByRole("button", { name: "Next" }).click();
  await expect(page).toHaveURL(/page=2/);
  await expect(count).toHaveText(/^25–48 of \d+$/);
  await expect(pages.getByText(/^Page 2 of \d+$/)).toBeVisible();
  await expect(cards(page)).toHaveCount(24);

  await page.getByRole("group", { name: "Kind of account" }).getByRole("button", { name: "Platform team" }).click();
  await expect(page).not.toHaveURL(/page=/);
  await expect(count).toHaveText(/^1–\d+ of \d+$/);
});

test("the institutions list filters by status and district, in the address, on a phone too @phone", async ({ page, baseURL }, testInfo) => {
  const admin = await createAccount(baseURL!, "E2E Inst Lists Admin");
  grantPlatformRole(admin, "PLATFORM_ADMIN");
  const institution = await approvedInstitution(baseURL!, `E2E Lister Kovil ${Date.now()}`);
  sql(`UPDATE org.Institutions SET Status = 'Suspended' WHERE Slug = '$(slug)';`, { slug: institution.slug });

  await signIn(page, admin);
  await page.goto("/admin/institutions");
  await page.getByLabel(/^Search/).fill(institution.name);
  await expect(cards(page)).toHaveCount(1);
  // The owner is the one holding the owner role.
  await expect(cards(page)).toContainText(`Owner: ${institution.owner.fullName}`);

  const statuses = page.getByRole("group", { name: "Status" });
  await statuses.getByRole("button", { name: "Active" }).click();
  await expect(page).toHaveURL(/status=Active/);
  await expect(page.getByText("No institution matches.")).toBeVisible();
  await statuses.getByRole("button", { name: "Suspended" }).click();
  await expect(cards(page)).toHaveCount(1);
  // A suspended place cannot be opened as support.
  await expect(cards(page).getByRole("button", { name: "Open as platform support" })).toHaveCount(0);

  const district = page.getByLabel("District");
  await expect(district.locator("option")).not.toHaveCount(1);
  await district.selectOption({ index: 1 });
  await expect(page).toHaveURL(/district=/);
  await page.waitForLoadState("networkidle");
  expect(await widerThanWindow(page)).toEqual([]);
  expect(await scrollsSideways(page)).toBe(false);
  await shot(page, testInfo.project.name, "en-admin-institutions-filtered");
});

test("a step-up given up says nothing changed, not that something went wrong", async ({ page, baseURL }) => {
  const admin = await createAccount(baseURL!, "E2E Gave Up Admin");
  grantPlatformRole(admin, "PLATFORM_ADMIN");
  await signIn(page, admin);
  await page.goto("/admin/institutions");
  await page.getByRole("button", { name: "Open as platform support" }).first().click();
  await expect(page.getByRole("heading", { name: "Confirm it's you" })).toBeVisible();
  await page.keyboard.press("Escape");

  await expect(page.getByText("Nothing was changed: the extra check was not finished.").first()).toBeVisible();
  await expect(page.getByText("Something went wrong. Please try again.")).toHaveCount(0);
});
