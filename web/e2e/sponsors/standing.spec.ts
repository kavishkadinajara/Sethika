import { mkdirSync } from "node:fs";
import path from "node:path";
import type { Page } from "@playwright/test";
import { apiAs, createAccount, newPhone, signIn } from "../support/accounts";
import { field } from "../support/fields";
import { approvedInstitution, colomboDate, slotOf, unlist, type Institution } from "../support/institutions";
import { expect, scrollsSideways, test, widerThanWindow } from "../support/test";

/*  Plan 08, K.4: the screens of standing sponsors - the donor book's arrangements with their next
    days (set up with a preview of clashes, skip, swap), the calendar's regular donors and its tray of
    asked-for days, a held day on /i/[slug], My bookings' requests, and (K24, K36) a person the donor
    book knows signing up and letting one of their regular days go. The rules are the API's integration
    tests (StandingSponsorshipTests, StandingChangesTests, StandingSponsorLinkTests); these prove the screens use
    the API, say what happened in words, and fit a 320 px phone.                                    */

function shot(name: string): string {
  const dir = path.join(__dirname, "..", ".screens");
  mkdirSync(dir, { recursive: true });
  return path.join(dir, `standing-${name}.png`);
}

const dayOfMonth = (date: string) => String(Number(date.slice(8, 10)));

/** A person in the institution's donor book, through the API; their publicId. */
async function donorOf(baseURL: string, institution: Institution, fullName: string, phone = newPhone()): Promise<string> {
  const api = await apiAs(baseURL, institution.owner);
  try {
    const response = await api.post("/api/institution/donors", { data: { fullName, phone, notes: null, language: "en", textConsent: false } });
    expect(response.status(), await response.text()).toBe(201);
    return ((await response.json()) as { publicId: string }).publicId;
  } finally {
    await api.dispose();
  }
}

/** A one-day standing sponsorship on <date>, booked at once; its publicId. */
async function onceOf(baseURL: string, institution: Institution, donor: string, mealSlot: string, date: string): Promise<string> {
  const api = await apiAs(baseURL, institution.owner);
  try {
    const response = await api.post("/api/institution/sponsorships", {
      data: { donor, mealSlot, pattern: "Once", startDate: date, headcount: 20, fulfilment: "InstitutionPrepares", inMemoryOf: null, notes: null },
    });
    expect(response.status(), await response.text()).toBe(201);
    const created = (await response.json()) as { publicId: string; days: { outcome: string }[] };
    expect(created.days.map((d) => d.outcome)).toEqual(["booked"]);
    return created.publicId;
  } finally {
    await api.dispose();
  }
}

async function openBook(page: Page) {
  await page.goto("/bookings/sponsors");
  await expect(page.getByRole("heading", { name: "Regular donors", level: 1 })).toBeVisible();
}

test("an owner sets up a monthly sponsor with two days, sees the clash first, and lets one day go @phone", async ({ page, baseURL }, testInfo) => {
  const institution = await approvedInstitution(baseURL!, `E2E Standing Vihara ${Date.now()}`);
  try {
    const slot = await slotOf(baseURL!, institution);
    const first = colomboDate(5);
    const second = colomboDate(9);
    // Another regular donor already holds the first day: the preview says so before saving.
    await onceOf(baseURL!, institution, await donorOf(baseURL!, institution, "E2E Kamala Holder"), slot, first);
    await donorOf(baseURL!, institution, "E2E Sunil Monthly");

    await signIn(page, institution.owner);
    await openBook(page);
    await page.getByRole("button", { name: "Set up regular days for E2E Sunil Monthly" }).click();
    const form = page.getByRole("dialog");
    await expect(form.getByRole("heading", { name: "Regular days for E2E Sunil Monthly" })).toBeVisible();

    // ---- saying what is missing: a monthly pattern needs its days
    await form.locator("label").filter({ hasText: /^Every month$/ }).click();
    await expect(form.getByRole("radio", { name: "Every month" })).toBeChecked();
    await expect(form.getByText("Choose the days to see them here.")).toBeVisible();
    await form.getByRole("button", { name: "Set up", exact: true }).click();
    await expect(form.getByText("Choose at least one day of the month.")).toBeVisible();

    // ---- two days a month, and the preview of the next days
    const days = form.getByRole("group", { name: "On these days of the month" });
    await days.getByRole("button", { name: dayOfMonth(first), exact: true }).click();
    await days.getByRole("button", { name: dayOfMonth(second), exact: true }).click();
    await expect(form.getByText("Choose at least one day of the month.")).toBeHidden();
    const preview = form.getByRole("region", { name: "Next days" });
    await expect(preview.getByText("Held by another regular donor")).toHaveCount(1);
    await expect(preview.getByText("Free").first()).toBeVisible();
    await expect(preview.getByText("Booked when it opens").first()).toBeVisible();
    await page.screenshot({ path: shot(`form-${testInfo.project.name}`), fullPage: true });

    await field(form, "People to feed").fill("25");
    await form.getByRole("button", { name: "Set up", exact: true }).click();
    await expect(form.getByRole("status")).toHaveText("1 day booked");
    await expect(form.getByText("Not booked:")).toBeVisible();
    await expect(form.getByText("Full", { exact: true })).toBeVisible();
    await expect(form.getByText(/more days? (is|are) being booked now/)).toBeVisible();
    await form.getByRole("button", { name: "Close", exact: true }).first().click();
    await expect(form).toBeHidden();

    // ---- the card lists the arrangement; opened, its next days
    const card = page.locator("article").filter({ hasText: "E2E Sunil Monthly" });
    await expect(card.getByText(`on days ${[dayOfMonth(first), dayOfMonth(second)].sort((a, b) => Number(a) - Number(b)).join(", ")}`, { exact: false })).toBeVisible();
    await card.getByRole("button", { name: /Lunch/ }).click();
    const sheet = page.getByRole("dialog");
    await expect(sheet.getByRole("heading", { name: "E2E Sunil Monthly's regular days" })).toBeVisible();
    await expect(sheet.getByText("Booked", { exact: true }).first()).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);
    await page.screenshot({ path: shot(`days-${testInfo.project.name}`), fullPage: true });

    // ---- the sponsor cannot give on their booked day
    // The night's job may still be booking the window's later days: follow the one day chosen.
    const booked = sheet.locator("li[data-state=booked]").first();
    const date = await booked.getAttribute("data-date");
    await booked.getByRole("button", { name: /: cannot give$/ }).click();
    await expect(sheet.getByRole("heading", { name: /^Cannot give on / })).toBeVisible();
    await field(sheet, "Reason (optional)").fill("Away in Kandy");
    await sheet.getByRole("button", { name: "Let the day go" }).click();
    await expect(page.getByText(/ is let go\.$/).first()).toBeVisible();
    await expect(sheet.locator("li[data-state=Skipped]").getByText("Away in Kandy")).toBeVisible();
    await expect(sheet.locator(`li[data-date="${date}"]`)).toHaveAttribute("data-state", "Skipped");
  } finally {
    await unlist(baseURL!, institution);
  }
});

test("two regular donors swap days this time, and the calendar marks their days", async ({ page, baseURL }) => {
  const institution = await approvedInstitution(baseURL!, `E2E Standing Swap ${Date.now()}`);
  try {
    const slot = await slotOf(baseURL!, institution);
    const a = colomboDate(6);
    const b = colomboDate(8);
    await onceOf(baseURL!, institution, await donorOf(baseURL!, institution, "E2E Anura Swap"), slot, a);
    await onceOf(baseURL!, institution, await donorOf(baseURL!, institution, "E2E Bimali Swap"), slot, b);

    await signIn(page, institution.owner);
    await openBook(page);
    await page.locator("article").filter({ hasText: "E2E Anura Swap" }).getByRole("button", { name: /Lunch/ }).click();
    const sheet = page.getByRole("dialog");
    await sheet.getByRole("button", { name: "Swap a day" }).click();
    await expect(sheet.getByText("This time only")).toBeVisible();
    await field(sheet, "E2E Anura Swap's day").selectOption({ index: 1 });
    await expect(field(sheet, "With")).toContainText("E2E Bimali Swap");
    await field(sheet, "Their day").selectOption({ index: 1 });
    await sheet.getByRole("button", { name: "Swap", exact: true }).click();
    await expect(page.getByText("Swapped.").first()).toBeVisible();
    await expect(sheet.locator("li[data-state=MovedTo]")).toHaveCount(1);
    await sheet.getByRole("button", { name: "Close", exact: true }).first().click();
    await expect(sheet).toBeHidden();

    // ---- the calendar: a ringed dot on regular donors' days, and the badge on their bookings
    await page.goto(`/bookings/calendar?date=${b}`);
    await expect(page.getByText("A regular donor's day")).toBeVisible();
    const day = page.locator("article").filter({ hasText: "E2E Anura Swap" });
    await expect(day.getByText("Regular donor", { exact: true })).toBeVisible();
  } finally {
    await unlist(baseURL!, institution);
  }
});

test("a donor asks for a held day, and the institution gives it to them @phone", async ({ page, baseURL, browser }, testInfo) => {
  const institution = await approvedInstitution(baseURL!, `E2E Standing Ask ${Date.now()}`);
  const asker = await createAccount(baseURL!, "E2E Asker Donor");
  try {
    const slot = await slotOf(baseURL!, institution);
    const day = colomboDate(7);
    await onceOf(baseURL!, institution, await donorOf(baseURL!, institution, "E2E Regular Holder"), slot, day);

    // ---- /i/[slug]: the held day offers to ask, and asking says what happens next
    await signIn(page, asker);
    await page.goto(`/i/${institution.slug}?slot=${slot}&date=${day}`);
    const sheet = page.getByRole("dialog");
    await expect(sheet.getByText("A regular donor gives on this day.", { exact: false })).toBeVisible();
    await field(sheet, "People you would feed").fill("500");
    await sheet.getByRole("button", { name: "Ask for this day" }).click();
    await expect(sheet.getByText("Enter a number between 5 and 50.")).toBeVisible();
    await field(sheet, "People you would feed").fill("12");
    await field(sheet, "A note for the institution (optional)").fill("My father's first death anniversary");
    await sheet.getByRole("button", { name: "Ask for this day" }).click();
    await expect(sheet.getByRole("status")).toHaveText(/^Asked: DR-\d{4}-\d{6}$/);
    expect(await scrollsSideways(page)).toBe(false);
    await page.screenshot({ path: shot(`asked-${testInfo.project.name}`) });

    // Asking again while it is open is refused in words.
    await page.goto(`/i/${institution.slug}?slot=${slot}&date=${day}`);
    await field(sheet, "People you would feed").fill("12");
    await sheet.getByRole("button", { name: "Ask for this day" }).click();
    await expect(sheet.getByText("You have already asked for this day.", { exact: false })).toBeVisible();

    await page.goto("/bookings/mine");
    const requests = page.getByRole("region", { name: "Days I asked for" });
    await expect(requests.getByText("Waiting for their answer")).toBeVisible();

    // ---- the institution's tray: give the day
    const owner = await (await browser.newContext()).newPage();
    await signIn(owner, institution.owner);
    await owner.goto("/bookings/calendar");
    const tray = owner.getByRole("region", { name: "Asked for a regular donor's day" });
    const ask = tray.getByRole("article", { name: "E2E Asker Donor asks for this day" });
    await expect(ask.getByText("Held by E2E Regular Holder")).toBeVisible();
    await expect(ask.getByText("My father's first death anniversary")).toBeVisible();
    await ask.getByRole("button", { name: "Give them the day" }).click();
    await expect(owner.getByText(/the day is E2E Asker Donor's now/).first()).toBeVisible();
    await expect(tray).toHaveCount(0);

    // ---- the donor sees it given, and booked
    await page.reload();
    await expect(requests.getByText("Given to you")).toBeVisible();
    await expect(requests.getByText(/^Booked as BK-/)).toBeVisible();
    await page.screenshot({ path: shot(`mine-${testInfo.project.name}`), fullPage: true });
  } finally {
    await unlist(baseURL!, institution);
  }
});

test("a person the donor book knows signs up, sees their regular days, and lets one go @phone", async ({ page, baseURL }, testInfo) => {
  const institution = await approvedInstitution(baseURL!, `E2E Standing Own ${Date.now()}`);
  try {
    const slot = await slotOf(baseURL!, institution);
    const first = colomboDate(4);
    const second = colomboDate(6);

    // The institution knows the person before they use the app (K14): their days are booked with no account.
    const phone = newPhone();
    const donor = await donorOf(baseURL!, institution, "E2E Own Regular", phone);
    const firstSponsorship = await onceOf(baseURL!, institution, donor, slot, first);
    await onceOf(baseURL!, institution, donor, slot, second);

    // Signing up with that phone links them, and the days become theirs (K36).
    const account = await createAccount(baseURL!, "E2E Own Regular", "en", phone);
    await signIn(page, account);
    await page.goto("/bookings/mine");
    const upcoming = page.getByRole("listitem").filter({ hasText: "A regular day the institution set up for you" });
    await expect(upcoming).toHaveCount(2);
    const card = upcoming.first();

    // A regular day is let go, not cancelled (K24).
    await expect(card.getByRole("button", { name: "Cancel booking" })).toHaveCount(0);
    await card.getByRole("button", { name: "I can't give that day" }).click();
    const sheet = page.getByRole("dialog");
    await expect(sheet.getByText("Only this day is let go", { exact: false })).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);
    await page.screenshot({ path: shot(`skip-${testInfo.project.name}`) });
    await field(sheet, "Reason (optional)").fill("Away in Kandy");
    await sheet.getByRole("button", { name: "I can't give that day" }).click();
    await expect(page.getByText(/^BK-\S+ is let go\. We will text you to confirm\.$/).first()).toBeVisible();

    // It is past and cancelled with their reason; the other day stays theirs, with its button.
    await expect(page.getByText("Cancelled: Away in Kandy")).toBeVisible();
    await expect(page.getByRole("button", { name: "I can't give that day" })).toHaveCount(1);
    await page.screenshot({ path: shot(`skipped-${testInfo.project.name}`), fullPage: true });

    // The institution sees the day let go, not merely cancelled: it is never booked again (K4).
    const staff = await apiAs(baseURL!, institution.owner);
    try {
      const details = (await (await staff.get(`/api/institution/sponsorships/${firstSponsorship}`)).json()) as { days: { date: string; state: string; reason: string | null }[] };
      expect(details.days.find((d) => d.date === first)).toMatchObject({ state: "Skipped", reason: "Away in Kandy" });
    } finally {
      await staff.dispose();
    }

    // The same in Sinhala and Tamil, on a phone too: through the language menu, which saves the
    // choice to the account (an address alone is moved back to the account's language, A11).
    for (const [locale, name, label] of [
      ["si", "සිංහල", "මට එදින දිය නොහැක"],
      ["ta", "தமிழ்", "அந்த நாளில் என்னால் வழங்க முடியாது"],
    ] as const) {
      await page.getByRole("banner").getByRole("button", { name: /^(Language|භාෂාව|மொழி)$/ }).click();
      await page.getByRole("menuitem", { name }).click();
      await expect(page.locator("html")).toHaveAttribute("lang", locale);
      await page.waitForLoadState("networkidle");
      await expect(page.getByRole("button", { name: label })).toHaveCount(1);
      await page.getByRole("button", { name: label }).click();
      await expect(page.getByRole("dialog")).toBeVisible();
      expect(await widerThanWindow(page)).toEqual([]);
      await page.screenshot({ path: shot(`skip-${locale}-${testInfo.project.name}`) });
      await page.keyboard.press("Escape");
    }
  } finally {
    await unlist(baseURL!, institution);
  }
});

test("the arrangement form speaks Sinhala and Tamil @phone", async ({ page, baseURL }, testInfo) => {
  for (const locale of ["si", "ta"] as const) {
    const institution = await approvedInstitution(baseURL!, `E2E Standing ${locale} ${Date.now()}`, locale);
    try {
      await slotOf(baseURL!, institution);
      await donorOf(baseURL!, institution, "E2E Lang Donor");
      await signIn(page, institution.owner, locale);
      await page.goto(`/${locale}/bookings/sponsors`);
      const setUp = locale === "si" ? "E2E Lang Donor සඳහා නිත්‍ය දින සකසන්න" : "E2E Lang Donor க்கு வழக்கமான நாட்களை அமைக்கவும்";
      await page.getByRole("button", { name: setUp }).click();
      const form = page.getByRole("dialog");
      const weekly = locale === "si" ? "සෑම සතියකම" : "ஒவ்வொரு வாரமும்";
      await form.locator("label").filter({ hasText: new RegExp(`^${weekly}$`) }).click();
      await expect(form.getByRole("radio", { name: weekly })).toBeChecked();
      await form.getByRole("group", { name: locale === "si" ? "සතියේ මෙම දිනවල" : "வாரத்தின் இந்த நாட்களில்" }).getByRole("button").nth(3).click();
      await expect(form.getByRole("region").locator("li").first()).toBeVisible();
      expect(await widerThanWindow(page)).toEqual([]);
      await page.screenshot({ path: shot(`${locale}-${testInfo.project.name}`), fullPage: true });
      await page.keyboard.press("Escape");
    } finally {
      await unlist(baseURL!, institution);
    }
  }
});
