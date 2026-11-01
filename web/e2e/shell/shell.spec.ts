import { mkdirSync } from "node:fs";
import path from "node:path";
import type { Locator, Page } from "@playwright/test";
import { createAccount, grantPlatformRole, signIn } from "../support/accounts";
import { approvedInstitution, bookingBy, colomboDate, slotOf } from "../support/institutions";
import { sql } from "../support/sql";
import { clipsSideways, expect, scrollsSideways, test } from "../support/test";

/*  Plan 05, X2: the living sidebar. The menu is the RBAC catalogue filtered by what each person
    may view (plan 04, W2), so each role is signed in for real and its menu read from the screen:
    a donor never sees an institution's or the platform's links, an owner sees their institution's
    with the count of requests to answer and today's table, a platform admin the registrations.
    On a phone the same body is in the menu sheet.                                            */

const screens = path.join(__dirname, "..", ".screens");

/** The Day Ledger's date (plan 05, R4): the weekday, then the day and month in English's order ("Saturday October 31"). */
const LEDGER_DATE = /^(Sun|Mon|Tues|Wednes|Thurs|Fri|Satur)day\s+\w+ \d{1,2}$/;
const isPhone = (page: Page) => (page.viewportSize()?.width ?? 1280) < 1024;

async function shot(page: Page, project: string, name: string) {
  mkdirSync(screens, { recursive: true });
  await page.screenshot({ path: path.join(screens, `${project}-${name}.png`), fullPage: true });
}

/** The sidebar as the person reaches it: the desktop column, or the phone's sheet (opened). */
async function sidebar(page: Page): Promise<Locator> {
  if (!isPhone(page)) return page.getByRole("complementary", { name: "Sidebar" });
  await page.getByRole("button", { name: "Open the menu" }).click();
  return page.getByRole("dialog", { name: "Main menu" });
}

async function closeSheet(page: Page) {
  if (isPhone(page)) await page.getByRole("dialog", { name: "Main menu" }).getByRole("button", { name: "Close" }).click();
}

/** The menu's links by their words, without a badge's count. */
async function menuLinks(page: Page): Promise<string[]> {
  const menu = (await sidebar(page)).getByRole("navigation", { name: "Main menu" });
  await expect(menu.getByRole("link").first()).toBeVisible();
  const names = (await menu.getByRole("link").allInnerTexts()).map((name) => name.replace(/\s*\d+\s*\d+ waiting$/, "").trim());
  await closeSheet(page);
  return names;
}

for (const locale of ["en", "si", "ta"] as const) {
  test(`the landing page in ${locale} @phone`, async ({ page }, testInfo) => {
    await page.goto(locale === "en" ? "/" : `/${locale}`);
    await expect(page.locator("html")).toHaveAttribute("lang", locale);
    await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
    // Signed out: the public way in, and no sidebar.
    await expect(page.getByRole("complementary", { name: "Sidebar" })).toHaveCount(0);
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
  await expect(page.getByRole("heading", { level: 1 })).toHaveText(LEDGER_DATE);
  await expect(page.getByText("Welcome back, E2E Applicant")).toBeVisible();
  await page.waitForLoadState("networkidle");
  expect(await scrollsSideways(page)).toBe(false);
  await shot(page, testInfo.project.name, "en-dashboard-donor");

  const links = await menuLinks(page);
  expect(links).toEqual(["Dashboard", "Find a place to give", "My bookings", "My service requests", "My applications"]);

  // Who the session acts as: the donor's own account, with nothing to switch to.
  const side = await sidebar(page);
  await expect(side.getByText("Acting as")).toBeVisible();
  await expect(side.getByText("Donor", { exact: true })).toBeVisible();
  await expect(side.getByRole("button", { name: /^Switch institution/ })).toHaveCount(0);
  await expect(side.getByText("Today's table")).toHaveCount(0);
  if (isPhone(page)) {
    expect(await clipsSideways(page, '[role="dialog"]'), "the sheet at 320 px").toEqual([]);
    await shot(page, testInfo.project.name, "en-sheet-donor");
  }
});

test("the sidebar takes a donor to a screen and marks where they are @phone", async ({ applicant }) => {
  const { page } = applicant;
  await page.goto("/");
  await (await sidebar(page)).getByRole("link", { name: "My bookings" }).click();
  await expect(page).toHaveURL(/\/bookings\/mine$/);
  await expect(page.getByRole("heading", { level: 1, name: "My bookings" })).toBeVisible();
  // The sheet closes on the way.
  await expect(page.getByRole("dialog", { name: "Main menu" })).toHaveCount(0);

  const side = await sidebar(page);
  await expect(side.getByRole("link", { name: "My bookings" })).toHaveAttribute("aria-current", "page");
  await expect(side.getByRole("link", { name: "Dashboard" })).not.toHaveAttribute("aria-current", "page");
});

test("folded, the sidebar keeps every link, each named on hover above the page, and stays folded", async ({ applicant }, testInfo) => {
  const { page } = applicant;
  await page.goto("/");
  await expect(page.getByRole("heading", { level: 1 })).toHaveText(LEDGER_DATE);
  const side = page.getByRole("complementary", { name: "Sidebar" });
  const unfolded = (await side.boundingBox())!.width;

  await side.getByRole("button", { name: "Fold the sidebar" }).click();
  await expect(side.getByRole("button", { name: "Open the sidebar" })).toHaveAttribute("aria-expanded", "false");
  await expect.poll(async () => (await side.boundingBox())!.width).toBeLessThan(unfolded / 2);
  await shot(page, testInfo.project.name, "en-sidebar-folded");

  const links = side.getByRole("navigation", { name: "Main menu" }).getByRole("link");
  await expect(links).toHaveCount(5);
  for (const link of await links.all()) {
    const name = (await link.getAttribute("aria-label"))!;
    await link.hover();
    const tooltip = page.getByRole("tooltip");
    await expect(tooltip).toHaveText(name);
    // The topmost element at the tooltip's centre is the tooltip itself, not the greeting or a card
    // (plan 04's rail tooltip sat under the page).
    const box = (await tooltip.boundingBox())!;
    const onTop = await tooltip.evaluate(
      (el, { x, y }) => {
        const hit = document.elementFromPoint(x, y);
        return hit !== null && (hit === el || el.contains(hit) || hit.contains(el));
      },
      { x: box.x + box.width / 2, y: box.y + box.height / 2 },
    );
    expect(onTop, `${name}'s tooltip is covered`).toBe(true);
    // In steps, as a pointer moves: Radix keeps a tooltip open while the pointer heads for it.
    await page.mouse.move(800, 400, { steps: 8 });
    await expect(tooltip).toHaveCount(0);
  }

  // Kept per browser: a reload opens it folded.
  await page.reload();
  await expect(side.getByRole("button", { name: "Open the sidebar" })).toBeVisible();
  await side.getByRole("button", { name: "Open the sidebar" }).click();
  await expect(side.getByRole("button", { name: "Fold the sidebar" })).toHaveAttribute("aria-expanded", "true");
});

test("jump to finds a screen by its name and opens it, offering only screens the person may view @phone", async ({ applicant }, testInfo) => {
  const { page } = applicant;
  await page.goto("/");
  await expect(page.getByRole("heading", { level: 1 })).toHaveText(LEDGER_DATE);

  if (isPhone(page)) {
    await (await sidebar(page)).getByRole("button", { name: "Jump to" }).click();
  } else {
    await page.keyboard.press("Control+K");
  }
  const palette = page.getByRole("dialog", { name: "Jump to" });
  const search = palette.getByRole("combobox", { name: "Jump to" });
  await expect(search).toBeFocused();
  await expect(palette.getByRole("option")).toHaveCount(5);

  // Not the platform's or an institution's, whatever is typed.
  await search.fill("registration");
  await expect(palette.getByRole("option")).toHaveCount(0);
  await expect(palette.getByText("No screen matches “registration”.")).toBeVisible();

  await search.fill("book");
  await expect(palette.getByRole("option")).toHaveText([/^My bookings/]);
  await expect(palette.getByRole("option", { name: "My bookings" })).toHaveAttribute("aria-selected", "true");
  await shot(page, testInfo.project.name, "en-jump");
  await search.press("Enter");
  await expect(palette).toHaveCount(0);
  await expect(page).toHaveURL(/\/bookings\/mine$/);

  // The arrows move the choice, and Escape gives up.
  await page.keyboard.press("Control+K");
  await page.getByRole("combobox", { name: "Jump to" }).press("ArrowDown");
  await expect(page.getByRole("option").nth(1)).toHaveAttribute("aria-selected", "true");
  await page.keyboard.press("Escape");
  await expect(page.getByRole("dialog", { name: "Jump to" })).toHaveCount(0);
  await expect(page).toHaveURL(/\/bookings\/mine$/);
});

test("an owner's sidebar names the institution, counts the requests to answer, and lays today's table @phone", async ({ page, baseURL }, testInfo) => {
  const institution = await approvedInstitution(baseURL!, `E2E Shell Vihara ${Date.now()}`);
  const lunch = await slotOf(baseURL!, institution, { name: "Lunch", bookingMode: "Shared", maxSponsors: 4 });
  const donor = await createAccount(baseURL!, "E2E Shell Donor");
  const today = await bookingBy(baseURL!, donor, lunch, colomboDate(7));
  const later = await bookingBy(baseURL!, donor, lunch, colomboDate(8));
  // Nobody may book for today here (one day ahead at least): move the first one's day to today.
  sql(
    `UPDATE si SET SlotDate = CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Sri Lanka Standard Time' AS DATE)
     FROM booking.SlotInstances si JOIN booking.Bookings b ON b.SlotInstanceId = si.Id WHERE b.BookingNo = '$(no)';`,
    { no: today },
  );

  await signIn(page, institution.owner);
  await page.goto("/");
  await expect(page.getByText(`Here is ${institution.name} at a glance.`)).toBeVisible();

  let side = await sidebar(page);
  await expect(side.getByRole("button", { name: `Switch institution: ${institution.name}` })).toBeVisible();
  await expect(side.getByRole("link", { name: "Booking calendar 2 waiting" })).toBeVisible();
  const table = side.getByRole("region", { name: "Today's table" });
  await expect(table.getByRole("link")).toHaveText([/Lunch\s*11:30 · 1 of 4 taken\s*1 to answer/]);
  await page.waitForLoadState("networkidle");
  expect(await clipsSideways(page, isPhone(page) ? '[role="dialog"]' : "aside")).toEqual([]);
  await shot(page, testInfo.project.name, "en-dashboard-owner");

  // Acting for the institution: its screens, and not the person's own bookings (plan 02, B1).
  await closeSheet(page);
  const links = await menuLinks(page);
  expect(links).toEqual(expect.arrayContaining(["Booking calendar", "Meals and days", "Roles", "Staff"]));
  expect(links).not.toContain("My bookings");
  expect(links).not.toContain("Registration requests");

  // Today's table leads to the calendar; answering a request there takes the count down at once.
  side = await sidebar(page);
  await side.getByRole("region", { name: "Today's table" }).getByRole("link").click();
  await expect(page).toHaveURL(/\/bookings\/calendar$/);
  const request = page.locator("article").filter({ hasText: later });
  await request.getByRole("button", { name: "Accept", exact: true }).click();
  await expect(page.getByText(`${later} accepted. The donor has been texted.`).first()).toBeVisible();
  side = await sidebar(page);
  await expect(side.getByRole("link", { name: "Booking calendar 1 waiting" })).toBeVisible();
  await closeSheet(page);
});

test("the sidebar in dark, folded and open, desktop and phone @phone", async ({ page, baseURL }, testInfo) => {
  const institution = await approvedInstitution(baseURL!, `E2E Dark Vihara ${Date.now()}`);
  await slotOf(baseURL!, institution, { name: "Dana", bookingMode: "Shared", maxSponsors: 4 });
  await page.context().addCookies([{ name: "sethika_theme", value: "dark", url: baseURL! }]);
  await signIn(page, institution.owner);
  await page.goto("/");
  await expect(page.getByText(`Here is ${institution.name} at a glance.`)).toBeVisible();
  await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");
  const side = await sidebar(page);
  await expect(side.getByRole("region", { name: "Today's table" }).getByText("Dana")).toBeVisible();
  await page.waitForLoadState("networkidle");
  await shot(page, testInfo.project.name, "en-dark-owner");
  if (!isPhone(page)) {
    await side.getByRole("button", { name: "Fold the sidebar" }).click();
    await shot(page, testInfo.project.name, "en-dark-owner-folded");
    await side.getByRole("button", { name: "Open the sidebar" }).click();
  }
});

test("an owner with one institution switches to their own account and back, the menu never blinking out (P1) @phone", async ({ page, baseURL }) => {
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

  // Back into the institution from the card. From here until the dashboard answers, the menu must
  // never be empty: the old one stays (busy) while the new context's permissions load.
  const side = await sidebar(page);
  if (!isPhone(page)) {
    await page.evaluate(() => {
      const w = window as unknown as { emptied: boolean };
      w.emptied = false;
      const count = () => document.querySelectorAll('aside nav[aria-label="Main menu"] a').length;
      new MutationObserver(() => {
        if (count() === 0) w.emptied = true;
      }).observe(document.body, { childList: true, subtree: true });
    });
  }
  await side.getByRole("button", { name: /^Switch institution/ }).click();
  await page.getByRole("menuitem", { name: institution.name }).click();
  await expect(page.getByText(`Here is ${institution.name} at a glance.`)).toBeVisible();
  if (!isPhone(page)) {
    expect(await page.evaluate(() => (window as unknown as { emptied: boolean }).emptied), "the menu blinked out").toBe(false);
  }
  expect(await menuLinks(page)).toContain("Booking calendar");
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
  expect(links).toEqual(expect.arrayContaining(["Registration requests", "Institutions", "Users"]));
  expect(links).not.toContain("Booking calendar");
  const side = await sidebar(page);
  await expect(side.getByText("Platform team")).toBeVisible();
  await closeSheet(page);
});

test("signing out from the account menu lands on the landing page", async ({ applicant }) => {
  const { page } = applicant;
  await page.goto("/");
  await page.getByRole("button", { name: "Your account" }).click();
  await page.getByRole("menuitem", { name: "Sign out", exact: true }).click();
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Sponsor a meal where it is needed most");
  await expect(page.getByRole("button", { name: "Your account" })).toHaveCount(0);
});
