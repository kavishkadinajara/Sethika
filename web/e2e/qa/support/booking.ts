import type { APIRequestContext } from "@playwright/test";
import type { Account } from "../../support/accounts";
import { colomboDate } from "../../support/institutions";
import { sql } from "../../support/sql";
import { signedIn, stepUpApi } from "./api";
import { account, answer, tenant, type PersonaKey, type TenantKey } from "./world";

/*  The booking calls of the F09-F12 cases, the same ones the screens make (lib/bookings/api.ts).
    Every slot a case makes is named "QA Fxx ... <stamp>" and switched off by the case once its
    bookings are cancelled, so T1's own meals (none, after setup) stay as they were and a re-run
    starts clean. Slot names are unique per institution even when switched off (UQ_MealSlots_Name),
    hence the stamp.                                                                            */

export { colomboDate };

export const SLOTS = "/api/institution/meal-slots";
export const BLOCKS = "/api/institution/blocked-dates";
export const LISTING = "/api/institution/listing";
export const CALENDAR = "/api/institution/bookings";
export const BOOK = "/api/bookings";

/** One signed-in API client per persona for the whole case: sign-ins are rate limited per account. */
export class Sessions {
  private readonly open = new Map<string, APIRequestContext>();
  private readonly stepped = new Set<string>();

  async of(who: PersonaKey | Account, stepUp = false): Promise<APIRequestContext> {
    const person = typeof who === "string" ? account(who) : who;
    let api = this.open.get(person.phone);
    if (!api) {
      api = await signedIn(person);
      this.open.set(person.phone, api);
    }
    if (stepUp && !this.stepped.has(person.phone)) {
      await stepUpApi(api, person);
      this.stepped.add(person.phone);
    }
    return api;
  }

  async dispose() {
    for (const api of this.open.values()) await api.dispose();
    this.open.clear();
  }
}

let stamps = 0;

/** A name no slot of T1 holds yet: "QA <label> <time><n>". */
export function slotName(label: string): string {
  stamps++;
  return `QA ${label} ${Date.now() % 1_000_000}${stamps}`;
}

export type SlotInput = {
  name: string;
  nameSi?: string | null;
  nameTa?: string | null;
  startTime?: string;
  endTime?: string;
  daysOfWeekMask?: number;
  bookingMode?: "Exclusive" | "Shared" | string;
  maxSponsors?: number | null;
  minHeadcount?: number | null;
  maxHeadcount?: number | null;
  estimatedCostPerHead?: number | null;
  minAdvanceDays?: number | null;
  maxAdvanceDays?: number | null;
  requiresApproval?: boolean;
  allowedFulfilment?: string[];
  sortOrder?: number | null;
  version?: string | null;
};

/** A slot that passes every rule: lunch, every day, one sponsor, 1-60 people, 1-120 days ahead. */
export function slotInput(overrides: Partial<SlotInput> & { name: string }): SlotInput {
  return {
    nameSi: null,
    nameTa: null,
    startTime: "11:30",
    endTime: "13:00",
    daysOfWeekMask: 127,
    bookingMode: "Exclusive",
    maxSponsors: null,
    minHeadcount: 1,
    maxHeadcount: 60,
    estimatedCostPerHead: 450,
    minAdvanceDays: 1,
    maxAdvanceDays: 120,
    requiresApproval: true,
    allowedFulfilment: ["DonorBrings", "InstitutionPrepares"],
    sortOrder: null,
    ...overrides,
  };
}

async function must(response: Awaited<ReturnType<APIRequestContext["post"]>>, what: string) {
  if (!response.ok()) throw new Error(`${what}: ${response.status()} ${await response.text()}`);
  return response;
}

export type Slot = { publicId: string; version: string; name: string };

/** A slot made by <api>'s institution; its public id and version. */
export async function makeSlot(api: APIRequestContext, overrides: Partial<SlotInput> & { name: string }): Promise<Slot> {
  const made = await must(await api.post(SLOTS, { data: slotInput(overrides) }), `Create slot ${overrides.name}`);
  const body = (await made.json()) as { publicId: string; version: string };
  return { ...body, name: overrides.name };
}

export type InstitutionSlot = { publicId: string; name: string; version: string; isActive: boolean; maxSponsors: number; bookingMode: string };

export async function slotsOf(api: APIRequestContext): Promise<InstitutionSlot[]> {
  return (await (await must(await api.get(SLOTS), "List slots")).json()) as InstitutionSlot[];
}

/** <donor>'s booking of <slot> on <date>, answered as the API answers it. */
export async function bookAs(api: APIRequestContext, slot: string, date: string, overrides: Record<string, unknown> = {}) {
  return answer(
    await api.post(BOOK, {
      data: { mealSlot: slot, date, headcount: 10, fulfilment: "DonorBrings", inMemoryOf: null, notes: null, ...overrides },
    }),
  );
}

/** A booking that must succeed; its number. */
export async function booked(api: APIRequestContext, slot: string, date: string, overrides: Record<string, unknown> = {}): Promise<string> {
  const r = await bookAs(api, slot, date, overrides);
  if (r.status !== 201) throw new Error(`Book ${slot} ${date}: ${r.status} ${JSON.stringify(r.body)}`);
  return r.body!.bookingNo as string;
}

/** An institution decision on <bookingNo>: approve, reject, cancel, complete, no-show. */
export async function decide(api: APIRequestContext, bookingNo: string, decision: string, reason?: string) {
  return answer(await api.post(`${CALENDAR}/${encodeURIComponent(bookingNo)}/${decision}`, reason === undefined ? {} : { data: { reason } }));
}

export async function donorCancel(api: APIRequestContext, bookingNo: string, reason: string | null = null) {
  return answer(await api.post(`${BOOK}/${encodeURIComponent(bookingNo)}/cancel`, { data: { reason } }));
}

/** The slot-day's counters: BookedSponsors / MaxSponsors / BookedHeadcount, or null before its first booking. */
export function slotDay(slot: string, date: string): { booked: number; max: number; headcount: number; status: string } | null {
  const [row] = sql(
    "SELECT CONCAT(si.BookedSponsors, '|', si.MaxSponsors, '|', si.BookedHeadcount, '|', si.Status) FROM booking.SlotInstances si JOIN booking.MealSlots s ON s.Id = si.MealSlotId WHERE s.PublicId = '$(slot)' AND si.SlotDate = '$(date)';",
    { slot, date },
  );
  if (!row) return null;
  const [booked, max, headcount, status] = row.split("|");
  return { booked: Number(booked), max: Number(max), headcount: Number(headcount), status };
}

export type Day = { date: string; bookable: boolean; reason: string | null; sponsors: number; headcount: number };

/** <date> of <slot> on <slug>'s public calendar, as anyone reads it. */
export async function publicDay(api: APIRequestContext, slug: string, slot: string, date: string): Promise<Day | undefined> {
  const body = (await (await api.get(`/api/institutions/${slug}/availability?month=${date.slice(0, 7)}`)).json()) as {
    slots: { mealSlot: string; days: Day[] }[];
  };
  return body.slots.find((s) => s.mealSlot === slot)?.days.find((d) => d.date === date);
}

/** A booking's status, from the database. */
export function statusOf(bookingNo: string): string {
  return sql("SELECT Status FROM booking.Bookings WHERE BookingNo = '$(no)';", { no: bookingNo })[0];
}

/** The active (Requested / Approved) bookings of <slot>. */
export function activeBookings(slot: string): string[] {
  return sql(
    "SELECT b.BookingNo FROM booking.Bookings b JOIN booking.SlotInstances si ON si.Id = b.SlotInstanceId JOIN booking.MealSlots s ON s.Id = si.MealSlotId WHERE s.PublicId = '$(slot)' AND b.Status IN ('Requested', 'Approved') ORDER BY b.BookingNo;",
    { slot },
  );
}

/**
 * Moves a booking's slot-day to <date> (yyyy-MM-dd), for the rules that depend on the day having
 * come (complete, no-show, the donor's cancel window). The product has no clock to turn; the QA
 * plan (section 4) triggers time-based work instead of waiting, and this is the same idea.
 */
export function moveSlotDay(slot: string, from: string, to: string) {
  sql(
    "UPDATE si SET SlotDate = '$(to)' FROM booking.SlotInstances si JOIN booking.MealSlots s ON s.Id = si.MealSlotId WHERE s.PublicId = '$(slot)' AND si.SlotDate = '$(from)';",
    { slot, from, to },
  );
}

/**
 * Closes what a case made in <tenant>: every active booking of its slots is cancelled by the
 * institution (with a reason), then each slot is switched off and the case's blocked dates removed.
 */
export async function cleanUp(sessions: Sessions, owner: PersonaKey, slots: string[], blocks: number[] = []) {
  const api = await sessions.of(owner);
  for (const slot of slots) {
    for (const bookingNo of activeBookings(slot)) {
      const r = await decide(api, bookingNo, "cancel", "QA: closed by the case that made it.");
      if (r.status !== 200) throw new Error(`Clean-up cancel ${bookingNo}: ${r.status} ${JSON.stringify(r.body)}`);
    }
    await must(await api.post(`${SLOTS}/${slot}/deactivate`), `Switch off ${slot}`);
  }
  for (const id of blocks) await api.delete(`${BLOCKS}/${id}`);
}

/** Removes every closed day of the owner's institution whose reason starts with <prefix> (a case's own). */
export async function removeBlocks(sessions: Sessions, owner: PersonaKey, prefix: string) {
  const api = await sessions.of(owner);
  const list = (await (await api.get(BLOCKS)).json()) as { id: number; reason: string }[];
  for (const b of list) if (b.reason.startsWith(prefix)) await api.delete(`${BLOCKS}/${b.id}`);
}

/** Whether <tenant> is in the public list right now (org.Institutions.IsPubliclyListed). */
export function isListed(key: TenantKey): boolean {
  return sql("SELECT CAST(IsPubliclyListed AS int) FROM org.Institutions WHERE Slug = '$(s)';", { s: tenant(key).slug! })[0] === "1";
}

/** Names nothing in an answer may carry: internal ids (CLAUDE.md, X1). */
export function internalIdKeys(value: unknown, path = "$"): string[] {
  if (Array.isArray(value)) return value.flatMap((v, i) => internalIdKeys(v, `${path}[${i}]`));
  if (value && typeof value === "object") {
    return Object.entries(value as Record<string, unknown>).flatMap(([key, v]) =>
      /^(id|.*Id)$/.test(key) && !/^(publicId)$/.test(key) ? [`${path}.${key}`] : internalIdKeys(v, `${path}.${key}`),
    );
  }
  return [];
}

export { stepUpApi };
