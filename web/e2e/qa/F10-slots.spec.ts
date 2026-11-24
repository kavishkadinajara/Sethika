import type { Locator, Page } from "@playwright/test";
import si from "../../messages/si.json";
import { signIn } from "../support/accounts";
import { sql } from "../support/sql";
import { expect, scrollsSideways, test, widerThanWindow } from "../support/test";
import { anonymous } from "./support/api";
import {
  BLOCKS,
  booked,
  bookAs,
  cleanUp,
  colomboDate,
  decide,
  LISTING,
  makeSlot,
  publicDay,
  removeBlocks,
  Sessions,
  SLOTS,
  slotDay,
  slotInput,
  slotName,
  slotsOf,
  statusOf,
} from "./support/booking";
import { account, answer, evidence, tenant } from "./support/world";

/*  F10 - Meal slots and blocked dates (P1). Cases: docs/qa/cases/F10-slots.md. M1 (Manager of T1,
    Sinhala) runs the meals day to day. Every slot is "QA F10 ... <stamp>"; each case cancels the
    bookings it made and switches its slots off (cleanUp). The plan's "plan slot limit" (QA-F10-08)
    is N/A: no code reads org.Plans.MaxMealSlots (OBS-02).                                     */

const t1 = () => tenant("T1");
const escape = (text: string) => text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");

/** The control labelled <label> in any language: "(required)" is translated too. */
const labelled = (scope: Page | Locator, label: string) => scope.getByLabel(new RegExp(`^${escape(label)}( \\(.+\\))?$`));

test("QA-F10-01 M1 adds a shared meal on the Sinhala slots screen; the form and the API refuse what is wrong, in Sinhala @phone", async ({ page }, testInfo) => {
  const sessions = new Sessions();
  const name = slotName("F10 Form lunch");
  const nameSi = `QA බෙදාගන්නා දානය ${name.split(" ").pop()}`;
  const made: string[] = [];
  try {
    await signIn(page, account("M1"), "si");
    await page.goto("/si/bookings/slots");
    await expect(page.getByRole("heading", { name: si.Slots.title, level: 1 })).toBeVisible();
    await page.getByRole("button", { name: si.Slots.add }).click();
    const form = page.getByRole("dialog");
    await expect(form.getByRole("heading", { name: si.Slots.form.newTitle })).toBeVisible();

    // Nothing filled in: the name and the most people are asked for.
    await form.getByRole("button", { name: si.Slots.form.create }).click();
    await expect(form.getByText(si.Slots.form.errors.name)).toBeVisible();
    await expect(form.getByText(si.Slots.form.errors.maxHeadcount)).toBeVisible();

    await labelled(form, si.Slots.form.name).fill(name);
    await labelled(form, si.Slots.form.nameSi).fill(nameSi);
    await labelled(form, si.Slots.form.maxHeadcount).fill("40");
    await form.getByRole("radio", { name: new RegExp(si.Slots.form.modes.Shared) }).check();
    // 21 sponsors is one past the rule's top (2-20): the API names the field, the form says why.
    await labelled(form, si.Slots.form.maxSponsors).fill("21");
    await form.getByRole("button", { name: si.Slots.form.create }).click();
    await expect(form.getByText(si.Slots.form.errors.maxSponsors)).toBeVisible();
    await labelled(form, si.Slots.form.maxSponsors).fill("4");
    await form.getByRole("button", { name: si.Slots.form.create }).click();
    await expect(form).toBeHidden();

    const card = page.locator("article").filter({ has: page.getByRole("heading", { name: nameSi }) });
    await expect(card).toBeVisible();
    await expect(card.getByText(si.Slots.sharedBy.replace("{count}", "4"))).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);
    await evidence(page, "QA-F10-01", testInfo);

    // A second meal of the same name is refused by name, in place.
    await page.getByRole("button", { name: si.Slots.add }).click();
    await labelled(form, si.Slots.form.name).fill(name);
    await labelled(form, si.Slots.form.maxHeadcount).fill("40");
    await form.getByRole("button", { name: si.Slots.form.create }).click();
    await expect(form.getByText(si.Booking.errors.slot_name_taken)).toBeVisible();
    await page.keyboard.press("Escape");

    const slot = (await slotsOf(await sessions.of("M1"))).find((s) => s.name === name)!;
    expect([slot.bookingMode, slot.maxSponsors]).toEqual(["Shared", 4]);
  } finally {
    // Made on the screen, so found by name: closed even when a step above failed.
    const mine = (await slotsOf(await sessions.of("M1"))).find((s) => s.name === name && s.isActive);
    if (mine) made.push(mine.publicId);
    await cleanUp(sessions, "M1", made);
    await sessions.dispose();
  }
});

test("QA-F10-02 a meal's rules at their boundaries: sponsors 1/2/20/21, people, times, days, advance, cost, fulfilment, name", async ({ page }, testInfo) => {
  const sessions = new Sessions();
  const longName = `QA ${Date.now()}`.padEnd(100, "x");
  const made: string[] = [];
  try {
    const api = await sessions.of("M1");
    const before = (await slotsOf(api)).length;
    const create = async (overrides: Record<string, unknown>) => {
      const r = await answer(await api.post(SLOTS, { data: slotInput({ name: slotName("F10 Edge"), ...overrides }) }));
      if (r.status === 201) made.push(r.body!.publicId as string);
      return r;
    };
    // overrides -> the one field named, or 201
    const table: [string, Record<string, unknown>, string | null][] = [
      ["shared 1", { bookingMode: "Shared", maxSponsors: 1 }, "maxSponsors"],
      ["shared 2", { bookingMode: "Shared", maxSponsors: 2 }, null],
      ["shared 20", { bookingMode: "Shared", maxSponsors: 20 }, null],
      ["shared 21", { bookingMode: "Shared", maxSponsors: 21 }, "maxSponsors"],
      ["shared none", { bookingMode: "Shared", maxSponsors: null }, "maxSponsors"],
      ["exclusive 2", { bookingMode: "Exclusive", maxSponsors: 2 }, "maxSponsors"],
      ["exclusive 1", { bookingMode: "Exclusive", maxSponsors: 1 }, null],
      ["mode", { bookingMode: "Buffet" }, "bookingMode"],
      ["min people 0", { minHeadcount: 0 }, "minHeadcount"],
      ["max < min", { minHeadcount: 10, maxHeadcount: 9 }, "maxHeadcount"],
      ["max = min", { minHeadcount: 10, maxHeadcount: 10 }, null],
      ["no max", { maxHeadcount: null }, "maxHeadcount"],
      ["ends = starts", { startTime: "11:30", endTime: "11:30" }, "endTime"],
      ["bad time", { startTime: "25:00" }, "startTime"],
      ["no days", { daysOfWeekMask: 0 }, "daysOfWeekMask"],
      ["mask 128", { daysOfWeekMask: 128 }, "daysOfWeekMask"],
      ["advance min > max", { minAdvanceDays: 10, maxAdvanceDays: 9 }, "maxAdvanceDays"],
      ["advance -1", { minAdvanceDays: -1 }, "minAdvanceDays"],
      ["same day", { minAdvanceDays: 0, maxAdvanceDays: 0 }, null],
      ["cost -1", { estimatedCostPerHead: -1 }, "estimatedCostPerHead"],
      ["cost 3 decimals", { estimatedCostPerHead: 450.555 }, "estimatedCostPerHead"],
      ["cost 1e8", { estimatedCostPerHead: 100000000 }, "estimatedCostPerHead"],
      ["no fulfilment", { allowedFulfilment: [] }, "allowedFulfilment"],
      ["caterer", { allowedFulfilment: ["Caterer"] }, "allowedFulfilment"],
      ["name 101", { name: "Q".repeat(101) }, "name"],
      ["name blank", { name: "   " }, "name"],
      ["name 100", { name: longName }, null],
    ];
    for (const [label, overrides, field] of table) {
      const r = await create(overrides);
      if (field === null) expect(r.status, label).toBe(201);
      else expect([r.status, r.body?.code, r.body?.fields], label).toEqual([400, "invalid_details", [field]]);
    }
    const taken = await create({ name: (await slotsOf(api)).find((s) => s.publicId === made[0])!.name });
    expect([taken.status, taken.body?.code]).toEqual([409, "slot_name_taken"]);
    expect((await slotsOf(api)).length, "only the good ones were made").toBe(before + made.length);

    // The longest name the rule allows, as one word, on the slots screen: the card must still fit.
    await signIn(page, account("M1"), "si");
    await page.goto("/si/bookings/slots");
    await expect(page.getByRole("heading", { name: longName, exact: true })).toBeVisible();
    await evidence(page, "QA-F10-02", testInfo, "long-name");
    const card = page.locator("article").filter({ has: page.getByRole("heading", { name: longName, exact: true }) });
    expect(await widerThanWindow(page), "nothing past the window").toEqual([]);
    // The title's text must wrap inside its card, not run over the card beside it.
    const overflow = await card.evaluate((article) => article.scrollWidth - article.clientWidth);
    expect(overflow, "the 100-character name stays inside its card (px past its edge)").toBeLessThanOrEqual(1);

    // The same meal on the public page, at phone width.
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto(`/si/i/${t1().slug}`);
    await expect(page.getByRole("radio", { name: new RegExp(longName) })).toBeVisible();
    await evidence(page, "QA-F10-02", testInfo, "long-name-public-390");
    expect(await widerThanWindow(page), "the public page at 390 px with a 100-character meal name").toEqual([]);
  } finally {
    await cleanUp(sessions, "M1", made);
    // Switched-off meals stay on the screen: a short name keeps later runs' layout checks about their own cards.
    sql("UPDATE booking.MealSlots SET Name = CONCAT('QA F10 long name ', Id) WHERE LEN(Name) = 100 AND Name LIKE 'QA %' AND IsActive = 0;");
    await sessions.dispose();
  }
});

test("QA-F10-03 two people edit one meal: the later save with the old version is refused and changes nothing, on the API and on the screen", async ({ page }) => {
  const sessions = new Sessions();
  const made: string[] = [];
  try {
    const m1 = await sessions.of("M1");
    const o1 = await sessions.of("O1");
    const name = slotName("F10 Version");
    const slot = await makeSlot(m1, { name });
    made.push(slot.publicId);
    const put = (api: typeof m1, version: string | null, overrides: Record<string, unknown> = {}) =>
      api.put(`${SLOTS}/${slot.publicId}`, { data: slotInput({ name, ...overrides, version }) }).then(answer);

    // Both read version v1. O1 saves first; M1's save with v1 is stale and changes nothing.
    const v1 = slot.version;
    expect((await put(o1, v1, { maxHeadcount: 30 })).status).toBe(200);
    expect(await put(m1, v1, { maxHeadcount: 99 })).toMatchObject({ status: 409, body: { code: "stale" } });
    expect((await slotsOf(m1)).find((s) => s.publicId === slot.publicId)).toMatchObject({ maxHeadcount: 30 });
    expect(await put(m1, null)).toMatchObject({ status: 400, body: { code: "invalid_details", fields: ["version"] } });
    expect(await put(m1, "AAAA")).toMatchObject({ status: 400, body: { code: "invalid_details", fields: ["version"] } });

    // On the screen: M1 opens the meal, O1 changes it meanwhile, M1 saves.
    await signIn(page, account("M1"), "si");
    await page.goto("/si/bookings/slots");
    const card = page.locator("article").filter({ has: page.getByRole("heading", { name, exact: true }) });
    await card.getByRole("button", { name: si.Slots.edit }).click();
    const form = page.getByRole("dialog");
    await expect(form.getByRole("heading", { name: si.Slots.form.editTitle })).toBeVisible();
    const current = (await slotsOf(o1)).find((s) => s.publicId === slot.publicId)!;
    expect((await put(o1, current.version, { maxHeadcount: 25 })).status).toBe(200);

    await labelled(form, si.Slots.form.maxHeadcount).fill("45");
    await form.getByRole("button", { name: si.Slots.form.save }).click();
    await expect(form.getByText(si.Booking.errors.stale)).toBeVisible();
    expect((await slotsOf(m1)).find((s) => s.publicId === slot.publicId)).toMatchObject({ maxHeadcount: 25 });

    // The message says the meal "has been refreshed; check and try again": the form should now
    // hold O1's change, and a second save should go through.
    await expect(labelled(form, si.Slots.form.maxHeadcount), "the form shows O1's 25 after the refresh").toHaveValue("25");
    await labelled(form, si.Slots.form.maxHeadcount).fill("45");
    await form.getByRole("button", { name: si.Slots.form.save }).click();
    await expect(form).toBeHidden();
    expect((await slotsOf(m1)).find((s) => s.publicId === slot.publicId)).toMatchObject({ maxHeadcount: 45 });
  } finally {
    await cleanUp(sessions, "M1", made);
    await sessions.dispose();
  }
});

test("QA-F10-04 closing a day that has bookings keeps them, lists them, and stops new ones; reopening lets booking in again @phone", async ({ page }, testInfo) => {
  const sessions = new Sessions();
  const made: string[] = [];
  const blocks: number[] = [];
  try {
    const m1 = await sessions.of("M1");
    const lunch = await makeSlot(m1, { name: slotName("F10 Block lunch"), bookingMode: "Shared", maxSponsors: 4 });
    const dinner = await makeSlot(m1, { name: slotName("F10 Block dinner"), startTime: "18:00", endTime: "19:30" });
    made.push(lunch.publicId, dinner.publicId);
    const day = colomboDate(6);
    const other = colomboDate(7);
    const d1No = await booked(await sessions.of("D1"), lunch.publicId, day);
    const d2No = await booked(await sessions.of("D2"), lunch.publicId, day);
    expect((await decide(m1, d2No, "approve")).status).toBe(200);

    // M1 closes the day for every meal, on the Sinhala screen.
    await signIn(page, account("M1"), "si");
    await page.goto("/si/bookings/slots");
    await page.getByRole("button", { name: si.Slots.blocks.add }).click();
    const sheet = page.getByRole("dialog");
    await expect(sheet.getByText(si.Slots.blocks.keepsBookings)).toBeVisible();
    await labelled(sheet, si.Slots.blocks.from).fill(day);
    await labelled(sheet, si.Slots.blocks.to).fill(day);
    await labelled(sheet, si.Slots.blocks.reason).fill("QA F10 pinkama");
    await sheet.getByRole("button", { name: si.Slots.blocks.save }).click();
    await expect(sheet).toBeHidden();
    // The two bookings are named, so M1 can call the donors; nothing was cancelled.
    for (const no of [d1No, d2No]) await expect(page.getByText(no).first()).toBeVisible();
    expect([statusOf(d1No), statusOf(d2No)]).toEqual(["Requested", "Approved"]);
    expect(slotDay(lunch.publicId, day)?.booked).toBe(2);
    expect(await widerThanWindow(page), "nothing reaches past the window").toEqual([]);
    await evidence(page, "QA-F10-04", testInfo);

    const list = (await (await m1.get(BLOCKS)).json()) as { id: number; dateFrom: string; reason: string }[];
    const block = list.find((b) => b.dateFrom === day && b.reason === "QA F10 pinkama")!;
    blocks.push(block.id);

    // Nobody else books the day, for either meal; the public calendar says it is closed.
    const d5 = await sessions.of("D5");
    expect(await bookAs(d5, lunch.publicId, day)).toMatchObject({ status: 409, body: { code: "date_blocked" } });
    expect(await bookAs(d5, dinner.publicId, day)).toMatchObject({ status: 409, body: { code: "date_blocked" } });
    const visitor = await anonymous();
    try {
      expect(await publicDay(visitor, t1().slug!, lunch.publicId, day)).toMatchObject({ bookable: false, reason: "date_blocked" });

      // A block for one meal leaves the other bookable.
      const one = await answer(await m1.post(BLOCKS, { data: { dateFrom: other, dateTo: other, mealSlot: dinner.publicId, reason: "QA F10 dinner only" } }));
      expect(one.status).toBe(201);
      blocks.push(one.body!.id as number);
      expect(await bookAs(d5, dinner.publicId, other)).toMatchObject({ status: 409, body: { code: "date_blocked" } });
      const lunchOther = await bookAs(d5, lunch.publicId, other);
      expect(lunchOther.status).toBe(201);

      // Reopened: booking goes in again; reopening twice says it is already gone.
      expect((await m1.delete(`${BLOCKS}/${block.id}`)).status()).toBe(204);
      blocks.splice(blocks.indexOf(block.id), 1);
      expect(await answer(await m1.delete(`${BLOCKS}/${block.id}`))).toMatchObject({ status: 404, body: { code: "blocked_date_not_found" } });
      expect(await publicDay(visitor, t1().slug!, lunch.publicId, day)).toMatchObject({ bookable: true, sponsors: 2 });
      expect((await bookAs(d5, lunch.publicId, day)).status).toBe(201);
    } finally {
      await visitor.dispose();
    }

    // The block's own rules at their edges.
    const block_ = (body: Record<string, unknown>) => m1.post(BLOCKS, { data: { reason: "QA F10", mealSlot: null, ...body } }).then(answer);
    const invalid: [string, Record<string, unknown>, string[]][] = [
      ["to before from", { dateFrom: colomboDate(9), dateTo: colomboDate(8) }, ["dateTo"]],
      ["367 dates", { dateFrom: colomboDate(1), dateTo: colomboDate(367) }, ["dateTo"]],
      ["ended yesterday", { dateFrom: colomboDate(-3), dateTo: colomboDate(-1) }, ["dateTo"]],
      ["no from", { dateFrom: null, dateTo: colomboDate(2) }, ["dateFrom"]],
      ["no reason", { dateFrom: colomboDate(2), dateTo: colomboDate(2), reason: "  " }, ["reason"]],
      ["reason 201", { dateFrom: colomboDate(2), dateTo: colomboDate(2), reason: "R".repeat(201) }, ["reason"]],
    ];
    for (const [label, body, fields] of invalid) expect(await block_(body), label).toMatchObject({ status: 400, body: { code: "invalid_details", fields } });
    expect(await block_({ dateFrom: colomboDate(2), dateTo: colomboDate(2), mealSlot: "0190aaaa-0000-7000-8000-000000000000" })).toMatchObject({
      status: 404,
      body: { code: "slot_not_found" },
    });
    // 366 dates (E18: "at most 366 days") and one that began before today are fine.
    for (const body of [{ dateFrom: colomboDate(1), dateTo: colomboDate(366) }, { dateFrom: colomboDate(-2), dateTo: colomboDate(0), mealSlot: dinner.publicId }]) {
      const r = await block_(body);
      expect(r.status, JSON.stringify(body)).toBe(201);
      blocks.push(r.body!.id as number);
    }
  } finally {
    await cleanUp(sessions, "M1", made, blocks);
    await removeBlocks(sessions, "M1", "QA F10");
    await sessions.dispose();
  }
});

test("QA-F10-05 a meal's capacity changed after a day was booked: that day keeps its places, new days take the new rule (C3)", async () => {
  const sessions = new Sessions();
  const made: string[] = [];
  try {
    const m1 = await sessions.of("M1");
    const name = slotName("F10 Snapshot");
    const slot = await makeSlot(m1, { name, bookingMode: "Shared", maxSponsors: 4 });
    made.push(slot.publicId);
    const touched = colomboDate(10);
    const fresh = colomboDate(11);
    await booked(await sessions.of("D1"), slot.publicId, touched);

    const version = (await slotsOf(m1)).find((s) => s.publicId === slot.publicId)!.version;
    expect((await m1.put(`${SLOTS}/${slot.publicId}`, { data: slotInput({ name, bookingMode: "Shared", maxSponsors: 2, version }) })).status()).toBe(200);
    expect(slotDay(slot.publicId, touched)).toMatchObject({ booked: 1, max: 4 });

    const visitor = await anonymous();
    try {
      expect(await publicDay(visitor, t1().slug!, slot.publicId, touched)).toMatchObject({ bookable: true, sponsors: 3 });
      expect(await publicDay(visitor, t1().slug!, slot.publicId, fresh)).toMatchObject({ bookable: true, sponsors: 2 });
    } finally {
      await visitor.dispose();
    }
    // The new day fills at two; the third donor is told it is full, and the counter never passes the cap.
    await booked(await sessions.of("D2"), slot.publicId, fresh);
    await booked(await sessions.of("D5"), slot.publicId, fresh);
    expect(await bookAs(await sessions.of("D1"), slot.publicId, fresh)).toMatchObject({ status: 409, body: { code: "slot_full" } });
    expect(slotDay(slot.publicId, fresh)).toMatchObject({ booked: 2, max: 2 });
  } finally {
    await cleanUp(sessions, "M1", made);
    await sessions.dispose();
  }
});

test("QA-F10-06 switching a meal off keeps its bookings and names them, hides it, and refuses new ones; switching on undoes it", async () => {
  const sessions = new Sessions();
  const made: string[] = [];
  try {
    const m1 = await sessions.of("M1");
    const slot = await makeSlot(m1, { name: slotName("F10 Switch") });
    made.push(slot.publicId);
    const day = colomboDate(8);
    const no = await booked(await sessions.of("D1"), slot.publicId, day);

    const off = await answer(await m1.post(`${SLOTS}/${slot.publicId}/deactivate`));
    expect(off).toMatchObject({ status: 200, body: { bookings: [no] } });
    expect(statusOf(no)).toBe("Requested");
    expect(await bookAs(await sessions.of("D2"), slot.publicId, colomboDate(9))).toMatchObject({ status: 409, body: { code: "slot_inactive" } });
    const visitor = await anonymous();
    try {
      const page = (await (await visitor.get(`/api/institutions/${t1().slug}`)).json()) as { slots: { publicId: string }[] };
      expect(page.slots.map((s) => s.publicId)).not.toContain(slot.publicId);
    } finally {
      await visitor.dispose();
    }
    // Off twice is still off; on again books again.
    expect((await m1.post(`${SLOTS}/${slot.publicId}/deactivate`)).status()).toBe(200);
    expect(await answer(await m1.post(`${SLOTS}/${slot.publicId}/activate`))).toMatchObject({ status: 200, body: { bookings: [] } });
    expect((await bookAs(await sessions.of("D2"), slot.publicId, colomboDate(9))).status).toBe(201);
  } finally {
    await cleanUp(sessions, "M1", made);
    await sessions.dispose();
  }
});

test("QA-F10-07 role × action on the slot routes: Owner and Manager change, Staff only reads, the rest are refused; the screen follows", async ({ page }) => {
  const sessions = new Sessions();
  const made: string[] = [];
  try {
    const m1 = await sessions.of("M1");
    const slot = await makeSlot(m1, { name: slotName("F10 Roles") });
    made.push(slot.publicId);
    const block = await answer(await m1.post(BLOCKS, { data: { dateFrom: colomboDate(40), dateTo: colomboDate(40), mealSlot: null, reason: "QA F10 roles" } }));
    const blockId = block.body!.id as number;
    try {
      const routes = async (who: "O1" | "M1" | "S1" | "C1" | "L1" | "D1") => {
        const api = await sessions.of(who);
        const version = (await slotsOf(m1)).find((s) => s.publicId === slot.publicId)!.version;
        return [
          (await api.get(SLOTS)).status(),
          (await api.get(BLOCKS)).status(),
          (await api.get(LISTING)).status(),
          (await api.post(SLOTS, { data: slotInput({ name: slotName("F10 Role try") }) })).status(),
          (await api.put(`${SLOTS}/${slot.publicId}`, { data: slotInput({ name: slot.name, version }) })).status(),
          (await api.post(`${SLOTS}/${slot.publicId}/activate`)).status(),
          (await api.post(BLOCKS, { data: { dateFrom: colomboDate(41), dateTo: colomboDate(41), reason: "QA F10 role try" } })).status(),
          (await api.delete(`${BLOCKS}/${-1}`)).status(),
          (await api.put(LISTING, { data: { listed: false } })).status(),
        ];
      };
      // reads x3, create, edit, activate, block, unblock (an id no one has: 404 when allowed), listing
      const expected: Record<string, number[]> = {
        O1: [200, 200, 200, 201, 200, 200, 201, 404, 204],
        M1: [200, 200, 200, 201, 200, 200, 201, 404, 204],
        S1: [200, 200, 200, 403, 403, 403, 403, 403, 403],
        C1: [403, 403, 403, 403, 403, 403, 403, 403, 403],
        L1: [403, 403, 403, 403, 403, 403, 403, 403, 403],
        D1: [403, 403, 403, 403, 403, 403, 403, 403, 403],
      };
      for (const who of Object.keys(expected) as (keyof typeof expected & ("O1" | "M1" | "S1" | "C1" | "L1" | "D1"))[]) {
        expect(await routes(who), who).toEqual(expected[who]);
      }
      // What O1 and M1 just made is the case's to close.
      for (const s of await slotsOf(m1)) if (s.name.startsWith("QA F10 Role try") && s.isActive) made.push(s.publicId);
      const list = (await (await m1.get(BLOCKS)).json()) as { id: number; reason: string }[];
      for (const b of list) if (b.reason === "QA F10 role try") await m1.delete(`${BLOCKS}/${b.id}`);
    } finally {
      await m1.delete(`${BLOCKS}/${blockId}`);
    }

    // S1's screen reads the meals but offers no change.
    await signIn(page, account("S1"), "si");
    await page.goto("/si/bookings/slots");
    await expect(page.getByRole("heading", { name: slot.name, exact: true })).toBeVisible();
    await expect(page.getByRole("button", { name: si.Slots.add })).toHaveCount(0);
    await expect(page.getByRole("button", { name: si.Slots.edit })).toHaveCount(0);
    await expect(page.getByRole("button", { name: si.Slots.blocks.add })).toHaveCount(0);
    await expect(page.getByRole("switch")).toHaveCount(0);
    await expect(page.getByText(si.Slots.listing.unlistedHint)).toBeVisible();
  } finally {
    await cleanUp(sessions, "M1", made);
    await sessions.dispose();
  }
});

test("QA-F10-09 another tenant's owner cannot read or change T1's meals and closed days by their ids", async () => {
  const sessions = new Sessions();
  const made: string[] = [];
  try {
    const m1 = await sessions.of("M1");
    const slot = await makeSlot(m1, { name: slotName("F10 Tenant") });
    made.push(slot.publicId);
    const block = await answer(await m1.post(BLOCKS, { data: { dateFrom: colomboDate(42), dateTo: colomboDate(42), mealSlot: slot.publicId, reason: "QA F10 tenant" } }));
    const blockId = block.body!.id as number;
    try {
      const o2 = await sessions.of("O2");
      const slots = (await (await o2.get(SLOTS)).json()) as { publicId: string }[];
      expect(slots.map((s) => s.publicId)).not.toContain(slot.publicId);
      const blocks = (await (await o2.get(BLOCKS)).json()) as { id: number }[];
      expect(blocks.map((b) => b.id)).not.toContain(blockId);

      expect(await answer(await o2.put(`${SLOTS}/${slot.publicId}`, { data: slotInput({ name: "QA hijack", version: slot.version }) }))).toMatchObject({
        status: 404,
        body: { code: "slot_not_found" },
      });
      for (const action of ["deactivate", "activate"]) {
        expect(await answer(await o2.post(`${SLOTS}/${slot.publicId}/${action}`)), action).toMatchObject({ status: 404, body: { code: "slot_not_found" } });
      }
      expect(await answer(await o2.post(BLOCKS, { data: { dateFrom: colomboDate(43), dateTo: colomboDate(43), mealSlot: slot.publicId, reason: "QA hijack" } }))).toMatchObject({
        status: 404,
        body: { code: "slot_not_found" },
      });
      expect(await answer(await o2.delete(`${BLOCKS}/${blockId}`))).toMatchObject({ status: 404, body: { code: "blocked_date_not_found" } });

      // T1's meal and closed day are as they were.
      expect((await slotsOf(m1)).find((s) => s.publicId === slot.publicId)).toMatchObject({ name: slot.name, isActive: true, version: slot.version });
      expect(((await (await m1.get(BLOCKS)).json()) as { id: number }[]).map((b) => b.id)).toContain(blockId);
    } finally {
      await m1.delete(`${BLOCKS}/${blockId}`);
    }
  } finally {
    await cleanUp(sessions, "M1", made);
    await sessions.dispose();
  }
});
