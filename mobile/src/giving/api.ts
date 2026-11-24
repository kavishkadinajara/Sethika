import { useMemo } from "react";

import { useSessionClient } from "@/auth/session-provider";
import type { BookingResult } from "@/bookings/api";
import type { BookingFailure } from "@/bookings/errors";

import type { CheckoutForm, GiveInput, MyPayment } from "./types";

/*  The donor's online giving calls (PaymentEndpoints.cs), the twin of web/lib/giving/api.ts.
    Both need the donor's own session: they go through the session client's fetch, which adds
    the Bearer token and refreshes it once on a 401. Every call resolves.                      */

type Fetch = (path: string, init?: RequestInit) => Promise<Response>;

export function createGivingApi(signedIn: Fetch) {
  return {
    /** Writes the order and answers PayHere's signed checkout form (M8). */
    startGift: (slug: string, input: GiveInput): Promise<BookingResult<CheckoutForm>> =>
      read(() =>
        signedIn(`/api/institutions/${encodeURIComponent(slug)}/give`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(input),
        }),
      ),

    /** One of the donor's own orders, as PayHere's notification left it (M9). */
    getMyPayment: (orderId: string): Promise<BookingResult<MyPayment>> =>
      read(() => signedIn(`/api/me/payments/${encodeURIComponent(orderId)}`)),
  };
}

export type GivingApi = ReturnType<typeof createGivingApi>;

export function useGivingApi(): GivingApi {
  const client = useSessionClient();
  return useMemo(() => createGivingApi((path, init) => client.fetch(path, init)), [client]);
}

async function read<T>(call: () => Promise<Response>): Promise<BookingResult<T>> {
  const failed = (failure: BookingFailure): BookingResult<T> => ({ ok: false, failure });
  let response: Response;
  try {
    response = await call();
  } catch {
    return failed({ status: 0, code: null, fields: [] });
  }

  if (response.ok) {
    try {
      return { ok: true, value: (await response.json()) as T };
    } catch {
      return failed({ status: response.status, code: null, fields: [] });
    }
  }

  let body: { code?: unknown; fields?: unknown } = {};
  try {
    body = (await response.json()) as typeof body;
  } catch {
    // Not JSON: a proxy's page, a rate limiter's empty 429.
  }
  return failed({
    status: response.status,
    code: typeof body.code === "string" ? body.code : null,
    fields: Array.isArray(body.fields) ? body.fields.filter((field): field is string => typeof field === "string") : [],
  });
}
