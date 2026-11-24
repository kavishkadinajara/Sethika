import { mkdirSync } from "node:fs";
import path from "node:path";
import type { Page } from "@playwright/test";
import { createAccount, grantPlatformRole, signIn } from "../support/accounts";
import { approvedInstitution } from "../support/institutions";
import { expect, scrollsSideways, test, widerThanWindow } from "../support/test";

/*  Plan 05, X4: every page on the new base, in dark. Each kind of session walks the pages it can
    open; each page must load without an error, never scroll sideways, and is kept as a picture to
    read for anything that still assumed the old green canvas.                                  */

const screens = path.join(__dirname, "..", ".screens");

async function walk(page: Page, project: string, who: string, addresses: string[]) {
  mkdirSync(screens, { recursive: true });
  for (const address of addresses) {
    await page.goto(address);
    await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");
    await expect(page.getByRole("heading").first()).toBeVisible();
    await page.waitForLoadState("networkidle");
    expect(await widerThanWindow(page), address).toEqual([]);
    expect(await scrollsSideways(page), address).toBe(false);
    const name = address === "/" ? "home" : address.slice(1).replace(/[/?=&]/g, "-");
    await page.screenshot({ path: path.join(screens, `${project}-walk-dark-${who}-${name}.png`), fullPage: true });
  }
}

test.beforeEach(async ({ page, baseURL }) => {
  await page.context().addCookies([{ name: "sethika_theme", value: "dark", url: baseURL! }]);
});

test("signed out, every page is dark and fits @phone", async ({ page }, testInfo) => {
  await walk(page, testInfo.project.name, "out", ["/login", "/signup", "/forgot-password", "/no-such-page"]);
});

test("a donor's pages are dark and fit @phone", async ({ page, baseURL }, testInfo) => {
  const institution = await approvedInstitution(baseURL!, `E2E Walk Kovil ${Date.now()}`);
  const donor = await createAccount(baseURL!, "E2E Walk Donor");
  await signIn(page, donor);
  await walk(page, testInfo.project.name, "donor", ["/", "/find", `/i/${institution.slug}`, "/bookings/mine", "/apply"]);
});

test("an owner's pages are dark and fit @phone", async ({ page, baseURL }, testInfo) => {
  const institution = await approvedInstitution(baseURL!, `E2E Walk Vihara ${Date.now()}`);
  await signIn(page, institution.owner);
  await walk(page, testInfo.project.name, "owner", ["/", "/bookings/calendar", "/bookings/slots", "/access/members", "/access/roles"]);
});

test("the platform's pages are dark and fit @phone", async ({ page, baseURL }, testInfo) => {
  const admin = await createAccount(baseURL!, "E2E Walk Admin");
  grantPlatformRole(admin, "PLATFORM_ADMIN");
  await signIn(page, admin);
  await walk(page, testInfo.project.name, "platform", ["/", "/admin/registrations", "/admin/users", "/admin/institutions"]);
});
