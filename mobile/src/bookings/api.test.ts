import { json } from "@/test-utils/render";
import { LUNCH } from "@/test-utils/fixtures";

import { createBookingApi } from "./api";

/*  The booking calls against a scripted transport: which requests go anonymous and which with the
    session, what they send, and how a refusal comes back (DonorBookingEndpoints.cs).        */

type Call = { path: string; init?: RequestInit };

function harness(answer: (path: string) => Response | Promise<Response>) {
  const anonymous: Call[] = [];
  const signedIn: Call[] = [];
  const api = createBookingApi(
    async (path, init) => {
      signedIn.push({ path, init });
      return answer(path);
    },
    async (path, init) => {
      anonymous.push({ path, init });
      return answer(path);
    },
  );
  return { api, anonymous, signedIn };
}

describe("the institution reads", () => {
  it("go without a session, with the slug and month in the path", async () => {
    const { api, anonymous, signedIn } = harness(() => json(200, { month: "2026-11", slots: [] }));

    await api.listInstitutions();
    await api.getInstitution("kandy home");
    const result = await api.getAvailability("kandy home", "2026-11");

    expect(anonymous.map((c) => c.path)).toEqual([
      "/api/institutions",
      "/api/institutions/kandy%20home",
      "/api/institutions/kandy%20home/availability?month=2026-11",
    ]);
    expect(signedIn).toEqual([]);
    expect(result).toEqual({ ok: true, value: { month: "2026-11", slots: [] } });
  });
});

describe("booking and cancelling", () => {
  it("takes a 204 as done: letting a regular day go answers with no body", async () => {
    const { api, signedIn } = harness(() => new Response(null, { status: 204 }));

    const result = await api.letGo("BK-2026-000123", "Away");

    expect(result.ok).toBe(true);
    expect(signedIn.map((c) => [c.path, c.init?.method])).toEqual([["/api/bookings/BK-2026-000123/skip", "POST"]]);
    expect(JSON.parse(String(signedIn[0].init?.body))).toEqual({ reason: "Away" });
  });

  it("go with the session, as JSON", async () => {
    const { api, anonymous, signedIn } = harness((path) =>
      path === "/api/bookings" ? json(201, { bookingNo: "BK-2026-000123", status: "Requested" }) : json(200, {}),
    );

    const booked = await api.book({ mealSlot: LUNCH, date: "2026-11-20", headcount: 25, fulfilment: "DonorBrings", inMemoryOf: null, notes: null });
    await api.cancel("BK-2026-000123", "Travelling");
    await api.listMine();

    expect(booked).toEqual({ ok: true, value: { bookingNo: "BK-2026-000123", status: "Requested" } });
    expect(anonymous).toEqual([]);
    expect(signedIn.map((c) => [c.path, c.init?.method ?? "GET"])).toEqual([
      ["/api/bookings", "POST"],
      ["/api/bookings/BK-2026-000123/cancel", "POST"],
      ["/api/bookings/mine", "GET"],
    ]);
    expect(JSON.parse(String(signedIn[0].init?.body))).toEqual({
      mealSlot: LUNCH,
      date: "2026-11-20",
      headcount: 25,
      fulfilment: "DonorBrings",
      inMemoryOf: null,
      notes: null,
    });
    expect(JSON.parse(String(signedIn[1].init?.body))).toEqual({ reason: "Travelling" });
  });
});

describe("a refusal", () => {
  it("carries the status, the code and the fields", async () => {
    const { api } = harness(() => json(400, { code: "invalid_details", fields: ["date", 7] }));
    expect(await api.book({ mealSlot: LUNCH, date: "x", headcount: 1, fulfilment: "DonorBrings", inMemoryOf: null, notes: null })).toEqual({
      ok: false,
      failure: { status: 400, code: "invalid_details", fields: ["date"] },
    });
  });

  it("is status 0 when the request never arrived, and code-less when the body is not the API's", async () => {
    expect(await harness(() => Promise.reject(new TypeError("Network request failed"))).api.listInstitutions()).toEqual({
      ok: false,
      failure: { status: 0, code: null, fields: [] },
    });
    expect(await harness(() => new Response("<html>Bad gateway</html>", { status: 502 })).api.listInstitutions()).toEqual({
      ok: false,
      failure: { status: 502, code: null, fields: [] },
    });
  });
});
