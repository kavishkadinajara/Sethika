import {
  empty,
  json,
  jsonBody,
  readOnboardingFailure,
  send,
  type OnboardingFailure,
  type OnboardingResult,
} from "@/lib/onboarding/api";
import type {
  Availability,
  BlockedDate,
  BookingPlace,
  BookingRequest,
  BookingStatus,
  Decision,
  DonorBooking,
  Invitation,
  MemorialChoice,
  MemorialPlan,
  MemorialPlanInput,
  MemorialReligion,
  SharedBooking,
  SharedWith,
  ShareSummary,
  YearlyBooking,
  InstitutionMealSlot,
  LedgerEntry,
  Listing,
  MealSlotInput,
  PublicInstitution,
  PublicInstitutionSummary,
  SearchAnswer,
  SearchLookups,
  SearchMonth,
  SlotDay,
  TodaysTable,
} from "./types";

/*  Typed calls to the booking API (plan 03, 5.5 and 5.4). The same `{ code, fields }` refusal as
    the onboarding calls, so they share its plumbing (as review-api.ts does).

    The institution reads are anonymous and use plain fetch: a visitor's first paint should not
    wait for the session to start, and they never answer 401. Everything else goes through the
    session's apiFetch.                                                                          */

export type BookingFailure = OnboardingFailure;
export type BookingResult<T = void> = OnboardingResult<T>;

// ---- anyone ---------------------------------------------------------------------------------------

export async function listInstitutions(): Promise<BookingResult<PublicInstitutionSummary[]>> {
  return json(await publicGet("/api/institutions"));
}

export async function getInstitution(slug: string): Promise<BookingResult<PublicInstitution>> {
  return json(await publicGet(`/api/institutions/${encodeURIComponent(slug)}`));
}

/** One month of every active slot's days; `month` is "yyyy-MM". */
export async function getAvailability(slug: string, month: string): Promise<BookingResult<Availability>> {
  return json(await publicGet(`/api/institutions/${encodeURIComponent(slug)}/availability?month=${encodeURIComponent(month)}`));
}

/** The places with a meal open on a day (plan 06); `query` from lib/bookings/find-query.ts. */
export async function searchPlaces(query: string): Promise<BookingResult<SearchAnswer>> {
  return json(await publicGet(`/api/search?${query}`));
}

/** How many places are open on each day of a month, under the same filters. */
export async function searchMonth(query: string): Promise<BookingResult<SearchMonth>> {
  return json(await publicGet(`/api/search/month?${query}`));
}

/** The kinds of place and the districts the search can be narrowed to. */
export async function getSearchLookups(): Promise<BookingResult<SearchLookups>> {
  return json(await publicGet("/api/search/lookups"));
}

// ---- the signed-in donor --------------------------------------------------------------------------

export async function book(request: BookingRequest): Promise<BookingResult<{ bookingNo: string; status: BookingStatus }>> {
  return json(await send("/api/bookings", jsonBody("POST", request)));
}

export async function listMyBookings(): Promise<BookingResult<DonorBooking[]>> {
  return json(await send("/api/bookings/mine", { method: "GET" }));
}

export async function cancelMyBooking(bookingNo: string, reason: string | null): Promise<BookingResult> {
  return empty(await send(`/api/bookings/${encodeURIComponent(bookingNo)}/cancel`, jsonBody("POST", { reason })));
}

/** The donor's active yearly bookings, soonest next first. */
export async function listYearly(): Promise<BookingResult<YearlyBooking[]>> {
  return json(await send("/api/bookings/recurring", { method: "GET" }));
}

/** Stops a yearly booking: no later year is booked; a year already booked stays. */
export async function stopYearly(publicId: string): Promise<BookingResult> {
  return empty(await send(`/api/bookings/recurring/${encodeURIComponent(publicId)}/stop`, { method: "POST" }));
}

// ---- the memorial planner -------------------------------------------------------------------------

export async function listMemorialReligions(): Promise<BookingResult<MemorialReligion[]>> {
  return json(await send("/api/memorial/religions", { method: "GET" }));
}

export async function listRelationships(): Promise<BookingResult<MemorialChoice[]>> {
  return json(await send("/api/memorial/relationships", { method: "GET" }));
}

export async function listMemorialPlans(): Promise<BookingResult<MemorialPlan[]>> {
  return json(await send("/api/memorial/plans", { method: "GET" }));
}

export async function createMemorialPlan(input: MemorialPlanInput): Promise<BookingResult<{ plan: string }>> {
  return json(await send("/api/memorial/plans", jsonBody("POST", input)));
}

export async function archiveMemorialPlan(publicId: string): Promise<BookingResult> {
  return empty(await send(`/api/memorial/plans/${encodeURIComponent(publicId)}/archive`, { method: "POST" }));
}

// ---- sharing a booking (co-sponsors) --------------------------------------------------------------

export async function listInvitations(bookingNo: string): Promise<BookingResult<Invitation[]>> {
  return json(await send(`/api/bookings/${encodeURIComponent(bookingNo)}/contributors`, { method: "GET" }));
}

/** Invites someone; the answer's token makes the link the donor sends themselves (H16). */
export async function invite(bookingNo: string, name: string, note: string | null): Promise<BookingResult<{ contributor: string; token: string }>> {
  return json(await send(`/api/bookings/${encodeURIComponent(bookingNo)}/contributors`, jsonBody("POST", { name, note })));
}

export async function withdrawInvitation(bookingNo: string, publicId: string): Promise<BookingResult> {
  return empty(
    await send(`/api/bookings/${encodeURIComponent(bookingNo)}/contributors/${encodeURIComponent(publicId)}`, { method: "DELETE" }),
  );
}

/** A link's summary. Anonymous, so a person not signed in sees what they are asked to join. */
export async function openShare(token: string): Promise<BookingResult<ShareSummary>> {
  return json(await publicGet(`/api/share/${encodeURIComponent(token)}`));
}

export async function answerShare(token: string, join: boolean): Promise<BookingResult> {
  return empty(await send(`/api/share/${encodeURIComponent(token)}/${join ? "join" : "decline"}`, { method: "POST" }));
}

export async function listSharedWithMe(): Promise<BookingResult<SharedBooking[]>> {
  return json(await send("/api/bookings/shared", { method: "GET" }));
}

// ---- my places (the heart, H12) -------------------------------------------------------------------

export async function listPlaces(): Promise<BookingResult<PublicInstitutionSummary[]>> {
  return json(await send("/api/me/places", { method: "GET" }));
}

export async function keepPlace(slug: string, keep: boolean): Promise<BookingResult> {
  return empty(await send(`/api/me/places/${encodeURIComponent(slug)}`, { method: keep ? "PUT" : "DELETE" }));
}

// ---- the institution, for the institution in the session ----------------------------------------

const INSTITUTION = "/api/institution";

export async function listSlots(): Promise<BookingResult<InstitutionMealSlot[]>> {
  return json(await send(`${INSTITUTION}/meal-slots`, { method: "GET" }));
}

export async function createSlot(input: MealSlotInput): Promise<BookingResult<{ publicId: string; version: string }>> {
  return json(await send(`${INSTITUTION}/meal-slots`, jsonBody("POST", input)));
}

export async function updateSlot(publicId: string, input: MealSlotInput): Promise<BookingResult<{ publicId: string; version: string }>> {
  return json(await send(`${INSTITUTION}/meal-slots/${encodeURIComponent(publicId)}`, jsonBody("PUT", input)));
}

/** Switches a slot off or on; the answer names the bookings still on it, which it does not cancel. */
export async function setSlotActive(publicId: string, active: boolean): Promise<BookingResult<{ bookings: string[] }>> {
  return json(
    await send(`${INSTITUTION}/meal-slots/${encodeURIComponent(publicId)}/${active ? "activate" : "deactivate"}`, { method: "POST" }),
  );
}

export async function listBlockedDates(): Promise<BookingResult<BlockedDate[]>> {
  return json(await send(`${INSTITUTION}/blocked-dates`, { method: "GET" }));
}

/** Adds a block; the answer names the bookings already on those days, which it does not cancel. */
export async function addBlockedDate(input: {
  dateFrom: string;
  dateTo: string;
  mealSlot: string | null;
  reason: string;
}): Promise<BookingResult<{ id: number; bookings: string[] }>> {
  return json(await send(`${INSTITUTION}/blocked-dates`, jsonBody("POST", input)));
}

export async function removeBlockedDate(id: number): Promise<BookingResult> {
  return empty(await send(`${INSTITUTION}/blocked-dates/${id}`, { method: "DELETE" }));
}

export async function getListing(): Promise<BookingResult<Listing>> {
  return json(await send(`${INSTITUTION}/listing`, { method: "GET" }));
}

export async function setListed(listed: boolean): Promise<BookingResult> {
  return empty(await send(`${INSTITUTION}/listing`, jsonBody("PUT", { listed })));
}

/** The booked slot-days from `from` to `to` (at most 62 days), only those with a booking in `status` if given. */
export async function getCalendar(from: string, to: string, status?: BookingStatus): Promise<BookingResult<SlotDay[]>> {
  const query = new URLSearchParams({ from, to });
  if (status) query.set("status", status);
  return json(await send(`${INSTITUTION}/bookings?${query}`, { method: "GET" }));
}

/** Today's meals, booked or not, with the places taken and the requests to answer. */
export async function getToday(): Promise<BookingResult<TodaysTable>> {
  return json(await send(`${INSTITUTION}/today`, { method: "GET" }));
}

/** The latest ten changes of the institution's bookings, newest first. */
export async function getLedger(): Promise<BookingResult<LedgerEntry[]>> {
  return json(await send(`${INSTITUTION}/ledger`, { method: "GET" }));
}

/** A booking by its number, as typed: 404 `booking_not_found` when the institution has none by it. */
export async function findBooking(bookingNo: string): Promise<BookingResult<BookingPlace>> {
  return json(await send(`${INSTITUTION}/bookings/${encodeURIComponent(bookingNo.trim())}`, { method: "GET" }));
}

/** Who joined a booking as a co-sponsor (H37): names only. */
export async function getSharedWith(bookingNo: string): Promise<BookingResult<SharedWith[]>> {
  return json(await send(`${INSTITUTION}/bookings/${encodeURIComponent(bookingNo)}/shared-with`, { method: "GET" }));
}

export async function decide(bookingNo: string, decision: Decision, reason?: string): Promise<BookingResult<{ status: BookingStatus }>> {
  const path = `${INSTITUTION}/bookings/${encodeURIComponent(bookingNo)}/${decision}`;
  return json(await send(path, reason === undefined ? { method: "POST" } : jsonBody("POST", { reason })));
}

// ---- plumbing ---------------------------------------------------------------------------------------

async function publicGet(path: string): Promise<BookingResult<Response>> {
  let response: Response;
  try {
    response = await fetch(path, { cache: "no-store" });
  } catch {
    return {
      ok: false,
      failure: { status: 0, code: null, retryAfter: null, attemptsRemaining: null, passwordFailures: [], fields: [] },
    };
  }
  if (response.ok) return { ok: true, value: response };
  return { ok: false, failure: await readOnboardingFailure(response) };
}
