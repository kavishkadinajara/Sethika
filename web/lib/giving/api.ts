import { json, jsonBody, send } from "@/lib/onboarding/api";
import type { BookingResult } from "@/lib/bookings/api";
import type { DonationInput, DonationListItem, LookupItem, MyDonation, Receipt } from "./types";

/*  Typed calls to GivingEndpoints.cs (plan 09, D.1). The same `{ code, fields }` refusal and session
    plumbing as the booking calls; a void that needs a step-up is asked for and retried by the session's
    fetch (StepUpHost).                                                                           */

const DONATIONS = "/api/institution/donations";
const MINE = "/api/me/donations";

/** The institution's gifts given in a Colombo month, `YYYY-MM`; the current month when left out. */
export async function listDonations(month?: string): Promise<BookingResult<DonationListItem[]>> {
  return json(await send(month ? `${DONATIONS}?month=${encodeURIComponent(month)}` : DONATIONS, { method: "GET" }));
}

export async function listCounterMethods(): Promise<BookingResult<LookupItem[]>> {
  return json(await send(`${DONATIONS}/methods`, { method: "GET" }));
}

export async function recordDonation(input: DonationInput): Promise<BookingResult<{ publicId: string; receiptNo: string; version: string }>> {
  return json(await send(DONATIONS, jsonBody("POST", input)));
}

/** Voids it (M6): another person than the recorder, with a reason, behind a step-up. */
export async function voidDonation(receiptNo: string, version: string, reason: string): Promise<BookingResult<{ version: string }>> {
  return json(await send(`${DONATIONS}/${encodeURIComponent(receiptNo)}/void`, jsonBody("POST", { reason, version })));
}

/** A receipt, read through the institution's session or, for `mine`, the donor's own. */
export async function getReceipt(publicId: string, mine: boolean): Promise<BookingResult<Receipt>> {
  return json(await send(`${mine ? MINE : DONATIONS}/${encodeURIComponent(publicId)}`, { method: "GET" }));
}

export async function listMyDonations(): Promise<BookingResult<MyDonation[]>> {
  return json(await send(MINE, { method: "GET" }));
}
