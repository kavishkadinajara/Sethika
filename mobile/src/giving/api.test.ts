import { createGivingApi } from "./api";

const json = (status: number, body: unknown) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

describe("createGivingApi", () => {
  it("starts a gift on the donor's session and returns PayHere's signed form", async () => {
    const calls: { path: string; init?: RequestInit }[] = [];
    const api = createGivingApi(async (path, init) => {
      calls.push({ path, init });
      return json(201, { orderId: "o-1", action: "https://sandbox.payhere.lk/pay/checkout", fields: { hash: "H" } });
    });

    const result = await api.startGift("sri bodhi", { amount: 1500, need: null, email: "a@b.lk", address: "1 Road", city: "Kandy" });

    expect(result).toEqual({ ok: true, value: { orderId: "o-1", action: "https://sandbox.payhere.lk/pay/checkout", fields: { hash: "H" } } });
    expect(calls[0].path).toBe("/api/institutions/sri%20bodhi/give");
    expect(calls[0].init?.method).toBe("POST");
    expect(JSON.parse(calls[0].init?.body as string)).toEqual({ amount: 1500, need: null, email: "a@b.lk", address: "1 Road", city: "Kandy" });
  });

  it("reads an order as the notification left it", async () => {
    const api = createGivingApi(async () => json(200, { orderId: "o-1", status: "Paid" }));
    expect(await api.getMyPayment("o-1")).toEqual({ ok: true, value: { orderId: "o-1", status: "Paid" } });
  });

  it("turns refusals and a lost connection into failures the sheet can word", async () => {
    const refused = createGivingApi(async () => json(400, { code: "invalid_details", fields: ["email", 7] }));
    expect(await refused.startGift("x", { amount: 100, need: null, email: "", address: "", city: "" })).toEqual({
      ok: false,
      failure: { status: 400, code: "invalid_details", fields: ["email"] },
    });

    const offline = createGivingApi(async () => {
      throw new TypeError("Network request failed");
    });
    expect(await offline.getMyPayment("o-1")).toEqual({ ok: false, failure: { status: 0, code: null, fields: [] } });
  });
});
