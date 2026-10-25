import { booking } from "@/test-utils/fixtures";

import { isUpcoming, splitBookings } from "./mine";

/*  My bookings' two lists. The API returns the newest 200 by slot date (plan 03, E27); the app
    sorts them itself, so the order does not depend on how the API happens to send them.     */

const TODAY = "2026-11-10";

describe("splitBookings", () => {
  it("puts what may still happen first, soonest first, and the rest after, latest first", () => {
    const bookings = [
      booking({ bookingNo: "BK-1", date: "2026-11-30", status: "Approved" }),
      booking({ bookingNo: "BK-2", date: "2026-11-01", status: "Completed" }),
      booking({ bookingNo: "BK-3", date: "2026-11-12", status: "Requested" }),
      booking({ bookingNo: "BK-4", date: "2026-10-01", status: "Cancelled" }),
      booking({ bookingNo: "BK-5", date: "2026-11-10", status: "Approved", startTime: "18:00" }),
      booking({ bookingNo: "BK-6", date: "2026-11-10", status: "Approved", startTime: "07:00" }),
      booking({ bookingNo: "BK-7", date: "2026-11-05", status: "NoShow" }),
    ];

    const { upcoming, past } = splitBookings(bookings, TODAY);

    expect(upcoming.map((b) => b.bookingNo)).toEqual(["BK-6", "BK-5", "BK-3", "BK-1"]);
    expect(past.map((b) => b.bookingNo)).toEqual(["BK-7", "BK-2", "BK-4"]);
  });

  it("counts a rejected or cancelled booking still to come as history", () => {
    expect(isUpcoming(booking({ date: "2026-11-20", status: "Rejected" }), TODAY)).toBe(false);
    expect(isUpcoming(booking({ date: "2026-11-20", status: "Cancelled" }), TODAY)).toBe(false);
    expect(isUpcoming(booking({ date: "2026-11-09", status: "Approved" }), TODAY)).toBe(false);
    expect(isUpcoming(booking({ date: TODAY, status: "Requested" }), TODAY)).toBe(true);
  });
});
