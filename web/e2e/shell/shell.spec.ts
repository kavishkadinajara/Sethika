import { mkdirSync } from "node:fs";
import path from "node:path";
import type { Page } from "@playwright/test";
import { createAccount, grantPlatformRole, signIn } from "../support/accounts";
import { approvedInstitution } from "../support/institutions";
import { expect, scrollsSideways, test } from "../support/test";

/*  Plan 04, U1: the app shell. The menu is the RBAC catalogue filtered by what each person may
    view (W2), so each role is signed in for real and its menu read from the screen: a donor never
    sees an institution's or the platform's sections, an owner sees their institution's, a platform
    admin the registrations. On a phone the same tree is in the menu sheet.                     */

const screens = path.join(__dirname, "..", ".screens");
const isPhone = (page: Page) => (page.viewportSize()?.width ?? 1280) < 1024;

async function shot(page: Page, project: string, name: string) {
  mkdirSync(screens, { recursive: true });
  await page.screenshot({ path: path.join(screens, `${project}-${name}.png`), fullPage: true });
}

/** The menu's links as the person sees them: the sidebar, or the phone's sheet. */
async function menuLinks(page: Page): Promise<string[]> {
  if (isPhone(page)) {
    await page.getByRole("button", { name: "Open the menu" }).click();
    const sheet = page.getByRole("dialog");
    const links = await sheet.getByRole("navigation", { name: "Main menu" }).getByRole("link").allTextContents();
    await sheet.getByRole("button", { name: "Close" }).click();
    return links.map((l) => l.trim());
  }
  const sidebar = page.getByRole("navigation", { name: "Main menu" });
  // Right after a switch the session reloads and the sidebar is briefly gone: read it once it is back.
  await expect(sidebar.getByRole("link").first()).toBeVisible();
  return (await sidebar.getByRole("link").allTextContents()).map((l) => l.trim());
}

for (const locale of ["en", "si", "ta"] as const) {
  test(`the landing page in ${locale} @phone`, async ({ page }, testInfo) => {
    await page.goto(locale === "en" ? "/" : `/${locale}`);
    await expect(page.locator("html")).toHaveAttribute("lang", locale);
    await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
    // Signed out: the public way in, and no menu of signed-in screens.
    await expect(page.getByRole("navigation", { name: /./ }).filter({ has: page.getByRole("link", { name: /Registration requests/ }) })).toHaveCount(0);
    if (locale === "en") {
      await expect(page.getByRole("heading", { level: 1 })).toHaveText("Sponsor a meal where it is needed most");
      await expect(page.getByRole("link", { name: "Sign in" })).toBeVisible();
      await expect(page.getByRole("link", { name: "Find a place to give" }).first()).toBeVisible();
    }
    await page.waitForLoadState("networkidle");
    expect(await scrollsSideways(page)).toBe(false);
    await shot(page, testInfo.project.name, `${locale}-landing`);
  });
}

test("a donor's menu holds giving, not an institution's or the platform's @phone", async ({ applicant }, testInfo) => {
  const { page } = applicant;
  await page.goto("/");
  await expect(page.getByRole("heading", { level: 1 })).toHaveText(/Good (morning|afternoon|evening)!/);
  await expect(page.getByText("Welcome back, E2E Applicant")).toBeVisible();
  await expect(page.getByRole("heading", { name: "Quick actions" })).toBeVisible();
  await expect(page.getByRole("link", { name: /Registration requests/ })).toHaveCount(0);
  await page.waitForLoadState("networkidle");
  expect(await scrollsSideways(page)).toBe(false);
  await shot(page, testInfo.project.name, "en-dashboard-donor");

  const links = await menuLinks(page);
  expect(links).toEqual(expect.arrayContaining(["Dashboard", "Find a place to give", "My bookings", "My applications"]));
  expect(links).not.toContain("Booking calendar");
  expect(links).not.toContain("Registration requests");
});

test("the sidebar takes a donor to a screen, and the pill glides to it", async ({ applicant }, testInfo) => {
  const { page } = applicant;
  await page.goto("/");
  const sidebar = page.getByRole("navigation", { name: "Main menu" });
  await expect(sidebar.getByRole("link", { name: "Dashboard" })).toHaveAttribute("aria-current", "page");
  await expect(sidebar.getByRole("region", { name: "Giving" })).toBeVisible();

  await sidebar.getByRole("link", { name: "My bookings" }).click();
  await expect(page).toHaveURL(/\/bookings\/mine$/);
  await expect(page.getByRole("heading", { level: 1, name: "My bookings" })).toBeVisible();
  const here = sidebar.getByRole("link", { name: "My bookings" });
  await expect(here).toHaveAttribute("aria-current", "page");
  await expect(sidebar.getByRole("link", { name: "Dashboard" })).not.toHaveAttribute("aria-current", "page");

  // Once the glide settles, the pill's band is the link's own box.
  await expect
    .poll(async () => {
      const link = await here.evaluate((el) => ({ top: (el as HTMLElement).offsetTop, bottom: (el as HTMLElement).offsetTop + (el as HTMLElement).offsetHeight }));
      const pill = await sidebar.locator(".glide").evaluate((el) => {
        const style = getComputedStyle(el);
        return { top: parseFloat(style.getPropertyValue("--glide-start")), bottom: parseFloat(style.getPropertyValue("--glide-end")) };
      });
      return pill.top === link.top && pill.bottom === link.bottom;
    })
    .toBe(true);
  await shot(page, testInfo.project.name, "en-sidebar-my-bookings");
});

test("an institution owner's menu holds their institution, and the bar names it @phone", async ({ page, baseURL }, testInfo) => {
  const institution = await approvedInstitution(baseURL!, `E2E Shell Vihara ${Date.now()}`);
  await signIn(page, institution.owner);
  await page.goto("/");
  await expect(page.getByText(`Here is ${institution.name} at a glance.`)).toBeVisible();
  if (!isPhone(page)) await expect(page.getByRole("banner").getByText(institution.name)).toBeVisible();
  await page.waitForLoadState("networkidle");
  await shot(page, testInfo.project.name, "en-dashboard-owner");

  // Acting for the institution: its screens, and not the person's own bookings (plan 02, B1).
  const links = await menuLinks(page);
  expect(links).toEqual(expect.arrayContaining(["Booking calendar", "Meals and days"]));
  expect(links).not.toContain("My bookings");
  expect(links).not.toContain("Registration requests");
});

test("an owner with one institution switches to their own account and back (P1) @phone", async ({ page, baseURL }) => {
  const institution = await approvedInstitution(baseURL!, `E2E Personal Vihara ${Date.now()}`);
  await signIn(page, institution.owner);

  // A donor's screen in the institution's session says why it is closed, and opens it in one click.
  await page.goto("/bookings/mine");
  await expect(page.getByText("You are acting for an institution.")).toBeVisible();
  await page.getByRole("button", { name: "Switch to my own account" }).click();
  await expect(page.getByText("You are acting for an institution.")).toBeHidden();
  await expect(page.getByRole("heading", { level: 1, name: "My bookings" })).toBeVisible();

  // A reload refreshes the session: it stays personal (the refresh used to put them back).
  await page.reload();
  await expect(page.getByRole("heading", { level: 1, name: "My bookings" })).toBeVisible();
  await expect(page.getByText("You are acting for an institution.")).toBeHidden();
  expect(await menuLinks(page)).toContain("My bookings");

  // And back into the institution from the context switcher.
  if (isPhone(page)) await page.getByRole("button", { name: "Open the menu" }).click();
  await page.getByRole("button", { name: /^Switch institution/ }).click();
  await page.getByRole("menuitem", { name: institution.name }).click();
  await expect(page.getByText(`Here is ${institution.name} at a glance.`)).toBeVisible();
});

test("a platform admin's menu holds the registrations, and no institution @phone", async ({ page, baseURL }, testInfo) => {
  const admin = await createAccount(baseURL!, "E2E Shell Admin");
  grantPlatformRole(admin, "PLATFORM_ADMIN");
  await signIn(page, admin);
  await page.goto("/");
  await expect(page.getByText("Every institution and every request, in one place.")).toBeVisible();
  await page.waitForLoadState("networkidle");
  await shot(page, testInfo.project.name, "en-dashboard-platform");

  const links = await menuLinks(page);
  expect(links).toContain("Registration requests");
  expect(links).not.toContain("Booking calendar");
});

test("signing out from the account menu lands on the landing page", async ({ applicant }) => {
  const { page } = applicant;
  await page.goto("/");
  await page.getByRole("button", { name: "Your account" }).click();
  await page.getByRole("menuitem", { name: "Sign out", exact: true }).click();
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Sponsor a meal where it is needed most");
  await expect(page.getByRole("button", { name: "Your account" })).toHaveCount(0);
});
