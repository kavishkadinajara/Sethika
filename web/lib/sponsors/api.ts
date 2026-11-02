import { json, jsonBody, send } from "@/lib/onboarding/api";
import type { BookingResult } from "@/lib/bookings/api";
import type { Donor, DonorInput } from "./types";

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
