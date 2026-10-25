import { router, type Href } from "expo-router";

import { safeReturnPath } from "@/bookings/return-path";

/*  Where the sign-in screens go once a session has started. They are pushed on top of whatever
    the visitor was doing (root _layout.tsx), so leaving is going back down the stack:
      - with a `next` from the booking sheet, to that institution page, which reads its slot and
        date again and reopens the sheet on the same day (plan 03, E33);
      - otherwise to the tabs.
    dismissTo pops to the nearest screen of that route, and replaces the sign-in screen with it
    if there is none (the app was opened on a sign-in link).                                   */

export function leaveSignIn(next: unknown): void {
  router.dismissTo((safeReturnPath(next) ?? "/") as Href);
}

/** The `next` to hand on when one sign-in screen opens another (password, code, signup). */
export function passNext(next: unknown): { next?: string } {
  const safe = safeReturnPath(next);
  return safe ? { next: safe } : {};
}
