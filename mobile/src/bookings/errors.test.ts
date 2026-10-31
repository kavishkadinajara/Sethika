import { createTranslator } from "@/i18n/translate";
import en from "@/i18n/messages/en.json";
import si from "@/i18n/messages/si.json";
import ta from "@/i18n/messages/ta.json";

import { BOOKING_CODES, bookingErrorKey, dayIsGone, type BookingFailure } from "./errors";

/*  Every refusal the donor API answers (plan 03, E25) in the donor's words. The codes and
    statuses are the API's: 400 for what the donor fixes, 404 for what does not exist, 409 for
    the slot-day or the account, 429 from the rate limiters (E26).                          */

const failure = (status: number, code: string | null): BookingFailure => ({ status, code, fields: [] });
const say = (f: BookingFailure) => createTranslator("en")(bookingErrorKey(f));

describe("bookingErrorKey", () => {
  it("gives each booking code its own sentence", () => {
    expect(say(failure(409, "slot_full"))).toBe("Someone has just taken this day. Please choose another.");
    expect(say(failure(409, "already_booked"))).toBe("You have already booked this meal on this day.");
    expect(say(failure(409, "too_soon"))).toBe("That day is too soon to book. Please choose a later day.");
    expect(say(failure(400, "headcount_out_of_range"))).toBe("That number of people is outside what this meal allows.");
    expect(say(failure(409, "phone_required"))).toBe("Your account needs a confirmed phone number before you can book.");
    expect(say(failure(409, "cancel_window_closed"))).toBe(
      "It is too late to cancel this booking yourself. Please call the institution.",
    );
  });

  it("says a lost connection and a rate limit in their own words", () => {
    expect(bookingErrorKey(failure(0, null))).toBe("Errors.network");
    expect(bookingErrorKey(failure(429, null))).toBe("Errors.tooManyRequestsNoTime");
  });

  it("falls back to the shared sentences, then to the status, then to something went wrong", () => {
    expect(bookingErrorKey(failure(401, "session_expired"))).toBe("Errors.session_expired");
    expect(bookingErrorKey(failure(401, null))).toBe("Errors.session_expired");
    expect(bookingErrorKey(failure(403, null))).toBe("Discover.sheet.notAllowed");
    expect(bookingErrorKey(failure(409, "not_donor"))).toBe("Errors.unexpected");
    expect(bookingErrorKey(failure(500, null))).toBe("Errors.unexpected");
  });

  it("has every code's sentence in English, Sinhala and Tamil", () => {
    for (const code of BOOKING_CODES) {
      expect(en.Booking.errors[code]).toBeTruthy();
      expect(si.Booking.errors[code]).toBeTruthy();
      expect(ta.Booking.errors[code]).toBeTruthy();
      expect(si.Booking.errors[code]).not.toBe(en.Booking.errors[code]);
      expect(ta.Booking.errors[code]).not.toBe(en.Booking.errors[code]);
    }
  });
});

describe("dayIsGone", () => {
  it("closes the sheet when the day itself went, not when the donor already holds it", () => {
    expect(["slot_full", "date_blocked", "too_soon", "slot_inactive", "institution_not_active"].every(dayIsGone)).toBe(true);
    expect(dayIsGone("already_booked")).toBe(false);
    expect(dayIsGone("headcount_out_of_range")).toBe(false);
    expect(dayIsGone(null)).toBe(false);
  });
});
