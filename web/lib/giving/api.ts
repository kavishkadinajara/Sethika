import { json, jsonBody, send } from "@/lib/onboarding/api";
import { publicGet, type BookingResult } from "@/lib/bookings/api";
import type {
  CheckoutForm,
  DonationInput,
  DonationListItem,
  GiveInput,
  LookupItem,
  MerchantInput,
  MerchantSettings,
  MyDonation,
  MyPayment,
  MyPledge,
  NeedInput,
  NeedListItem,
  NeedLookups,
  NeedPledge,
  PublicNeed,
  Receipt,
} from "./types";

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

// ---- needs and pledges (plan 09, D.2) -------------------------------------------------------------

const NEEDS = "/api/institution/needs";
const PLEDGES = "/api/institution/pledges";
const MY_PLEDGES = "/api/me/pledges";

type Done = { publicId: string };

/** The public board (M12); `query` is `district=&type=&category=` or `institution=slug`, already encoded. */
export async function listBoard(query = ""): Promise<BookingResult<PublicNeed[]>> {
  return json(await publicGet(query ? `/api/needs?${query}` : "/api/needs"));
}

export async function getNeedLookups(): Promise<BookingResult<NeedLookups>> {
  return json(await publicGet("/api/needs/lookups"));
}

export async function listNeeds(): Promise<BookingResult<NeedListItem[]>> {
  return json(await send(NEEDS, { method: "GET" }));
}

export async function addNeed(input: NeedInput): Promise<BookingResult<Done>> {
  return json(await send(NEEDS, jsonBody("POST", input)));
}

export async function changeNeed(publicId: string, input: NeedInput): Promise<BookingResult<Done>> {
  return json(await send(`${NEEDS}/${encodeURIComponent(publicId)}`, jsonBody("PUT", input)));
}

/** Opens a draft, closes an open need, or cancels one nothing came for. */
export async function stepNeed(publicId: string, step: "open" | "close" | "cancel", version: string): Promise<BookingResult<Done>> {
  return json(await send(`${NEEDS}/${encodeURIComponent(publicId)}/${step}`, jsonBody("POST", { version })));
}

export async function listNeedPledges(publicId: string): Promise<BookingResult<NeedPledge[]>> {
  return json(await send(`${NEEDS}/${encodeURIComponent(publicId)}/pledges`, { method: "GET" }));
}

/** Marks a pledge delivered (M15): the answer names the in-kind receipt it made. */
export async function receivePledge(publicId: string, quantity: number, version: string): Promise<BookingResult<Done & { receiptNo: string; receiptPublicId: string }>> {
  return json(await send(`${PLEDGES}/${encodeURIComponent(publicId)}/receive`, jsonBody("POST", { quantity, version })));
}

export async function cancelPledge(publicId: string, reason: string, version: string): Promise<BookingResult<Done>> {
  return json(await send(`${PLEDGES}/${encodeURIComponent(publicId)}/cancel`, jsonBody("POST", { reason, version })));
}

export async function pledge(needId: string, quantity: number, expectedDate: string | null): Promise<BookingResult<Done>> {
  return json(await send(`/api/needs/${encodeURIComponent(needId)}/pledges`, jsonBody("POST", { quantity, expectedDate })));
}

export async function listMyPledges(): Promise<BookingResult<MyPledge[]>> {
  return json(await send(MY_PLEDGES, { method: "GET" }));
}

export async function cancelMyPledge(publicId: string): Promise<BookingResult<Done>> {
  return json(await send(`${MY_PLEDGES}/${encodeURIComponent(publicId)}/cancel`, { method: "POST" }));
}

// ---- online giving (plan 09, D.3) -----------------------------------------------------------------

const MERCHANT = "/api/institution/payments/merchant";

export async function getMerchant(): Promise<BookingResult<{ merchant: MerchantSettings | null }>> {
  return json(await send(MERCHANT, { method: "GET" }));
}

/** Saves the merchant (M10), behind a step-up. A `secret` of null keeps the stored one. */
export async function saveMerchant(input: MerchantInput): Promise<BookingResult<{ version: string }>> {
  return json(await send(MERCHANT, jsonBody("PUT", input)));
}

/** Writes the order and answers PayHere's signed form (M8). */
export async function startGift(slug: string, input: GiveInput): Promise<BookingResult<CheckoutForm>> {
  return json(await send(`/api/institutions/${encodeURIComponent(slug)}/give`, jsonBody("POST", input)));
}

/** One of the donor's own orders, as the notification left it (M9). */
export async function getMyPayment(orderId: string): Promise<BookingResult<MyPayment>> {
  return json(await send(`/api/me/payments/${encodeURIComponent(orderId)}`, { method: "GET" }));
}
