import { useMemo } from "react";

import { getJson } from "@/api/http";
import { useSessionClient } from "@/auth/session-provider";

import type { BookingFailure } from "./errors";
import type {
  Availability,
  BookingCreated,
  BookingRequest,
  DonorBooking,
  PublicInstitution,
  PublicInstitutionSummary,
} from "./types";

/*  Typed calls to the donor booking API (plan 03, 5.5), the twin of web/lib/bookings/api.ts.

    The institution reads are anonymous (C10): they go through http.ts's getJson, so a visitor
    who never signed in can browse, and they never wait for a session. Booking, the donor's list
    and cancelling go through the session client's fetch, which adds the Bearer token and
    refreshes it once on a 401. Every call resolves: a refusal or a lost connection comes back as
    a BookingFailure for the screen to put into words (errors.ts).                           */

export type BookingResult<T> = { ok: true; value: T } | { ok: false; failure: BookingFailure };

type Fetch = (path: string, init?: RequestInit) => Promise<Response>;

export function createBookingApi(signedIn: Fetch, anonymous: Fetch = (path) => getJson(path)) {
  const slug = (value: string) => encodeURIComponent(value);
  const post = (body: unknown): RequestInit => ({
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });

  return {
    listInstitutions: (): Promise<BookingResult<PublicInstitutionSummary[]>> => read(() => anonymous("/api/institutions")),

    getInstitution: (institution: string): Promise<BookingResult<PublicInstitution>> =>
      read(() => anonymous(`/api/institutions/${slug(institution)}`)),

    /** One month of every active slot's days; `month` is "yyyy-MM". */
    getAvailability: (institution: string, month: string): Promise<BookingResult<Availability>> =>
      read(() => anonymous(`/api/institutions/${slug(institution)}/availability?month=${encodeURIComponent(month)}`)),

    book: (request: BookingRequest): Promise<BookingResult<BookingCreated>> => read(() => signedIn("/api/bookings", post(request))),

    listMine: (): Promise<BookingResult<DonorBooking[]>> => read(() => signedIn("/api/bookings/mine")),

    cancel: (bookingNo: string, reason: string | null): Promise<BookingResult<unknown>> =>
      read(() => signedIn(`/api/bookings/${encodeURIComponent(bookingNo)}/cancel`, post({ reason }))),

    /** Lets one of the donor's regular days go (plan 08, K24). */
    letGo: (bookingNo: string, reason: string | null): Promise<BookingResult<unknown>> =>
      read(() => signedIn(`/api/bookings/${encodeURIComponent(bookingNo)}/skip`, post({ reason }))),
  };
}

export type BookingApi = ReturnType<typeof createBookingApi>;

/** The booking API for this app's one session client. */
export function useBookingApi(): BookingApi {
  const client = useSessionClient();
  return useMemo(() => createBookingApi((path, init) => client.fetch(path, init)), [client]);
}

async function read<T>(call: () => Promise<Response>): Promise<BookingResult<T>> {
  let response: Response;
  try {
    response = await call();
  } catch {
    return { ok: false, failure: { status: 0, code: null, fields: [] } };
  }

  if (response.status === 204) return { ok: true, value: undefined as T };

  if (response.ok) {
    try {
      return { ok: true, value: (await response.json()) as T };
    } catch {
      // A 2xx that is not the API's JSON: a captive portal's page, a proxy.
      return { ok: false, failure: { status: response.status, code: null, fields: [] } };
    }
  }

  let body: { code?: unknown; fields?: unknown } = {};
  try {
    body = (await response.json()) as typeof body;
  } catch {
    // Not JSON: a 502 from a proxy, a rate limiter's empty 429.
  }
  return {
    ok: false,
    failure: {
      status: response.status,
      code: typeof body.code === "string" ? body.code : null,
      fields: Array.isArray(body.fields) ? body.fields.filter((field): field is string => typeof field === "string") : [],
    },
  };
}
