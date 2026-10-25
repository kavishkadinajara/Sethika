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
  BookingRequest,
  BookingStatus,
  Decision,
  DonorBooking,
  InstitutionMealSlot,
  Listing,
  MealSlotInput,
  PublicInstitution,
  PublicInstitutionSummary,
  SlotDay,
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
