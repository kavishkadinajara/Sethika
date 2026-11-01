import { mkdirSync } from "node:fs";
import path from "node:path";
import type { Page } from "@playwright/test";
import { createAccount, grantPlatformRole, signIn } from "../support/accounts";
import { approvedInstitution, bookingBy, colomboDate, slotOf } from "../support/institutions";
import { sql } from "../support/sql";
import { clipsSideways, expect, scrollsSideways, test, widerThanWindow } from "../support/test";

/*  Plan 05, X3: the Day Ledger. Each kind of session is signed in for real and its day read from
    the screen: the one sentence (composed from data), the panels its permissions allow, an answer
    given inline, and a booking number typed into jump-to opening the calendar on its day. The
    sentence's tests are clock-free: requests to answer come first, whatever the hour.          */

const screens = path.join(__dirname, "..", ".screens");

async function shot(page: Page, project: string, name: string) {
  mkdirSync(screens, { recursive: true });
  await page.screenshot({ path: path.join(screens, `${project}-${name}.png`), fullPage: true });
}

/** The panel titled <title>, as a region of the page. */
const panel = (page: Page, title: string) => page.getByRole("main").getByRole("region", { name: title });

/** An institution with a shared lunch, one request for today and one for next week. */
async function dayWithRequests(baseURL: string) {
  const institution = await approvedInstitution(baseURL, `E2E Ledger Vihara ${Date.now()}`);
  const lunch = await slotOf(baseURL, institution, { name: "Lunch", bookingMode: "Shared", maxSponsors: 4 });
  const donor = await createAccount(baseURL, "E2E Ledger Donor");
  const today = await bookingBy(baseURL, donor, lunch, colomboDate(7));
  const later = await bookingBy(baseURL, donor, lunch, colomboDate(8));
  // Nobody may book for today here (one day ahead at least): move the first one's day to today.
  sql(
    `UPDATE si SET SlotDate = CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Sri Lanka Standard Time' AS DATE)
     FROM booking.SlotInstances si JOIN booking.Bookings b ON b.SlotInstanceId = si.Id WHERE b.BookingNo = '$(no)';`,
    { no: today },
  );
  return { institution, today, later };
}

test("an owner's day: the sentence, today's table, an answer given inline, and the ledger @phone", async ({ page, baseURL }, testInfo) => {
  const { institution, today, later } = await dayWithRequests(baseURL!);
  await signIn(page, institution.owner);
  await page.goto("/");

  await expect(page.getByText(`Here is ${institution.name} at a glance.`)).toBeVisible();
  await expect(page.getByText(/^2 bookings wait for your answer/)).toBeVisible();

  // Today's table in words under the line: the time, the meal, its places and its request.
  await expect(panel(page, "Today's table").getByRole("listitem")).toHaveText([/11:30\s*Lunch\s*1 of 4 taken · 1 to answer/]);

  const waiting = panel(page, "Waiting for you");
  await expect(waiting.getByRole("article")).toHaveCount(2);
  await expect(panel(page, "Month, bowl by bowl").locator('a[aria-current="date"]')).toBeVisible();
  await page.waitForLoadState("networkidle");
  expect(await scrollsSideways(page)).toBe(false);
  expect(await clipsSideways(page, "main")).toEqual([]);
  await shot(page, testInfo.project.name, "en-ledger-owner");

  // Accepted where it waits: the calendar's own call, then the sentence and the ledger follow.
  await waiting.getByRole("article").filter({ hasText: later }).getByRole("button", { name: "Accept", exact: true }).click();
  await expect(page.getByText(`${later} accepted. The donor has been texted.`).first()).toBeVisible();
  await expect(page.getByText(/^1 booking waits for your answer/)).toBeVisible();
  await expect(waiting.getByRole("article")).toHaveCount(1);
  await expect(waiting.getByRole("article")).toContainText(today);

  const ledger = panel(page, "The ledger");
  await expect(ledger.getByRole("listitem").first()).toContainText(/E2E Ledger Donor's Lunch on .+ was confirmed\./);
  await expect(ledger.getByRole("listitem").first().getByRole("link", { name: later })).toHaveAttribute("href", new RegExp(`booking=${later}$`));
  await expect(ledger.getByText(/E2E Ledger Donor booked Lunch for/)).toHaveCount(2);

  // The same day in dark, and in Sinhala and Tamil.
  await page.context().addCookies([{ name: "sethika_theme", value: "dark", url: baseURL! }]);
  await page.reload();
  await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");
  await expect(page.getByText(/^1 booking waits for your answer/)).toBeVisible();
  await page.waitForLoadState("networkidle");
  await shot(page, testInfo.project.name, "en-dark-ledger-owner");
  await page.context().clearCookies({ name: "sethika_theme" });

  // Through the language menu, which saves the choice to the account: an address alone would be
  // moved back to the account's English when the session starts (A11).
  const inWords = { si: "ඔබේ පිළිතුර බලාපොරොත්තුවෙන්", ta: "உங்கள் பதிலுக்காகக் காத்திருக்கிறது" };
  const names = { si: "සිංහල", ta: "தமிழ்" };
  for (const locale of ["si", "ta"] as const) {
    await page.getByRole("banner").getByRole("button", { name: /^(Language|භාෂාව|மொழி)$/ }).click();
    await page.getByRole("menuitem", { name: names[locale] }).click();
    await expect(page).toHaveURL(new RegExp(`/${locale}$`));
    await expect(page.locator("html")).toHaveAttribute("lang", locale);
    await expect(page.getByText(inWords[locale])).toBeVisible();
    await expect(page.getByRole("main").getByRole("article")).toHaveCount(1);
    await page.waitForLoadState("networkidle");
    expect(await scrollsSideways(page)).toBe(false);
    await shot(page, testInfo.project.name, `${locale}-ledger-owner`);
  }
});

test("a booking number typed into jump-to opens the calendar on its day, marked", async ({ page, baseURL }, testInfo) => {
  const { institution, later } = await dayWithRequests(baseURL!);
  await signIn(page, institution.owner);
  await page.goto("/");
  await expect(page.getByText(/^2 bookings wait for your answer/)).toBeVisible();

  await page.keyboard.press("Control+K");
  const palette = page.getByRole("dialog", { name: "Jump to" });
  const search = palette.getByRole("combobox", { name: "Jump to" });

  // An unknown number says so, and the palette stays open for another.
  await search.fill("BK-2026-999999");
  await search.press("Enter");
  await expect(palette.getByText("This institution has no booking BK-2026-999999.")).toBeVisible();

  // Typed in lower case, as people type.
  await search.fill(later.toLowerCase());
  await expect(palette.getByRole("option")).toHaveText([new RegExp(`Open booking ${later}`)]);
  await shot(page, testInfo.project.name, "en-jump-booking");
  await search.press("Enter");

  await expect(palette).toHaveCount(0);
  await expect(page).toHaveURL(new RegExp(`/bookings/calendar\\?date=${colomboDate(8)}&booking=${later}$`));
  const marked = page.locator('article[aria-current="true"]');
  await expect(marked).toHaveCount(1);
  await expect(marked).toContainText(later);
  await expect(marked).toBeInViewport();
});

test("a donor's day names the next meal to give, and the places they give to @phone", async ({ page, baseURL }, testInfo) => {
  const institution = await approvedInstitution(baseURL!, `E2E Ledger Kovil ${Date.now()}`);
  const lunch = await slotOf(baseURL!, institution, { name: "Lunch" });
  const donor = await createAccount(baseURL!, "E2E Ledger Giver");
  const booked = await bookingBy(baseURL!, donor, lunch, colomboDate(9));

  await signIn(page, donor);
  await page.goto("/");

  // Still waiting for the institution's answer, nine days ahead.
  await expect(page.getByText(new RegExp(`^Your next meal, Lunch at ${institution.name} on .+, is still waiting for their answer\\.$`))).toBeVisible();
  await expect(panel(page, "Your table").getByRole("listitem")).toHaveCount(1);
  await expect(panel(page, "Your table")).toContainText(booked);
  await expect(panel(page, "Places you give to").getByRole("link", { name: institution.name })).toHaveAttribute("href", `/i/${institution.slug}`);
  await page.waitForLoadState("networkidle");
  expect(await widerThanWindow(page)).toEqual([]);
  expect(await scrollsSideways(page)).toBe(false);
  await shot(page, testInfo.project.name, "en-ledger-donor");
});

test("a platform admin's day: the review queue, the latest decisions and the institutions by status @phone", async ({ page, baseURL }, testInfo) => {
  const admin = await createAccount(baseURL!, "E2E Ledger Admin");
  grantPlatformRole(admin, "PLATFORM_ADMIN");
  await signIn(page, admin);
  await page.goto("/");

  await expect(page.getByText("Every institution and every request, in one place.")).toBeVisible();
  await expect(page.getByText(/^(\d+ registrations? waits? for a review; the oldest has waited|No registration is waiting for a review\.)/)).toBeVisible();
  await expect(panel(page, "Waiting for a review")).toBeVisible();
  await expect(panel(page, "Latest decisions")).toBeVisible();

  // Every status, in an institution's lifecycle order; the earlier tests approved some.
  const statuses = panel(page, "Institutions by status");
  await expect(statuses.getByRole("term")).toHaveText(["Onboarding", "Active", "Suspended", "Deactivated"]);
  await expect(statuses.getByRole("definition").nth(1)).not.toHaveText("0");
  await page.waitForLoadState("networkidle");
  expect(await scrollsSideways(page)).toBe(false);
  await shot(page, testInfo.project.name, "en-ledger-platform");
});
