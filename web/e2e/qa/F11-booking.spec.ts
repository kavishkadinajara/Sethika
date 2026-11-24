import si from "../../messages/si.json";
import ta from "../../messages/ta.json";
import { signIn } from "../support/accounts";
import { sql } from "../support/sql";
import { expect, scrollsSideways, test, widerThanWindow } from "../support/test";
import { anonymous, throwaway } from "./support/api";
import {
  BLOCKS,
  BOOK,
  bookAs,
  booked,
  cleanUp,
  colomboDate,
  decide,
  donorCancel,
  internalIdKeys,
  makeSlot,
  moveSlotDay,
  removeBlocks,
  Sessions,
  slotDay,
  slotName,
  statusOf,
} from "./support/booking";
import { account, answer, evidence, persona, tenant } from "./support/world";

/*  F11 - Donor booking and My bookings (P1). Cases: docs/qa/cases/F11-booking.md. D1 (Sinhala, web),
    D2 (Tamil), D5 and throwaway donors book T1's "QA F11 ..." meals, made by M1 for the case; each
    case cancels what it booked and switches its meals off. The plan's "regular-day refusal" needs
    a standing sponsorship linked to an account and is run in F18 (QA-F11-08).                 */

const t1 = () => tenant("T1");
const day = (page: import("@playwright/test").Page, date: string) => page.locator(`button[data-date="${date}"]`);

/** Texts to <phone> after text <after> that name <bookingNo>: "dedup key|body". */
function textsAbout(phone: string, after: number, bookingNo: string): string[] {
  return sql(
    "SELECT CONCAT(DedupKey, '|', Body) FROM notify.Notifications WHERE Channel = 'SMS' AND Id > $(after) AND RIGHT(Recipient, 9) = '$(last9)' AND (DedupKey LIKE '%$(no)%' OR Body LIKE '%$(no)%') ORDER BY Id;",
    { after: String(after), last9: phone.slice(-9), no: bookingNo },
  );
}
const lastText = () => Number(sql("SELECT ISNULL(MAX(Id), 0) FROM notify.Notifications;")[0]);

/** How many active bookings <phone> holds on <slot>'s <date>. */
function heldBy(phone: string, slot: string, date: string): number {
  return Number(
    sql(
      "SELECT COUNT(*) FROM booking.Bookings b JOIN booking.SlotInstances si ON si.Id = b.SlotInstanceId JOIN booking.MealSlots s ON s.Id = si.MealSlotId WHERE s.PublicId = '$(slot)' AND si.SlotDate = '$(date)' AND RIGHT(b.DonorPhone, 9) = '$(last9)' AND b.Status IN ('Requested', 'Approved');",
      { slot, date, last9: phone.slice(-9) },
    )[0],
  );
}

test("QA-F11-01 D1 books a meal on T1's page in Sinhala, sees it in My bookings, and is texted at once @phone", async ({ page }, testInfo) => {
  const sessions = new Sessions();
  const made: string[] = [];
  try {
    const slot = await makeSlot(await sessions.of("M1"), { name: slotName("F11 Screen lunch"), nameSi: "QA F11 දිවා දානය" });
    made.push(slot.publicId);
    const date = colomboDate(4);
    const before = lastText();

    await signIn(page, account("D1"), "si");
    await page.goto(`/si/i/${t1().slug}?slot=${slot.publicId}&date=${date}`);
    const sheet = page.getByRole("dialog");
    await expect(sheet.getByRole("heading", { name: si.Discover.sheet.title.replace("{meal}", "QA F11 දිවා දානය") })).toBeVisible();
    await sheet.getByRole("button", { name: si.Discover.sheet.more }).click();
    await sheet.getByLabel(new RegExp(`^${si.Discover.sheet.inMemoryOf.replace(/[()]/g, ".")}`)).fill("QA අම්මා");
    expect(await scrollsSideways(page)).toBe(false);
    await sheet.getByRole("button", { name: si.Discover.sheet.request }).click();
    await expect(sheet.getByText(si.Discover.sheet.doneRequested)).toBeVisible();
    const bookingNo = (await sheet.getByText(/^BK-\d{4}-\d{6}$/).textContent())!;
    await evidence(page, "QA-F11-01", testInfo, "sheet");

    expect(statusOf(bookingNo)).toBe("Requested");
    expect(slotDay(slot.publicId, date)).toMatchObject({ booked: 1, max: 1 });
    // The request text goes at once (bookings wake the outbox), once.
    await expect.poll(() => textsAbout(persona("D1").phone, before, bookingNo).length, { timeout: 20_000 }).toBe(1);
    expect(textsAbout(persona("D1").phone, before, bookingNo)[0]).toMatch(/^[^|]*BOOKING_REQUESTED/i);

    await page.goto("/si/bookings/mine");
    const mine = page.locator("article").filter({ hasText: bookingNo });
    await expect(mine.getByText(si.Booking.status.Requested, { exact: true })).toBeVisible();
    await expect(mine.getByText(si.MyBookings.inMemoryOf.replace("{name}", "QA අම්මා"))).toBeVisible();
    await expect(mine.getByRole("button", { name: si.MyBookings.cancel })).toBeVisible();
    await evidence(page, "QA-F11-01", testInfo, "mine");
    expect(await widerThanWindow(page), "My bookings: nothing past the window").toEqual([]);
  } finally {
    await cleanUp(sessions, "M1", made);
    await sessions.dispose();
  }
});

test("QA-F11-02 the last place, taken by two donors at the same moment: exactly one gets it, and the counter never passes the cap", async () => {
  const sessions = new Sessions();
  const made: string[] = [];
  try {
    const m1 = await sessions.of("M1");
    const shared = await makeSlot(m1, { name: slotName("F11 Race shared"), bookingMode: "Shared", maxSponsors: 2 });
    const exclusive = await makeSlot(m1, { name: slotName("F11 Race exclusive"), startTime: "18:00", endTime: "19:00" });
    made.push(shared.publicId, exclusive.publicId);
    const [d1, d2, d5] = [await sessions.of("D1"), await sessions.of("D2"), await sessions.of("D5")];

    // Three days each: one place left on a shared meal (D5 took the other), and a whole exclusive one.
    for (const offset of [12, 13, 14]) {
      const date = colomboDate(offset);
      await booked(d5, shared.publicId, date);
      for (const slot of [shared.publicId, exclusive.publicId]) {
        const results = await Promise.all([bookAs(d1, slot, date), bookAs(d2, slot, date)]);
        const statuses = results.map((r) => r.status).sort();
        expect(statuses, `${slot} ${date}`).toEqual([201, 409]);
        expect(results.find((r) => r.status === 409)!.body?.code).toBe("slot_full");
        const counters = slotDay(slot, date)!;
        expect(counters.booked, `${slot} ${date}`).toBe(counters.max);
      }
    }

    // Six donors at once on a four-place day: four in, two told it is full (E6: the lock queues them).
    const four = await makeSlot(m1, { name: slotName("F11 Race four"), bookingMode: "Shared", maxSponsors: 4, startTime: "06:00", endTime: "07:00" });
    made.push(four.publicId);
    const date = colomboDate(15);
    const extra = await Promise.all([throwaway("f11-race-a"), throwaway("f11-race-b"), throwaway("f11-race-c")]);
    const donors = [d1, d2, d5, ...(await Promise.all(extra.map((a) => sessions.of(a))))];
    const results = await Promise.all(donors.map((api) => bookAs(api, four.publicId, date)));
    expect(results.filter((r) => r.status === 201)).toHaveLength(4);
    expect(results.filter((r) => r.status !== 201).map((r) => r.body?.code)).toEqual(["slot_full", "slot_full"]);
    expect(slotDay(four.publicId, date)).toMatchObject({ booked: 4, max: 4, headcount: 40 });
  } finally {
    await cleanUp(sessions, "M1", made);
    await sessions.dispose();
  }
});

test("QA-F11-03 which days can be booked: past, today, tomorrow, the horizon and one past it, a day not served, a closed day, a bad date", async ({ page }) => {
  const sessions = new Sessions();
  const made: string[] = [];
  try {
    const m1 = await sessions.of("M1");
    // Served on every day but the weekday of +5; 1 to 30 days ahead.
    const notServed = colomboDate(5);
    const weekday = new Date(`${notServed}T00:00:00Z`).getUTCDay();
    const slot = await makeSlot(m1, { name: slotName("F11 Dates"), bookingMode: "Shared", maxSponsors: 20, maxAdvanceDays: 30, daysOfWeekMask: 127 - (1 << weekday) });
    const sameDay = await makeSlot(m1, { name: slotName("F11 Same day"), startTime: "00:00", endTime: "00:30", minAdvanceDays: 0, maxAdvanceDays: 10 });
    made.push(slot.publicId, sameDay.publicId);
    const closed = colomboDate(8);
    expect((await m1.post(BLOCKS, { data: { dateFrom: closed, dateTo: closed, mealSlot: slot.publicId, reason: "QA F11 closed" } })).status()).toBe(201);

    const d1 = await sessions.of("D1");
    const table: [string, string, string, number, string | null][] = [
      ["yesterday", slot.publicId, colomboDate(-1), 409, "too_soon"],
      ["today, 1 day ahead needed", slot.publicId, colomboDate(0), 409, "too_soon"],
      ["tomorrow", slot.publicId, colomboDate(1), 201, null],
      ["the 30th day", slot.publicId, colomboDate(30), 201, null],
      ["the 31st day", slot.publicId, colomboDate(31), 409, "too_far"],
      ["a weekday not served", slot.publicId, notServed, 409, "not_on_this_day"],
      ["a closed day", slot.publicId, closed, 409, "date_blocked"],
      ["same-day meal after it started (00:00)", sameDay.publicId, colomboDate(0), 409, "too_soon"],
      ["same-day meal tomorrow", sameDay.publicId, colomboDate(1), 201, null],
    ];
    for (const [label, mealSlot, date, status, code] of table) {
      const r = await bookAs(d1, mealSlot, date);
      expect([r.status, r.body?.code ?? null], label).toEqual([status, code]);
    }
    for (const date of [`${colomboDate(3).slice(0, 8)}32`, "2026-02-30", "22/11/2026", "", "tomorrow"]) {
      expect(await bookAs(d1, slot.publicId, date), date).toMatchObject({ status: 400, body: { code: "invalid_details", fields: ["date"] } });
    }

    // The calendar shows the same: yesterday, the 31st day, the weekday and the closed day cannot be chosen.
    await signIn(page, account("D1"), "si");
    for (const [date, open] of [[colomboDate(-1), false], [colomboDate(2), true], [notServed, false], [closed, false], [colomboDate(31), false]] as const) {
      await page.goto(`/si/i/${t1().slug}?slot=${slot.publicId}&date=${date}`);
      await page.keyboard.press("Escape");
      const cell = day(page, date);
      if (open) await expect(cell, date).not.toHaveAttribute("aria-disabled", "true");
      else await expect(cell, date).toHaveAttribute("aria-disabled", "true");
    }
  } finally {
    await cleanUp(sessions, "M1", made);
    await removeBlocks(sessions, "M1", "QA F11");
    await sessions.dispose();
  }
});

test("QA-F11-04 what the donor sends is checked: people, how the meal is given, long texts, an unknown meal, who may book, and the hourly limit", async ({ page }, testInfo) => {
  const sessions = new Sessions();
  const made: string[] = [];
  try {
    const m1 = await sessions.of("M1");
    const slot = await makeSlot(m1, { name: slotName("F11 Details"), bookingMode: "Shared", maxSponsors: 20, minHeadcount: 5, maxHeadcount: 50, allowedFulfilment: ["InstitutionPrepares"] });
    made.push(slot.publicId);
    const d1 = await sessions.of("D1");
    const date = colomboDate(16);
    const ok = { fulfilment: "InstitutionPrepares" };
    const table: [string, Record<string, unknown>, number, string, string[]?][] = [
      ["4 people (min 5)", { ...ok, headcount: 4 }, 400, "headcount_out_of_range"],
      ["51 people (max 50)", { ...ok, headcount: 51 }, 400, "headcount_out_of_range"],
      ["no headcount", { ...ok, headcount: null }, 400, "invalid_details", ["headcount"]],
      ["donor brings (not offered)", { headcount: 10, fulfilment: "DonorBrings" }, 400, "fulfilment_not_offered"],
      ["no fulfilment", { headcount: 10, fulfilment: null }, 400, "invalid_details", ["fulfilment"]],
      ["in memory of 201", { ...ok, inMemoryOf: "M".repeat(201) }, 400, "invalid_details", ["inMemoryOf"]],
      ["notes 501", { ...ok, notes: "N".repeat(501) }, 400, "invalid_details", ["notes"]],
    ];
    for (const [label, overrides, status, code, fields] of table) {
      const r = await bookAs(d1, slot.publicId, date, overrides);
      expect([r.status, r.body?.code], label).toEqual([status, code]);
      if (fields) expect(r.body?.fields, label).toEqual(fields);
    }
    expect(await bookAs(d1, "0190aaaa-0000-7000-8000-000000000000", date, ok)).toMatchObject({ status: 404, body: { code: "slot_not_found" } });
    expect(await bookAs(d1, "not-a-guid", date, ok)).toMatchObject({ status: 400 });
    // The edges themselves go through.
    // By a throwaway: the longest texts stay on the donor's list for good, and D1's must stay readable.
    const edge = await throwaway("f11-edges");
    expect((await bookAs(await sessions.of(edge), slot.publicId, date, { ...ok, headcount: 5, inMemoryOf: "M".repeat(200), notes: "N".repeat(500) })).status).toBe(201);
    expect((await bookAs(await sessions.of("D2"), slot.publicId, colomboDate(19), { ...ok, headcount: 50 })).status).toBe(201);

    // Who may book: no one signed out; a staff session acts for T1, not as a donor (plan 02, B1).
    const visitor = await anonymous();
    try {
      expect((await visitor.post(BOOK, { data: { mealSlot: slot.publicId, date, headcount: 10, ...ok } })).status()).toBe(401);
    } finally {
      await visitor.dispose();
    }
    expect((await bookAs(m1, slot.publicId, colomboDate(17), ok)).status).toBe(403);

    // E26: 20 bookings an hour per account; the 21st is refused before it takes a place.
    const busy = await sessions.of(await throwaway("f11-hourly"));
    for (let i = 0; i < 20; i++) expect((await bookAs(busy, slot.publicId, colomboDate(20 + i), ok)).status, `booking ${i + 1}`).toBe(201);
    const over = await bookAs(busy, slot.publicId, colomboDate(40), ok);
    expect(over.status).toBe(429);
    expect(slotDay(slot.publicId, colomboDate(40))).toBeNull();

    // The longest "in memory of" the rule allows, as one word, on the donor's list at phone width.
    await page.setViewportSize({ width: 390, height: 844 });
    await signIn(page, edge, "si");
    await page.goto("/si/bookings/mine");
    await expect(page.getByText("M".repeat(200)).first()).toBeAttached();
    await evidence(page, "QA-F11-04", testInfo, "long-in-memory-390");
    expect(await widerThanWindow(page), "a 200-character in-memory line stays inside the page at 390 px").toEqual([]);

    // And on the institution's calendar, where M1 reads it.
    const staff = await (await page.context().browser()!.newContext({ viewport: { width: 390, height: 844 } })).newPage();
    await signIn(staff, account("M1"), "si");
    await staff.goto(`/si/bookings/calendar?date=${date}`);
    await expect(staff.getByText("M".repeat(200)).first()).toBeAttached();
    await evidence(staff, "QA-F11-04", testInfo, "long-in-memory-calendar-390");
    expect(await widerThanWindow(staff), "the calendar at 390 px with a 200-character in-memory line").toEqual([]);
    await staff.context().close();
  } finally {
    await cleanUp(sessions, "M1", made);
    await sessions.dispose();
  }
});

test("QA-F11-05 a double tap books once: on the sheet, and the same donor twice at once through the API", async ({ page }) => {
  const sessions = new Sessions();
  const made: string[] = [];
  try {
    const m1 = await sessions.of("M1");
    const shared = await makeSlot(m1, { name: slotName("F11 Double shared"), bookingMode: "Shared", maxSponsors: 4 });
    const exclusive = await makeSlot(m1, { name: slotName("F11 Double exclusive"), startTime: "18:00", endTime: "19:00" });
    made.push(shared.publicId, exclusive.publicId);
    const date = colomboDate(18);

    await signIn(page, account("D1"), "si");
    await page.goto(`/si/i/${t1().slug}?slot=${shared.publicId}&date=${date}`);
    const sheet = page.getByRole("dialog");
    await sheet.getByRole("button", { name: si.Discover.sheet.request }).dblclick();
    await expect(sheet.getByText(si.Discover.sheet.doneRequested)).toBeVisible();
    await expect(sheet.getByText(si.Booking.errors.already_booked)).toHaveCount(0);
    expect(heldBy(persona("D1").phone, shared.publicId, date)).toBe(1);
    expect(slotDay(shared.publicId, date)?.booked).toBe(1);

    // Through the API at once: a shared meal answers already_booked, an exclusive one slot_full (E25).
    const d2 = await sessions.of("D2");
    const twiceShared = await Promise.all([bookAs(d2, shared.publicId, date), bookAs(d2, shared.publicId, date)]);
    expect(twiceShared.map((r) => [r.status, r.body?.code ?? null]).sort()).toEqual([[201, null], [409, "already_booked"]]);
    const twiceExclusive = await Promise.all([bookAs(d2, exclusive.publicId, date), bookAs(d2, exclusive.publicId, date)]);
    expect(twiceExclusive.map((r) => [r.status, r.body?.code ?? null]).sort()).toEqual([[201, null], [409, "slot_full"]]);
    expect(heldBy(persona("D2").phone, shared.publicId, date)).toBe(1);
    expect(slotDay(shared.publicId, date)?.booked).toBe(2);
    expect(slotDay(exclusive.publicId, date)?.booked).toBe(1);
  } finally {
    await cleanUp(sessions, "M1", made);
    await sessions.dispose();
  }
});

test("QA-F11-06 cancelling: the donor's own, until the day before, once; the place comes back; no text back to them", async ({ page }) => {
  const sessions = new Sessions();
  const made: string[] = [];
  try {
    const m1 = await sessions.of("M1");
    const slot = await makeSlot(m1, { name: slotName("F11 Cancel"), bookingMode: "Shared", maxSponsors: 4 });
    made.push(slot.publicId);
    const [d1, d2] = [await sessions.of("D1"), await sessions.of("D2")];
    const date = colomboDate(9);
    const mine = await booked(d1, slot.publicId, date);
    const before = lastText();

    // Only D1 may cancel it, with a reason of at most 300 characters.
    expect(await donorCancel(d2, mine)).toMatchObject({ status: 404, body: { code: "booking_not_found" } });
    expect(await donorCancel(d1, mine, "R".repeat(301))).toMatchObject({ status: 400, body: { code: "reason_too_long" } });
    expect(statusOf(mine)).toBe("Requested");

    // On the Sinhala screen, with a reason.
    await signIn(page, account("D1"), "si");
    await page.goto("/si/bookings/mine");
    const card = page.locator("article").filter({ hasText: mine });
    await card.getByRole("button", { name: si.MyBookings.cancel }).click();
    const sheet = page.getByRole("dialog");
    await expect(sheet.getByRole("heading", { name: si.MyBookings.cancelSheet.title })).toBeVisible();
    await sheet.getByLabel(new RegExp(`^${si.MyBookings.cancelSheet.reason.replace(/[()]/g, ".")}`)).fill("QA ගමනක් යනවා");
    await sheet.getByRole("button", { name: si.MyBookings.cancelSheet.confirm }).click();
    await expect(page.getByText(si.MyBookings.cancelSheet.done.replace("{bookingNo}", mine)).first()).toBeVisible();
    await expect(card.getByText(si.Booking.status.Cancelled, { exact: true })).toBeVisible();
    expect(statusOf(mine)).toBe("Cancelled");
    expect(slotDay(slot.publicId, date)?.booked).toBe(0);
    expect(await donorCancel(d1, mine)).toMatchObject({ status: 409, body: { code: "invalid_transition" } });
    // E30: the donor did it on the screen, so no text tells them back.
    await new Promise((r) => setTimeout(r, 3000));
    expect(textsAbout(persona("D1").phone, before, mine)).toEqual([]);

    // A request turned down cannot be cancelled; one whose day has come cannot be cancelled by the donor.
    const rejected = await booked(d1, slot.publicId, colomboDate(10));
    expect((await decide(m1, rejected, "reject", "QA: the hall is closed.")).status).toBe(200);
    expect(await donorCancel(d1, rejected)).toMatchObject({ status: 409, body: { code: "invalid_transition" } });

    const tomorrowish = colomboDate(11);
    const dayCome = await booked(d1, slot.publicId, tomorrowish);
    moveSlotDay(slot.publicId, tomorrowish, colomboDate(0));
    expect(await donorCancel(d1, dayCome)).toMatchObject({ status: 409, body: { code: "cancel_window_closed" } });
    await page.reload();
    const today = page.locator("article").filter({ hasText: dayCome });
    await expect(today.getByText(si.Booking.status.Requested, { exact: true })).toBeVisible();
    await expect(today.getByRole("button", { name: si.MyBookings.cancel })).toHaveCount(0);
    // The day before is still open to cancel (Q1: until the end of the day before).
    const dayBefore = await booked(d1, slot.publicId, colomboDate(12));
    moveSlotDay(slot.publicId, colomboDate(12), colomboDate(1));
    expect((await donorCancel(d1, dayBefore)).status).toBe(200);
  } finally {
    await cleanUp(sessions, "M1", made);
    await sessions.dispose();
  }
});

test("QA-F11-07 My bookings shows a donor only their own, in their language, with no internal ids @phone", async ({ page }, testInfo) => {
  const sessions = new Sessions();
  const made: string[] = [];
  try {
    const slot = await makeSlot(await sessions.of("M1"), { name: slotName("F11 Mine"), bookingMode: "Shared", maxSponsors: 4, nameTa: "QA F11 மதிய தானம்" });
    made.push(slot.publicId);
    const date = colomboDate(19);
    const [d1, d2] = [await sessions.of("D1"), await sessions.of("D2")];
    const d1No = await booked(d1, slot.publicId, date);
    const d2No = await booked(d2, slot.publicId, date);

    const list = await answer(await d2.get(`${BOOK}/mine`));
    expect(list.status).toBe(200);
    const numbers = (list.body as unknown as { bookingNo: string }[]).map((b) => b.bookingNo);
    expect(numbers).toContain(d2No);
    expect(numbers).not.toContain(d1No);
    expect(internalIdKeys(list.body)).toEqual([]);

    await signIn(page, account("D2"), "ta");
    await page.goto("/ta/bookings/mine");
    await expect(page.getByRole("heading", { name: ta.MyBookings.title, level: 1 })).toBeVisible();
    const card = page.locator("article").filter({ hasText: d2No });
    await expect(card.getByText(ta.Booking.status.Requested, { exact: true })).toBeVisible();
    await expect(card.getByText("QA F11 மதிய தானம்")).toBeVisible();
    await expect(page.getByText(d1No)).toHaveCount(0);
    expect(await scrollsSideways(page)).toBe(false);
    await evidence(page, "QA-F11-07", testInfo);
  } finally {
    await cleanUp(sessions, "M1", made);
    await sessions.dispose();
  }
});
