import { mkdirSync } from "node:fs";
import path from "node:path";
import type { APIRequestContext, Browser, Page } from "@playwright/test";
import { apiAs, createAccount, signIn, type Account } from "../support/accounts";
import { field } from "../support/fields";
import { approvedInstitution, bookingBy, colomboDate, slotOf, unlist, type Institution } from "../support/institutions";
import { expect, scrollsSideways, test } from "../support/test";

/*  Plan 07, C.5: Phase C's screens. Booking every year and stopping it; sharing a booking and the
    link's join, decline and withdraw, seen by the donor, the co-sponsor and the institution; the
    memorial planner booking an observance through the search; the heart and My places; and two
    browsers on one institution's page, one booking while the other's calendar changes by itself.
    The rules behind each are the API's integration tests (RecurringBookingTests, CoSponsorTests,
    MemorialPlanTests, FavoritePlacesTests, LiveAvailabilityTests).                              */

function screenDir(): string {
  const dir = path.join(__dirname, "..", ".screens");
  mkdirSync(dir, { recursive: true });
  return dir;
}

async function personPage(browser: Browser, account: Account | null, locale: "" | "si" | "ta" = ""): Promise<Page> {
  const context = await browser.newContext();
  const page = await context.newPage();
  if (account) await signIn(page, account, locale);
  return page;
}

const day = (page: Page, date: string) => page.locator(`button[data-date="${date}"]`);

async function withApi<T>(baseURL: string, account: Account, run: (api: APIRequestContext) => Promise<T>): Promise<T> {
  const api = await apiAs(baseURL, account);
  try {
    return await run(api);
  } finally {
    await api.dispose();
  }
}

async function listed(baseURL: string, institution: Institution) {
  await withApi(baseURL, institution.owner, async (api) => {
    const response = await api.put("/api/institution/listing", { data: { listed: true } });
    expect(response.ok()).toBe(true);
  });
}

/** Makes an invitation on <bookingNo> through the share sheet; the link's path, /share/... */
async function makeLink(page: Page, bookingNo: string, name: string): Promise<string> {
  const sheet = page.getByRole("dialog");
  if (!(await sheet.isVisible())) {
    await page.locator("article").filter({ hasText: bookingNo }).getByRole("button", { name: "Share this dana" }).click();
  } else if (await sheet.getByRole("button", { name: "Invite someone else" }).isVisible()) {
    await sheet.getByRole("button", { name: "Invite someone else" }).click();
  }
  await field(sheet, "Their name").fill(name);
  await sheet.getByRole("button", { name: "Make a link" }).click();
  const link = await sheet.getByRole("textbox", { name: `The link for ${name}` }).inputValue();
  return new URL(link).pathname;
}

test("a donor books a meal every year, sees it under Every year, and stops it; this year's booking stays @phone", async ({ page, baseURL }) => {
  const institution = await approvedInstitution(baseURL!, `E2E Yearly Vihara ${Date.now()}`);
  const slot = await slotOf(baseURL!, institution, { name: "Dana" });
  const donor = await createAccount(baseURL!, "E2E Yearly Donor");
  const date = colomboDate(6);

  await signIn(page, donor);
  await page.goto(`/i/${institution.slug}?slot=${slot}&date=${date}`);
  const sheet = page.getByRole("dialog");
  await expect(sheet.getByRole("heading", { name: "Sponsor Dana" })).toBeVisible();
  const everyYear = sheet.getByRole("switch", { name: "Book this every year" });
  await expect(everyYear).toHaveAttribute("aria-checked", "false");
  await everyYear.click();
  await expect(everyYear).toHaveAttribute("aria-checked", "true");
  await sheet.getByRole("button", { name: "Send request" }).click();
  await expect(sheet.getByText("Request sent")).toBeVisible();
  const bookingNo = (await sheet.getByText(/^BK-\d{4}-\d{6}$/).textContent())!;
  await sheet.getByRole("link", { name: "See my bookings" }).click();

  const booking = page.locator("article").filter({ hasText: bookingNo });
  await expect(booking.getByText("Booked every year")).toBeVisible();
  const yearly = page.getByRole("region", { name: "Every year" });
  await expect(yearly).toBeVisible();
  const card = yearly.locator("article").filter({ hasText: "Dana" });
  // Next year's day, as the yearly job will book it.
  await expect(card.getByText(/next on/)).toContainText(String(Number(date.slice(0, 4)) + 1));
  expect(await scrollsSideways(page)).toBe(false);

  await card.getByRole("button", { name: "Stop booking every year" }).click();
  const stop = page.getByRole("dialog");
  await expect(stop.getByRole("heading", { name: "Stop booking this every year?" })).toBeVisible();
  await stop.getByRole("button", { name: "Stop", exact: true }).click();
  await expect(page.getByText("It will not be booked again.").first()).toBeVisible();
  await expect(page.getByRole("region", { name: "Every year" })).toHaveCount(0);
  await expect(page.locator("article").filter({ hasText: bookingNo }).getByText("Waiting for their reply. We will text you.")).toBeVisible();
  await expect(page.locator("article").filter({ hasText: bookingNo }).getByText("Booked every year")).toHaveCount(0);
});

test("a donor shares a dana: one joins after signing in, one declines, one is withdrawn; each side sees it @phone", async ({ page, baseURL, browser }) => {
  const institution = await approvedInstitution(baseURL!, `E2E Shared Kovil ${Date.now()}`);
  const slot = await slotOf(baseURL!, institution);
  const donor = await createAccount(baseURL!, "E2E Sharing Donor");
  const sister = await createAccount(baseURL!, "E2E Sister");
  const friend = await createAccount(baseURL!, "E2E Friend");
  const bookingNo = await bookingBy(baseURL!, donor, slot, colomboDate(8));

  await signIn(page, donor);
  await page.goto("/bookings/mine");
  await page.locator("article").filter({ hasText: bookingNo }).getByRole("button", { name: "Share this dana" }).click();
  const sheet = page.getByRole("dialog");
  await expect(sheet.getByText("Nobody yet.")).toBeVisible();
  // A name is asked for before a link is made.
  await sheet.getByRole("button", { name: "Make a link" }).click();
  await expect(sheet.getByText("Please enter their name.")).toBeVisible();

  const sisterLink = await makeLink(page, bookingNo, "Nangi");
  await expect(sheet.getByText("Copy it now: for safety we show each link only once.", { exact: false })).toBeVisible();
  expect(await scrollsSideways(page)).toBe(false);
  const friendLink = await makeLink(page, bookingNo, "Sunil");
  const withdrawnLink = await makeLink(page, bookingNo, "Not coming");
  const row = (name: string) => sheet.getByRole("listitem").filter({ hasText: name });
  await expect(row("Nangi").getByText("Invited", { exact: true })).toBeVisible();

  await row("Not coming").getByRole("button", { name: "Withdraw" }).click();
  await expect(page.getByText("Not coming's invitation is withdrawn.").first()).toBeVisible();
  await expect(row("Not coming")).toHaveCount(0);

  // The sister opens the link signed out: the summary, and a sign-in that comes back here.
  const sisterPage = await personPage(browser, null);
  await sisterPage.goto(sisterLink);
  await expect(sisterPage.getByText("E2E Sharing Donor invites you to give with them")).toBeVisible();
  await expect(sisterPage.getByRole("link", { name: institution.name })).toBeVisible();
  await expect(sisterPage.getByText("Sign in to answer.", { exact: false })).toBeVisible();
  expect(await scrollsSideways(sisterPage)).toBe(false);
  // The card's own sign-in, which comes back to the link (the header has one too).
  await sisterPage.getByRole("link", { name: "Sign in" }).and(sisterPage.locator('a[href*="next=%2Fshare%2F"]')).click();
  await expect(sisterPage).toHaveURL(/\/login\?next=/);
  await signIn(sisterPage, sister);
  await sisterPage.goto(sisterLink);
  await sisterPage.getByRole("button", { name: "Join", exact: true }).click();
  await expect(sisterPage.getByText("You have joined this dana.")).toBeVisible();
  await sisterPage.getByRole("link", { name: "See my bookings" }).click();
  const shared = sisterPage.getByRole("region", { name: "Shared with me" });
  await expect(shared.getByText("With E2E Sharing Donor")).toBeVisible();
  await expect(shared.getByRole("link", { name: institution.name })).toBeVisible();
  // Once joined, the link says so to anyone.
  await sisterPage.goto(sisterLink);
  await expect(sisterPage.getByText("This invitation has already been accepted.")).toBeVisible();

  const friendPage = await personPage(browser, friend);
  await friendPage.goto(friendLink);
  await friendPage.getByRole("button", { name: "Decline" }).click();
  await expect(friendPage.getByText("You have declined.", { exact: false })).toBeVisible();

  // A withdrawn link opens nothing; the donor's own link is not theirs to join.
  await friendPage.goto(withdrawnLink);
  await expect(friendPage.getByText("This link is not valid, or the invitation was withdrawn.")).toBeVisible();
  await page.goto("/bookings/mine");
  const ownLink = await makeLink(page, bookingNo, "Myself");
  await page.goto(ownLink);
  await page.getByRole("button", { name: "Join", exact: true }).click();
  await expect(page.getByText("This is your own booking: you do not need to join it.")).toBeVisible();

  // The donor's sheet shows each answer.
  await page.goto("/bookings/mine");
  await page.locator("article").filter({ hasText: bookingNo }).getByRole("button", { name: "Share this dana" }).click();
  await expect(row("Nangi").getByText("Joined", { exact: true })).toBeVisible();
  await expect(row("Sunil").getByText("Declined", { exact: true })).toBeVisible();
  await expect(row("Nangi").getByRole("button", { name: "Withdraw" })).toHaveCount(0);

  // The institution sees the name of the one who joined, and no other.
  const ownerPage = await personPage(browser, institution.owner);
  await ownerPage.goto(`/bookings/calendar?date=${colomboDate(8)}&booking=${bookingNo}`);
  const item = ownerPage.locator("article").filter({ hasText: bookingNo }).last();
  await expect(item.getByText("Shared with: Nangi")).toBeVisible();
  await expect(item.getByText("Sunil")).toHaveCount(0);

  for (const p of [sisterPage, friendPage, ownerPage]) await p.context().close();
});

test("a memorial plan shows its days; Find a place books the 7th day, and the plan shows it booked @phone", async ({ page, baseURL }) => {
  const name = `E2E Memorial Temple ${Date.now()}`;
  const institution = await approvedInstitution(baseURL!, name);
  await slotOf(baseURL!, institution, { name: "Dana" });
  await listed(baseURL!, institution);
  const donor = await createAccount(baseURL!, "E2E Memorial Donor");
  // The 7th day is the day of passing + 6 (H24): three days from now.
  const passed = colomboDate(-3);
  const seventh = colomboDate(3);

  try {
    await signIn(page, donor);
    await page.goto("/memorial");
    await expect(page.getByRole("heading", { name: "Memorial dana planner", level: 1 })).toBeVisible();
    await expect(page.getByText("No one yet.", { exact: false })).toBeVisible();
    await page.getByRole("button", { name: "Remember someone" }).click();
    const form = page.getByRole("dialog");
    await form.getByRole("button", { name: "Save" }).click();
    await expect(form.getByText("Please enter their name.")).toBeVisible();
    await expect(form.getByText("Please enter the date of passing.")).toBeVisible();
    await field(form, "Their name").fill("Seeya Appuhamy");
    await field(form, "Who they were to you (optional)").selectOption({ index: 1 });
    await field(form, "Date of passing").fill(passed);
    await field(form, "Their faith").selectOption({ label: "Buddhism" });
    await expect(form.getByText("DRAFT - 7th day almsgiving")).toBeVisible();
    await form.getByRole("button", { name: "Save" }).click();
    await expect(form).toBeHidden();

    const plan = page.locator("article").filter({ hasText: "Seeya Appuhamy" });
    await expect(plan.getByText("DRAFT - 3 months almsgiving")).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);
    await plan.getByRole("link", { name: /^Find a place for DRAFT - 7th day almsgiving/ }).click();
    await expect(page).toHaveURL(new RegExp(`/find\\?date=${seventh}&plan=[0-9a-f-]+&rule=BUDDHIST_7TH_DAY`));

    // The search keeps the observance while the district narrows it; Book carries it to the sheet.
    await page.goto(`${new URL(page.url()).pathname}${new URL(page.url()).search}&district=AMP`);
    await page.getByRole("link", { name: `Book Dana at ${name}` }).click();
    const sheet = page.getByRole("dialog");
    await expect(sheet.getByText("This booking is for a memorial day in your planner.")).toBeVisible();
    await sheet.getByRole("button", { name: "Send request" }).click();
    await expect(sheet.getByText("Request sent")).toBeVisible();
    await sheet.getByRole("link", { name: "See my memorial plans" }).click();

    const booked = page.locator("article").filter({ hasText: "Seeya Appuhamy" }).locator("li").filter({ hasText: "DRAFT - 7th day almsgiving" });
    await expect(booked.getByText("Booked", { exact: false })).toBeVisible();
    await expect(booked.getByRole("link", { name })).toBeVisible();
    await expect(booked.getByRole("link", { name: /^Find a place/ })).toHaveCount(0);
  } finally {
    await unlist(baseURL!, institution);
  }
});

test("the heart keeps a place in My places on the home page, and lets it go @phone", async ({ page, baseURL, browser }) => {
  const institution = await approvedInstitution(baseURL!, `E2E Heart Church ${Date.now()}`);
  await slotOf(baseURL!, institution);
  const donor = await createAccount(baseURL!, "E2E Heart Donor");

  // A visitor sees no heart.
  const visitor = await personPage(browser, null);
  await visitor.goto(`/i/${institution.slug}`);
  await expect(visitor.getByRole("heading", { name: institution.name, level: 1 })).toBeVisible();
  await expect(visitor.getByRole("button", { name: "Keep this place" })).toHaveCount(0);
  await visitor.context().close();

  await signIn(page, donor);
  await page.goto("/");
  await expect(page.getByText("Tap the heart on a place's page to keep it here.")).toBeVisible();

  await page.goto(`/i/${institution.slug}`);
  await page.getByRole("button", { name: "Keep this place" }).click();
  await expect(page.getByRole("button", { name: "In my places" })).toHaveAttribute("aria-pressed", "true");
  expect(await scrollsSideways(page)).toBe(false);
  await page.reload();
  await expect(page.getByRole("button", { name: "In my places" })).toHaveAttribute("aria-pressed", "true");

  await page.goto("/");
  await expect(page.getByRole("link", { name: new RegExp(institution.name) })).toBeVisible();
  await page.getByRole("link", { name: new RegExp(institution.name) }).first().click();
  await page.getByRole("button", { name: "In my places" }).click();
  await expect(page.getByRole("button", { name: "Keep this place" })).toHaveAttribute("aria-pressed", "false");
  await page.goto("/");
  await expect(page.getByText("Tap the heart on a place's page to keep it here.")).toBeVisible();
});

test("two browsers on one place's page: one books a day, the other's calendar shows it taken without a reload", async ({ page, baseURL, browser }) => {
  const institution = await approvedInstitution(baseURL!, `E2E Live Mosque ${Date.now()}`);
  const slot = await slotOf(baseURL!, institution);
  const donor = await createAccount(baseURL!, "E2E Live Donor");
  const date = colomboDate(10);

  // The watcher: anyone, not signed in, looking at the month.
  const watcher = await personPage(browser, null);
  await watcher.goto(`/i/${institution.slug}?slot=${slot}&date=${date}`);
  // The address's day opens its sheet; close it and watch the calendar.
  await watcher.keyboard.press("Escape");
  await expect(day(watcher, date)).not.toHaveAttribute("aria-disabled", "true");
  await watcher.evaluate(() => ((window as unknown as { __notReloaded: boolean }).__notReloaded = true));

  // The donor books that day in their own browser.
  await signIn(page, donor);
  await page.goto(`/i/${institution.slug}?slot=${slot}&date=${date}`);
  const sheet = page.getByRole("dialog");
  await sheet.getByRole("button", { name: "Send request" }).click();
  await expect(sheet.getByText("Request sent")).toBeVisible();

  // The hint goes through the outbox (H10), woken right after the commit.
  await expect(day(watcher, date)).toHaveAttribute("aria-disabled", "true", { timeout: 20_000 });
  await expect(day(watcher, date)).toHaveAttribute("aria-label", /Full$/);
  expect(await watcher.evaluate(() => (window as unknown as { __notReloaded?: boolean }).__notReloaded)).toBe(true);
  await watcher.context().close();
});

for (const locale of ["si", "ta"] as const) {
  test(`the planner, sharing and the link read in ${locale} @phone`, async ({ page, baseURL, browser }, testInfo) => {
    const institution = await approvedInstitution(baseURL!, `E2E Lang Place ${locale} ${Date.now()}`);
    const slot = await slotOf(baseURL!, institution);
    const donor = await createAccount(baseURL!, `E2E Lang Donor ${locale}`, locale);
    const bookingNo = await bookingBy(baseURL!, donor, slot, colomboDate(7));
    const shot = (screen: string) => path.join(screenDir(), `${testInfo.project.name}-${locale}-${screen}.png`);
    const t = {
      si: { share: "මෙම දානය බෙදා ගන්න", make: "සබැඳියක් සාදන්න", name: "ඔවුන්ගේ නම", planner: "අනුස්මරණ දාන සැලසුම", add: "කෙනෙකු සිහිපත් කරන්න", join: "එක්වන්න", heart: "මෙම ස්ථානය තබා ගන්න" },
      ta: { share: "இந்த தானத்தைப் பகிரவும்", make: "இணைப்பை உருவாக்கு", name: "அவர்களின் பெயர்", planner: "நினைவு தான திட்டம்", add: "ஒருவரை நினைவுகூர", join: "சேர்", heart: "இந்த இடத்தை வைத்துக்கொள்" },
    }[locale];

    await signIn(page, donor, locale);
    await page.goto(`/${locale}/memorial`);
    await expect(page.getByRole("heading", { name: t.planner, level: 1 })).toBeVisible();
    await page.getByRole("button", { name: t.add }).click();
    await expect(page.getByRole("dialog")).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);
    await page.screenshot({ path: shot("memorial-form") });
    await page.keyboard.press("Escape");

    await page.goto(`/${locale}/bookings/mine`);
    await page.locator("article").filter({ hasText: bookingNo }).getByRole("button", { name: t.share }).click();
    const sheet = page.getByRole("dialog");
    // field() knows only the English "(required)"; the name is the sheet's first box.
    await sheet.getByRole("textbox", { name: new RegExp(`^${t.name}`) }).fill("Nangi");
    await sheet.getByRole("button", { name: t.make }).click();
    const made = sheet.locator("input[readonly]");
    await expect(made).toHaveValue(/\/share\//);
    const link = new URL(await made.inputValue()).pathname;
    expect(link.startsWith(`/${locale}/share/`)).toBe(true);
    expect(await scrollsSideways(page)).toBe(false);
    await page.screenshot({ path: shot("share-sheet") });

    const other = await personPage(browser, await createAccount(baseURL!, `E2E Lang Joiner ${locale}`, locale), locale);
    await other.goto(link);
    await expect(other.getByRole("button", { name: t.join, exact: true })).toBeVisible();
    expect(await scrollsSideways(other)).toBe(false);
    await other.screenshot({ path: shot("share-link"), fullPage: true });

    await other.goto(`/${locale}/i/${institution.slug}`);
    await expect(other.getByRole("button", { name: t.heart })).toBeVisible();
    await other.screenshot({ path: shot("heart") });
    await other.context().close();
  });
}
