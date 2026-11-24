import { mkdirSync } from "node:fs";
import path from "node:path";
import type { Browser, Page } from "@playwright/test";
import { createAccount, signIn, type Account } from "../support/accounts";
import { field } from "../support/fields";
import { approvedInstitution, bookingBy, colomboDate, slotOf, unlist, type Institution } from "../support/institutions";
import { clipsSideways, expect, scrollsSideways, test } from "../support/test";

/*  Plan 03, 5.7: booking a meal, from both sides. Each test approves its own institution through
    the API (support/institutions.ts) and makes its own donors, so the tests run in parallel and in
    any order. The rules behind each refusal are the API's integration tests; these prove the
    screens show them, in words, and that a person can get through.                              */

function screenDir(): string {
  const dir = path.join(__dirname, "..", ".screens");
  mkdirSync(dir, { recursive: true });
  return dir;
}

/** A second person, in their own browser, signed in. */
async function personPage(browser: Browser, account: Account, locale: "" | "si" | "ta" = ""): Promise<Page> {
  const context = await browser.newContext();
  const page = await context.newPage();
  await signIn(page, account, locale);
  return page;
}

const openDays = (page: Page) => page.locator("button[data-date]:not([aria-disabled])");
const day = (page: Page, date: string) => page.locator(`button[data-date="${date}"]`);

/** The institution's page, open on <date>'s month for its first meal. */
async function openOn(page: Page, institution: Institution, slot: string, date: string, locale = "") {
  await page.goto(`${locale ? `/${locale}` : ""}/i/${institution.slug}?slot=${slot}&date=${date}`);
}

test("an owner adds a meal and lists the place; a donor finds it and books; the owner accepts; the donor sees it confirmed @phone", async ({ page, baseURL, browser }) => {
  const name = `E2E Kovil ${Date.now()}`;
  const institution = await approvedInstitution(baseURL!, name);
  const donor = await createAccount(baseURL!, "E2E Donor Ama");

  try {
    // The owner: an empty slots screen, a meal added through the form, the place listed.
    await signIn(page, institution.owner);
    await page.goto("/bookings/slots");
    await expect(page.getByRole("heading", { name: "Meals and days", level: 1 })).toBeVisible();
    await expect(page.getByText("Add a meal first; then you can be listed.")).toBeVisible();

    await page.getByRole("button", { name: "Add a meal" }).click();
    const form = page.getByRole("dialog");
    await form.getByRole("button", { name: "Add meal" }).click();
    await expect(form.getByText("Enter a name (up to 100 characters).")).toBeVisible();
    await field(form, "Name").fill("Lunch");
    await field(form, "Most people").fill("40");
    await field(form, "Cost a person (Rs, optional)").fill("450");
    await form.getByRole("button", { name: "Add meal" }).click();
    await expect(form).toBeHidden();
    await expect(page.getByRole("heading", { name: "Lunch" })).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);

    await page.getByRole("switch").click();
    await expect(page.getByRole("switch")).toHaveAttribute("aria-checked", "true");
    await expect(page.getByText("Donors browsing Sethika can find you.").first()).toBeVisible();

    // The donor finds it on the search page, in its district tomorrow, and books its first open day.
    const donorPage = await personPage(browser, donor);
    await donorPage.goto(`/find?district=AMP&date=${colomboDate(1)}`);
    await donorPage.getByRole("link", { name, exact: true }).click();
    await expect(donorPage.getByRole("heading", { name, level: 1 })).toBeVisible();
    await expect(donorPage.getByRole("radio", { name: /Lunch/ })).toHaveAttribute("aria-checked", "true");
    expect(await scrollsSideways(donorPage)).toBe(false);

    await openDays(donorPage).first().click();
    const sheet = donorPage.getByRole("dialog");
    await expect(sheet.getByRole("heading", { name: "Sponsor Lunch" })).toBeVisible();
    await sheet.getByRole("button", { name: "One more" }).click();
    await expect(sheet.getByText(/Estimated cost: about/)).toBeVisible();
    await field(sheet, "In memory of (optional)").fill("Amma");
    await sheet.getByRole("button", { name: "Send request" }).click();
    await expect(sheet.getByText("Request sent")).toBeVisible();
    const bookingNo = (await sheet.getByText(/^BK-\d{4}-\d{6}$/).textContent())!;
    await sheet.getByRole("button", { name: "Close" }).first().click();

    // The owner's tray holds the request; accepting it texts the donor.
    await page.goto("/bookings/calendar");
    const request = page.locator("article").filter({ hasText: bookingNo });
    await expect(request.getByText("E2E Donor Ama")).toBeVisible();
    await expect(request.getByText("In memory of Amma")).toBeVisible();
    await request.getByRole("button", { name: "Accept", exact: true }).click();
    await expect(page.getByText(`${bookingNo} accepted. The donor has been texted.`).first()).toBeVisible();
    await expect(page.getByText("No requests are waiting. All answered.")).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);

    await donorPage.goto("/bookings/mine");
    const mine = donorPage.locator("article").filter({ hasText: bookingNo });
    await expect(mine.getByText("Confirmed", { exact: true })).toBeVisible();
    await expect(mine.getByText("In memory of Amma")).toBeVisible();
    expect(await scrollsSideways(donorPage)).toBe(false);
    await donorPage.context().close();
  } finally {
    await unlist(baseURL!, institution);
  }
});

test("an exclusive day booked by one donor shows as full to the next", async ({ page, baseURL }) => {
  const institution = await approvedInstitution(baseURL!, `E2E Full Church ${Date.now()}`);
  const slot = await slotOf(baseURL!, institution);
  const date = colomboDate(12);
  await bookingBy(baseURL!, await createAccount(baseURL!, "E2E First Donor"), slot, date);

  const second = await createAccount(baseURL!, "E2E Second Donor");
  await signIn(page, second);
  await openOn(page, institution, slot, date);

  await expect(day(page, date)).toHaveAttribute("aria-disabled", "true");
  await expect(day(page, date)).toHaveAttribute("aria-label", /Full$/);
  await expect(page.getByRole("dialog")).toHaveCount(0);
  // The next day is still open.
  await expect(day(page, colomboDate(13))).not.toHaveAttribute("aria-disabled", "true");
});

test("a request turned down with a reason; the donor reads the reason", async ({ page, baseURL, browser }) => {
  const institution = await approvedInstitution(baseURL!, `E2E Reject Mosque ${Date.now()}`);
  const slot = await slotOf(baseURL!, institution);
  const donor = await createAccount(baseURL!, "E2E Turned Down");
  const bookingNo = await bookingBy(baseURL!, donor, slot, colomboDate(9));

  await signIn(page, institution.owner);
  await page.goto("/bookings/calendar");
  const request = page.locator("article").filter({ hasText: bookingNo });
  await request.getByRole("button", { name: "Do not accept" }).click();
  // A reason is asked for before it can be sent.
  await expect(request.getByRole("button", { name: "Do not accept" })).toBeDisabled();
  await field(request, "Why not? The donor will read this.").fill("The hall is being repaired that week.");
  await request.getByRole("button", { name: "Do not accept" }).click();
  await expect(page.getByText(`${bookingNo} not accepted. The donor has been texted.`).first()).toBeVisible();

  const donorPage = await personPage(browser, donor);
  await donorPage.goto("/bookings/mine");
  const booking = donorPage.locator("article").filter({ hasText: bookingNo });
  await expect(booking.getByText("Not accepted", { exact: true })).toBeVisible();
  await expect(booking.getByText("Not accepted: The hall is being repaired that week.")).toBeVisible();
  await expect(booking.getByRole("button", { name: "Cancel booking" })).toHaveCount(0);
  await donorPage.context().close();
});

test("a donor cancels and the day is open again @phone", async ({ page, baseURL }) => {
  const institution = await approvedInstitution(baseURL!, `E2E Cancel Home ${Date.now()}`);
  const slot = await slotOf(baseURL!, institution);
  const date = colomboDate(15);
  const donor = await createAccount(baseURL!, "E2E Cancelling Donor");
  const bookingNo = await bookingBy(baseURL!, donor, slot, date);

  await signIn(page, donor);
  await page.goto("/bookings/mine");
  const booking = page.locator("article").filter({ hasText: bookingNo });
  await expect(booking.getByText("Waiting for reply")).toBeVisible();
  await booking.getByRole("button", { name: "Cancel booking" }).click();

  const sheet = page.getByRole("dialog");
  await expect(sheet.getByRole("heading", { name: "Cancel this booking?" })).toBeVisible();
  await field(sheet, "Reason (optional)").fill("Travelling that week");
  await sheet.getByRole("button", { name: "Cancel booking" }).click();
  await expect(page.getByText(`${bookingNo} is cancelled.`).first()).toBeVisible();
  await expect(booking.getByText("Cancelled", { exact: true })).toBeVisible();
  await expect(booking.getByText("Cancelled: Travelling that week")).toBeVisible();
  expect(await scrollsSideways(page)).toBe(false);

  await openOn(page, institution, slot, date);
  await expect(day(page, date)).not.toHaveAttribute("aria-disabled", "true");
  await expect(day(page, date)).toHaveAttribute("aria-label", /Open$/);
});

test("a day the owner closes cannot be booked, and says why", async ({ page, baseURL, browser }) => {
  const institution = await approvedInstitution(baseURL!, `E2E Closed Temple ${Date.now()}`);
  const slot = await slotOf(baseURL!, institution);
  const date = colomboDate(20);

  await signIn(page, institution.owner);
  await page.goto("/bookings/slots");
  await page.getByRole("button", { name: "Close some days" }).click();
  const sheet = page.getByRole("dialog");
  await field(sheet, "From").fill(date);
  await field(sheet, "To").fill(date);
  await field(sheet, "Reason").fill("Annual festival");
  await sheet.getByRole("button", { name: "Close these days" }).click();
  await expect(sheet).toBeHidden();
  await expect(page.getByText(/All meals · Annual festival/)).toBeVisible();

  const visitor = await (await browser.newContext()).newPage();
  await openOn(visitor, institution, slot, date);
  await expect(day(visitor, date)).toHaveAttribute("aria-disabled", "true");
  await expect(day(visitor, date)).toHaveAttribute("aria-label", /Closed this day$/);
  await visitor.context().close();
});

test("a visitor who chooses a day is asked to sign in, and comes back to the same day", async ({ page, baseURL }) => {
  const institution = await approvedInstitution(baseURL!, `E2E Visitor Kovil ${Date.now()}`);
  const slot = await slotOf(baseURL!, institution, { requiresApproval: false });
  const date = colomboDate(11);
  const donor = await createAccount(baseURL!, "E2E Visitor");

  await openOn(page, institution, slot, date);
  const sheet = page.getByRole("dialog");
  await expect(sheet.getByText(/Sign in to book this day/)).toBeVisible();
  await sheet.getByRole("link", { name: "Sign in" }).click();

  await page.locator('main form input:not([type="password"]):not([type="hidden"])').first().fill(donor.phone);
  await page.locator('input[type="password"]').fill("E2e-Sethika-1");
  await page.locator('button[type="submit"]').click();

  await page.waitForURL(new RegExp(`/i/${institution.slug}`));
  await expect(page.getByRole("dialog").getByRole("heading", { name: "Sponsor Lunch" })).toBeVisible();
  // This meal needs no approval: booked, not requested.
  await page.getByRole("dialog").getByRole("button", { name: "Book this day" }).click();
  await expect(page.getByRole("dialog").getByText("Booked", { exact: true })).toBeVisible();
});

test("the booking screens in Sinhala and Tamil, nothing cut off at 320 px @phone", async ({ page, baseURL, browser }, info) => {
  const institution = await approvedInstitution(baseURL!, `E2E Languages Vihara ${Date.now()}`, "si");
  const slot = await slotOf(baseURL!, institution, { bookingMode: "Shared", maxSponsors: 4 });
  const date = colomboDate(14);
  const donor = await createAccount(baseURL!, "E2E Tamil Donor", "ta");
  await bookingBy(baseURL!, donor, slot, date);
  const shot = (locale: string, screen: string) =>
    path.join(screenDir(), `${info.project.name}-${locale}-booking-${screen}.png`);

  for (const locale of ["si", "ta"] as const) {
    // Without a date in the address, so the page opens on its month and no sheet yet.
    const visitor = await (await browser.newContext()).newPage();
    await visitor.goto(`/${locale}/i/${institution.slug}?slot=${slot}`);
    await expect(openDays(visitor).first()).toBeVisible();
    // The month in the page's own script: Chromium has no Sinhala Intl data, so this would read
    // "October 2026" if the names came from the browser.
    const script = locale === "si" ? /^\d{4} [\u0D80-\u0DFF]/u : /[\u0B80-\u0BFF]/u;
    await expect(visitor.getByRole("heading", { level: 3 })).toHaveText(script);
    expect(await scrollsSideways(visitor), `${locale} institution page`).toBe(false);
    expect(await clipsSideways(visitor), `${locale} institution page`).toEqual([]);
    await visitor.screenshot({ path: shot(locale, "institution"), fullPage: true });

    await openDays(visitor).first().click();
    await expect(visitor.getByRole("dialog")).toBeVisible();
    await visitor.screenshot({ path: shot(locale, "sheet") });
    await visitor.context().close();
  }

  // The owner works in Sinhala, the donor in Tamil: each lands in their own language.
  await signIn(page, institution.owner, "si");
  await page.goto("/si/bookings/calendar");
  await expect(page.locator("article").first()).toBeVisible();
  expect(await scrollsSideways(page), "si calendar").toBe(false);
  expect(await clipsSideways(page), "si calendar").toEqual([]);
  await page.screenshot({ path: shot("si", "calendar"), fullPage: true });

  await page.goto("/si/bookings/slots");
  await expect(page.getByRole("switch")).toBeVisible();
  expect(await scrollsSideways(page), "si slots").toBe(false);
  expect(await clipsSideways(page), "si slots").toEqual([]);
  await page.screenshot({ path: shot("si", "slots"), fullPage: true });

  const donorPage = await personPage(browser, donor, "ta");
  await donorPage.goto("/ta/bookings/mine");
  await expect(donorPage.locator("article").first()).toBeVisible();
  expect(await scrollsSideways(donorPage), "ta my bookings").toBe(false);
  expect(await clipsSideways(donorPage), "ta my bookings").toEqual([]);
  await donorPage.screenshot({ path: shot("ta", "mine"), fullPage: true });
  await donorPage.context().close();
});
