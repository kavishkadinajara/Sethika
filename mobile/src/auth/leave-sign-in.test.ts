import { router } from "expo-router";

import { LUNCH } from "@/test-utils/fixtures";

import { leaveSignIn, passNext } from "./leave-sign-in";

/*  Where a successful sign-in takes the donor: back to the booking they left, or the tabs. */

jest.mock("expo-router", () => ({ router: { dismissTo: jest.fn() } }));

const BACK = `/i/test-elders-home?slot=${LUNCH}&date=2026-11-21`;

beforeEach(() => {
  jest.mocked(router.dismissTo).mockClear();
});

it("goes back to the institution page and day the booking sheet named", () => {
  leaveSignIn(BACK);
  expect(router.dismissTo).toHaveBeenCalledWith(BACK);
});

it("goes to the tabs without a `next`, or with one that is not an institution page", () => {
  leaveSignIn(undefined);
  leaveSignIn("https://evil.example/i/x");
  expect(jest.mocked(router.dismissTo).mock.calls).toEqual([["/"], ["/"]]);
});

it("hands a safe `next` on from one sign-in screen to the next, and drops any other", () => {
  expect(passNext(BACK)).toEqual({ next: BACK });
  expect(passNext("//evil.example")).toEqual({});
  expect(passNext(undefined)).toEqual({});
});
