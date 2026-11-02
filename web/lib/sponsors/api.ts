import { empty, json, jsonBody, send } from "@/lib/onboarding/api";
import type { BookingResult } from "@/lib/bookings/api";
import type {
  CreatedSponsorship,
  DayRequestInput,
  Donor,
  DonorInput,
  InstitutionDayRequest,
  MyDayRequest,
  PreviewDay,
  Sponsorship,
  SponsorshipDetails,
  SponsorshipInput,
  SwapDay,
} from "./types";

/*  Typed calls to the donor book (plan 08, K1), for the institution in the session. The same
    `{ code, fields }` refusal and session plumbing as the booking calls.                       */

const DONORS = "/api/institution/donors";

export async function listDonors(): Promise<BookingResult<Donor[]>> {
  return json(await send(DONORS, { method: "GET" }));
}

export async function createDonor(input: DonorInput): Promise<BookingResult<{ publicId: string; version: string }>> {
  return json(await send(DONORS, jsonBody("POST", input)));
}

export async function updateDonor(publicId: string, input: DonorInput): Promise<BookingResult<{ publicId: string; version: string }>> {
  return json(await send(`${DONORS}/${encodeURIComponent(publicId)}`, jsonBody("PUT", input)));
}

const SPONSORSHIPS = "/api/institution/sponsorships";
const TRAY = "/api/institution/day-requests";

const path = (base: string, id: string, action?: string) => `${base}/${encodeURIComponent(id)}${action ? `/${action}` : ""}`;

/** Every sponsorship of the institution, active and ended (plan 08, K2). */
export async function listSponsorships(): Promise<BookingResult<Sponsorship[]>> {
  return json(await send(SPONSORSHIPS, { method: "GET" }));
}

/** One sponsorship and its next days (K2, K9). */
export async function getSponsorship(publicId: string): Promise<BookingResult<SponsorshipDetails>> {
  return json(await send(path(SPONSORSHIPS, publicId), { method: "GET" }));
}

/** The next days a sponsorship would have, and their clashes, before it is saved (K9). Writes nothing. */
export async function previewSponsorship(input: Partial<SponsorshipInput>): Promise<BookingResult<{ days: PreviewDay[] }>> {
  return json(await send(`${SPONSORSHIPS}/preview`, jsonBody("POST", input)));
}

export async function createSponsorship(input: SponsorshipInput): Promise<BookingResult<CreatedSponsorship>> {
  return json(await send(SPONSORSHIPS, jsonBody("POST", input)));
}

/** Ends it: every booked day after today is let go, today's meal kept (K23). */
export async function endSponsorship(publicId: string, version: string, reason: string | null): Promise<BookingResult<{ publicId: string; version: string }>> {
  return json(await send(path(SPONSORSHIPS, publicId, "end"), jsonBody("POST", { version, reason })));
}

/** The sponsor cannot give on `date` (K5). */
export async function skipDay(publicId: string, date: string, reason: string | null): Promise<BookingResult> {
  return empty(await send(path(SPONSORSHIPS, publicId, "skip"), jsonBody("POST", { date, reason })));
}

/** Two regular donors swap days, this time only or from now on (K8, K29). */
export async function swapDays(a: SwapDay, b: SwapDay, permanent: boolean): Promise<BookingResult<{ a: string | null; b: string | null }>> {
  return json(await send(`${SPONSORSHIPS}/swap`, jsonBody("POST", { a, b, permanent })));
}

/** The tray: open requests for held days, oldest first (K6). */
export async function listDayRequests(): Promise<BookingResult<InstitutionDayRequest[]>> {
  return json(await send(TRAY, { method: "GET" }));
}

/** Gives the day to the asker: the regular donor's day is let go (K27). `sponsorship` names whose, when several hold it. */
export async function giveDay(requestNo: string, sponsorship: string | null): Promise<BookingResult<{ requestNo: string; bookingNo: string }>> {
  return json(await send(path(TRAY, requestNo, "give"), jsonBody("POST", { sponsorship })));
}

export async function declineDay(requestNo: string, reason: string | null): Promise<BookingResult> {
  return empty(await send(path(TRAY, requestNo, "decline"), jsonBody("POST", { reason })));
}

/** A donor asks for a day a regular donor holds (K26). */
export async function askForDay(slug: string, input: DayRequestInput): Promise<BookingResult<{ requestNo: string }>> {
  return json(await send(`/api/institutions/${encodeURIComponent(slug)}/day-requests`, jsonBody("POST", input)));
}

/** The donor's own requests, newest first. */
export async function listMyDayRequests(): Promise<BookingResult<MyDayRequest[]>> {
  return json(await send("/api/bookings/day-requests", { method: "GET" }));
}
