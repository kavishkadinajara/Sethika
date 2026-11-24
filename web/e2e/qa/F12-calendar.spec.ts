import type { Page } from "@playwright/test";
import si from "../../messages/si.json";
import { signIn } from "../support/accounts";
import { sql } from "../support/sql";
import { expect, scrollsSideways, test } from "../support/test";
import { anonymous, bearer, mobileLogin, type MobileSession } from "./support/api";
import {
  BOOK,
  booked,
  CALENDAR,
  cleanUp,
  colomboDate,
  decide,
  makeSlot,
  moveSlotDay,
  Sessions,
  slotDay,
  slotName,
  statusOf,
} from "./support/booking";
import { account, answer, evidence, persona } from "./support/world";

/*  F12 - Booking calendar (staff) and live updates (P1). Cases: docs/qa/cases/F12-calendar.md.
    M1 (Manager, Sinhala) answers T1's requests; S1 (Staff) may only look; O1 is the colleague in
    the second browser. Donors D1, D2, D5 book "QA F12 ..." meals made for the case; every case
    cancels what is still open and switches its meals off.                                      */

const lastText = () => Number(sql("SELECT ISNULL(MAX(Id), 0) FROM notify.Notifications;")[0]);

/** Texts to <phone> after <after> whose dedup key starts with <template> and names <bookingNo>. */
function texts(phone: string, after: number, template: string, bookingNo: string): string[] {
  return sql(
    "SELECT DedupKey FROM notify.Notifications WHERE Channel = 'SMS' AND Id > $(after) AND RIGHT(Recipient, 9) = '$(last9)' AND DedupKey LIKE '$(template)%' AND DedupKey LIKE '%$(no)%';",
    { after: String(after), last9: phone.slice(-9), template, no: bookingNo },
  );
}

/** The calendar's card for <bookingNo>: in the tray, or in the chosen day's panel. */
const cardOf = (page: Page, bookingNo: string) => page.locator("article").filter({ hasText: bookingNo }).first();

test("QA-F12-01 M1 answers requests on the Sinhala calendar: accept texts the donor; not accepting needs a reason the donor reads @phone", async ({ page, browser }, testInfo) => {
  const sessions = new Sessions();
  const made: string[] = [];
  try {
    const slot = await makeSlot(await sessions.of("M1"), { name: slotName("F12 Answer"), bookingMode: "Shared", maxSponsors: 4 });
    made.push(slot.publicId);
    const d1 = await sessions.of("D1");
    const accepted = await booked(d1, slot.publicId, colomboDate(5), { inMemoryOf: "QA සීයා" });
    const refused = await booked(d1, slot.publicId, colomboDate(6));
    const before = lastText();

    await signIn(page, account("M1"), "si");
    await page.goto("/si/bookings/calendar");
    await expect(page.getByRole("heading", { name: si.Calendar.tray.heading })).toBeVisible();
    const first = cardOf(page, accepted);
    await expect(first.getByText(persona("D1").name)).toBeVisible();
    await expect(first.getByText(si.Calendar.booking.inMemoryOf.replace("{name}", "QA සීයා"))).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);
    await evidence(page, "QA-F12-01", testInfo, "tray");

    await first.getByRole("button", { name: si.Calendar.booking.approve, exact: true }).click();
    await expect(page.getByText(si.Calendar.booking.done.approve.replace("{bookingNo}", accepted)).first()).toBeVisible();
    expect(statusOf(accepted)).toBe("Approved");
    await expect.poll(() => texts(persona("D1").phone, before, "BOOKING_APPROVED", accepted).length, { timeout: 20_000 }).toBe(1);

    const second = cardOf(page, refused);
    await second.getByRole("button", { name: si.Calendar.booking.reject, exact: true }).click();
    await expect(second.getByRole("button", { name: si.Calendar.booking.reject, exact: true })).toBeDisabled();
    await second.getByLabel(si.Calendar.booking.rejectReason, { exact: false }).fill("QA: ශාලාව අලුත්වැඩියා කරනවා.");
    await second.getByRole("button", { name: si.Calendar.booking.reject, exact: true }).click();
    await expect(page.getByText(si.Calendar.booking.done.reject.replace("{bookingNo}", refused)).first()).toBeVisible();
    expect(statusOf(refused)).toBe("Rejected");
    expect(slotDay(slot.publicId, colomboDate(6))?.booked).toBe(0);
    await expect.poll(() => texts(persona("D1").phone, before, "BOOKING_REJECTED", refused).length, { timeout: 20_000 }).toBe(1);
    // The text says "sign in to see why" (E30); the reason is on the donor's screen.
    expect(sql("SELECT Body FROM notify.Notifications WHERE DedupKey LIKE '%$(no)' AND DedupKey LIKE 'BOOKING_REJECTED%';", { no: refused })[0]).not.toContain("ශාලාව");

    const donor = await (await browser.newContext()).newPage();
    await signIn(donor, account("D1"), "si");
    await donor.goto("/si/bookings/mine");
    const mine = donor.locator("article").filter({ hasText: refused });
    await expect(mine.getByText(si.MyBookings.rejectedBecause.replace("{reason}", "QA: ශාලාව අලුත්වැඩියා කරනවා."))).toBeVisible();
    await donor.context().close();
  } finally {
    await cleanUp(sessions, "M1", made);
    await sessions.dispose();
  }
});

test("QA-F12-02 a booking's states: each decision only from the state it belongs to, served and not-happened only from its day, places given back or kept", async ({ page }) => {
  const sessions = new Sessions();
  const made: string[] = [];
  try {
    const m1 = await sessions.of("M1");
    const slot = await makeSlot(m1, { name: slotName("F12 States"), bookingMode: "Shared", maxSponsors: 4 });
    made.push(slot.publicId);
    const [d1, d2, d5] = [await sessions.of("D1"), await sessions.of("D2"), await sessions.of("D5")];
    const date = colomboDate(7);
    const a = await booked(d1, slot.publicId, date);
    const b = await booked(d2, slot.publicId, date);
    const c = await booked(d5, slot.publicId, date);
    const step = async (no: string, decision: string, reason?: string) => {
      const r = await decide(m1, no, decision, reason);
      return [r.status, r.body?.code ?? r.body?.status];
    };

    expect(await step(a, "complete")).toEqual([409, "invalid_transition"]);
    expect(await step(a, "no-show")).toEqual([409, "invalid_transition"]);
    expect(await step(a, "reject")).toEqual([400, "reason_required"]);
    expect(await step(a, "reject", "  ")).toEqual([400, "reason_required"]);
    expect(await step(a, "reject", "R".repeat(301))).toEqual([400, "reason_too_long"]);
    expect(await step(a, "approve")).toEqual([200, "Approved"]);
    expect(await step(a, "approve")).toEqual([409, "invalid_transition"]);
    expect(await step(a, "reject", "QA late")).toEqual([409, "invalid_transition"]);
    // Before the day: neither served nor not-happened.
    expect(await step(a, "complete")).toEqual([409, "too_early"]);
    expect(await step(a, "no-show")).toEqual([409, "too_early"]);
    expect(await step(b, "approve")).toEqual([200, "Approved"]);
    expect(await step(b, "cancel")).toEqual([400, "reason_required"]);
    expect(await step(b, "cancel", "R".repeat(301))).toEqual([400, "reason_too_long"]);
    expect(await step("BK-2000-999999", "approve")).toEqual([404, "booking_not_found"]);
    expect(slotDay(slot.publicId, date)?.booked).toBe(3);

    // The day comes (moved in SQL: the product has no clock to turn).
    moveSlotDay(slot.publicId, date, colomboDate(0));
    const today = colomboDate(0);
    const before = Number(sql("SELECT ISNULL(MAX(Id), 0) FROM notify.Notifications;")[0]);

    // On the screen: today's panel offers served / not happened for an approved booking only.
    await signIn(page, account("M1"), "si");
    await page.goto(`/si/bookings/calendar?date=${today}`);
    const cardA = page.locator("article").filter({ hasText: a }).last();
    await expect(cardA.getByRole("button", { name: si.Calendar.booking.complete })).toBeVisible();
    await expect(cardA.getByRole("button", { name: si.Calendar.booking.noShow })).toBeVisible();
    const cardC = page.locator("article").filter({ hasText: c }).last();
    await expect(cardC.getByRole("button", { name: si.Calendar.booking.complete })).toHaveCount(0);
    await cardA.getByRole("button", { name: si.Calendar.booking.complete }).click();
    await expect(page.getByText(si.Calendar.booking.done.complete.replace("{bookingNo}", a)).first()).toBeVisible();
    expect(statusOf(a)).toBe("Completed");

    expect(await step(b, "no-show")).toEqual([200, "NoShow"]);
    // Served and not-happened keep the place (it was promised and taken); nothing moves on from them.
    expect(slotDay(slot.publicId, today)?.booked).toBe(3);
    for (const no of [a, b]) {
      expect(await step(no, "cancel", "QA after"), no).toEqual([409, "invalid_transition"]);
      expect(await step(no, "complete"), no).toEqual([409, "invalid_transition"]);
    }
    // The institution may still cancel a request on the day itself, with a reason; the place comes back and the donor is texted.
    expect(await step(c, "cancel", "QA: the kitchen is closed today.")).toEqual([200, "Cancelled"]);
    expect(slotDay(slot.publicId, today)?.booked).toBe(2);
    await expect.poll(() => texts(persona("D5").phone, before, "BOOKING_CANCELLED", c).length, { timeout: 20_000 }).toBe(1);
    // The history has every move.
    expect(sql("SELECT COUNT(*) FROM booking.BookingStatusHistory h JOIN booking.Bookings b ON b.Id = h.BookingId WHERE b.BookingNo = '$(no)';", { no: a })[0]).toBe("3");
  } finally {
    await cleanUp(sessions, "M1", made);
    await sessions.dispose();
  }
});

test("QA-F12-03 two staff decide the same booking at the same moment: one wins, the other is told, and the place is counted once", async () => {
  const sessions = new Sessions();
  const made: string[] = [];
  try {
    const [m1, o1] = [await sessions.of("M1"), await sessions.of("O1")];
    const slot = await makeSlot(m1, { name: slotName("F12 Race"), bookingMode: "Shared", maxSponsors: 4 });
    made.push(slot.publicId);
    const [d1, d2] = [await sessions.of("D1"), await sessions.of("D2")];

    for (const offset of [20, 21, 22]) {
      const date = colomboDate(offset);
      // Accept against not accept.
      const no = await booked(d1, slot.publicId, date);
      const [x, y] = await Promise.all([decide(m1, no, "approve"), decide(o1, no, "reject", "QA: race")]);
      expect([x.status, y.status].sort(), `${no} approve vs reject`).toEqual([200, 409]);
      expect(["stale", "invalid_transition"]).toContain([x, y].find((r) => r.status === 409)!.body?.code);
      const final = statusOf(no);
      expect(slotDay(slot.publicId, date)?.booked, `${no} ended ${final}`).toBe(final === "Approved" ? 1 : 0);

      // Two cancellations of one approved booking: the place comes back once (E7).
      const other = await booked(d2, slot.publicId, date);
      expect((await decide(m1, other, "approve")).status).toBe(200);
      const held = slotDay(slot.publicId, date)!.booked;
      const [p, q] = await Promise.all([decide(m1, other, "cancel", "QA: race 1"), decide(o1, other, "cancel", "QA: race 2")]);
      expect([p.status, q.status].sort(), `${other} cancel twice`).toEqual([200, 409]);
      expect(slotDay(slot.publicId, date)?.booked).toBe(held - 1);
      expect(sql("SELECT COUNT(*) FROM booking.BookingStatusHistory h JOIN booking.Bookings b ON b.Id = h.BookingId WHERE b.BookingNo = '$(no)' AND h.ToStatus = 'Cancelled';", { no: other })[0]).toBe("1");
    }
  } finally {
    await cleanUp(sessions, "M1", made);
    await sessions.dispose();
  }
});

test("QA-F12-04 role × action on the calendar: Owner and Manager decide, Staff only looks (screen and API), others are refused", async ({ page }) => {
  const sessions = new Sessions();
  const made: string[] = [];
  try {
    const m1 = await sessions.of("M1");
    const slot = await makeSlot(m1, { name: slotName("F12 Roles"), bookingMode: "Shared", maxSponsors: 4 });
    made.push(slot.publicId);
    const date = colomboDate(8);
    const requested = await booked(await sessions.of("D1"), slot.publicId, date);
    const approved = await booked(await sessions.of("D2"), slot.publicId, date);
    expect((await decide(m1, approved, "approve")).status).toBe(200);
    const range = `?from=${colomboDate(0)}&to=${colomboDate(30)}`;

    const reads = async (who: "O1" | "M1" | "S1" | "C1" | "L1" | "D1") => {
      const api = await sessions.of(who);
      return [
        (await api.get(`${CALENDAR}${range}`)).status(),
        (await api.get("/api/institution/today")).status(),
        (await api.get("/api/institution/ledger")).status(),
        (await api.get(`${CALENDAR}/${requested}`)).status(),
      ];
    };
    expect(await reads("O1")).toEqual([200, 200, 200, 200]);
    expect(await reads("M1")).toEqual([200, 200, 200, 200]);
    expect(await reads("S1")).toEqual([200, 200, 200, 200]);
    for (const who of ["C1", "L1", "D1"] as const) expect(await reads(who), who).toEqual([403, 403, 403, 403]);

    // S1 and the others try every decision: all refused, nothing changes.
    for (const who of ["S1", "C1", "L1", "D1"] as const) {
      const api = await sessions.of(who);
      const tries = [
        await decide(api, requested, "approve"),
        await decide(api, requested, "reject", "QA"),
        await decide(api, approved, "cancel", "QA"),
        await decide(api, approved, "complete"),
        await decide(api, approved, "no-show"),
      ];
      expect(tries.map((r) => r.status), who).toEqual([403, 403, 403, 403, 403]);
    }
    expect([statusOf(requested), statusOf(approved)]).toEqual(["Requested", "Approved"]);

    // S1's screen shows the request and the day, with no button to decide.
    await signIn(page, account("S1"), "si");
    await page.goto(`/si/bookings/calendar?date=${date}`);
    const card = cardOf(page, requested);
    await expect(card).toBeVisible();
    for (const name of [si.Calendar.booking.approve, si.Calendar.booking.reject, si.Calendar.booking.cancel, si.Calendar.booking.complete]) {
      await expect(page.getByRole("button", { name, exact: true }), name).toHaveCount(0);
    }
    expect(await scrollsSideways(page)).toBe(false);
  } finally {
    await cleanUp(sessions, "M1", made);
    await sessions.dispose();
  }
});

test("QA-F12-05 another tenant cannot see or decide T1's bookings by number; T1's staff cannot decide their own booking at another place (E8)", async () => {
  const sessions = new Sessions();
  const made: string[] = [];
  const madeT2: string[] = [];
  try {
    const m1 = await sessions.of("M1");
    const slot = await makeSlot(m1, { name: slotName("F12 Tenant"), bookingMode: "Shared", maxSponsors: 4 });
    made.push(slot.publicId);
    const date = colomboDate(9);
    const t1Booking = await booked(await sessions.of("D1"), slot.publicId, date);

    const o2 = await sessions.of("O2");
    expect(await answer(await o2.get(`${CALENDAR}/${t1Booking}`))).toMatchObject({ status: 404, body: { code: "booking_not_found" } });
    expect((await o2.get(`${CALENDAR}/${t1Booking}/shared-with`)).status()).toBe(404);
    for (const [decision, reason] of [["approve"], ["reject", "QA"], ["cancel", "QA"], ["complete"], ["no-show"]] as [string, string?][]) {
      expect(await decide(o2, t1Booking, decision, reason), decision).toMatchObject({ status: 404, body: { code: "booking_not_found" } });
    }
    const calendar = (await (await o2.get(`${CALENDAR}?from=${colomboDate(0)}&to=${colomboDate(30)}`)).json()) as { bookings: { bookingNo: string }[] }[];
    expect(calendar.flatMap((d) => d.bookings.map((b) => b.bookingNo))).not.toContain(t1Booking);
    expect(statusOf(t1Booking)).toBe("Requested");

    // M1, as a donor in a personal session, books a T2 meal; back in T1's session M1 cannot approve it.
    const t2Slot = await makeSlot(o2, { name: slotName("F12 T2 meal") });
    madeT2.push(t2Slot.publicId);
    const signedInM1 = (await mobileLogin(account("M1"))).session!;
    const visitor = await anonymous();
    let personal!: MobileSession;
    try {
      const r = await visitor.post("/api/auth/mobile/session/switch-personal", { data: { refreshToken: signedInM1.refreshToken } });
      expect(r.status()).toBe(200);
      personal = (await r.json()) as MobileSession;
    } finally {
      await visitor.dispose();
    }
    const asDonor = await bearer(personal.accessToken);
    let own = "";
    try {
      own = await booked(asDonor, t2Slot.publicId, date);
    } finally {
      await asDonor.dispose();
    }
    expect(await decide(m1, own, "approve")).toMatchObject({ status: 404, body: { code: "booking_not_found" } });
    expect(statusOf(own)).toBe("Requested");
  } finally {
    await cleanUp(sessions, "M1", made);
    await cleanUp(sessions, "O2", madeT2);
    await sessions.dispose();
  }
});

test("QA-F12-06 live: a donor's booking appears on M1's open calendar, and O1's answer in a second browser clears it, without a reload", async ({ page, browser }, testInfo) => {
  const sessions = new Sessions();
  const made: string[] = [];
  try {
    const slot = await makeSlot(await sessions.of("M1"), { name: slotName("F12 Live"), bookingMode: "Shared", maxSponsors: 4 });
    made.push(slot.publicId);
    const date = colomboDate(10);

    await signIn(page, account("M1"), "si");
    await page.goto(`/si/bookings/calendar?date=${date}`);
    await expect(page.getByRole("heading", { name: si.Calendar.tray.heading })).toBeVisible();
    await page.evaluate(() => ((window as unknown as { __notReloaded: boolean }).__notReloaded = true));

    // O1 in a browser of its own, on the same calendar.
    const colleague = await (await browser.newContext()).newPage();
    await signIn(colleague, account("O1"), "si");
    await colleague.goto(`/si/bookings/calendar?date=${date}`);
    await expect(colleague.getByRole("heading", { name: si.Calendar.tray.heading })).toBeVisible();
    await colleague.evaluate(() => ((window as unknown as { __notReloaded: boolean }).__notReloaded = true));

    // D2 books (through the API, as the phone app does): both calendars show the request.
    const no = await booked(await sessions.of("D2"), slot.publicId, date);
    await expect(cardOf(page, no), "M1 sees the new request").toBeVisible({ timeout: 20_000 });
    await expect(cardOf(colleague, no), "O1 sees the new request").toBeVisible({ timeout: 20_000 });
    await evidence(page, "QA-F12-06", testInfo, "arrived");

    // O1 accepts it on screen; M1's tray lets it go, its day shows it confirmed.
    await cardOf(colleague, no).getByRole("button", { name: si.Calendar.booking.approve, exact: true }).click();
    await expect(colleague.getByText(si.Calendar.booking.done.approve.replace("{bookingNo}", no)).first()).toBeVisible();
    // Soft, so the screen is kept as it was at that moment (the clean-up below would refresh it).
    const trayCount = page.getByRole("heading", { name: si.Calendar.tray.heading }).locator("xpath=following-sibling::span[1]");
    await expect.soft(trayCount, "the request leaves M1's tray").toHaveText("0", { timeout: 20_000 });
    await expect.soft(cardOf(page, no).getByText(si.Booking.status.Approved, { exact: true }), "M1's day shows it confirmed").toBeVisible({ timeout: 20_000 });
    await evidence(page, "QA-F12-06", testInfo, "after-colleague-accepts");

    // The donor cancels; the booking shows cancelled on M1's day without a reload.
    expect((await (await sessions.of("D2")).post(`${BOOK}/${no}/cancel`, { data: { reason: null } })).status()).toBe(200);
    await expect(cardOf(page, no).getByText(si.Booking.status.Cancelled, { exact: true })).toBeVisible({ timeout: 20_000 });
    for (const p of [page, colleague]) expect(await p.evaluate(() => (window as unknown as { __notReloaded?: boolean }).__notReloaded)).toBe(true);
    await colleague.context().close();
  } finally {
    await cleanUp(sessions, "M1", made);
    await sessions.dispose();
  }
});

test("QA-F12-07 the calendar's own reads refuse what is malformed and carry no internal ids", async () => {
  const sessions = new Sessions();
  try {
    const m1 = await sessions.of("M1");
    const bad: string[] = [
      `?from=${colomboDate(0)}`,
      `?from=${colomboDate(5)}&to=${colomboDate(4)}`,
      `?from=${colomboDate(0)}&to=${colomboDate(62)}`,
      `?from=2026-02-30&to=2026-03-01`,
      `?from=${colomboDate(0)}&to=${colomboDate(1)}&status=Lost`,
    ];
    for (const query of bad) expect((await answer(await m1.get(`${CALENDAR}${query}`))).status, query).toBe(400);
    const ok = await answer(await m1.get(`${CALENDAR}?from=${colomboDate(0)}&to=${colomboDate(61)}&status=Requested`));
    expect(ok.status).toBe(200);
    const json = JSON.stringify(ok.body);
    expect(json).not.toMatch(/"(id|slotInstanceId|mealSlotId|institutionId|donorUserId)":/);
  } finally {
    await sessions.dispose();
  }
});
