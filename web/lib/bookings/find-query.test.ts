import { describe, expect, it } from "vitest";
import { defaultDate, inSriLanka, memorialSearch, monthApiQuery, parseFindQuery, roundCoordinate, searchApiQuery, toFindSearch, type FindQuery } from "./find-query";

const today = "2026-11-01";
const parse = (search: string) => parseFindQuery(new URLSearchParams(search), today);

describe("parseFindQuery", () => {
  it("defaults to tomorrow, any time, any kind of place, anywhere, the list", () => {
    expect(parse("")).toEqual({ date: "2026-11-02", time: "any", type: null, where: { kind: "anywhere" }, view: "list", memorial: null });
  });

  it("reads every filter", () => {
    expect(parse("date=2026-12-14&time=midday&type=TEMPLE&near=6.033,80.217&radius=25&view=map")).toEqual({
      date: "2026-12-14",
      time: "midday",
      type: "TEMPLE",
      where: { kind: "near", latitude: 6.033, longitude: 80.217, radiusKm: 25 },
      view: "map",
      memorial: null,
    });
    expect(parse("district=GLL").where).toEqual({ kind: "district", district: "GLL" });
  });

  it("falls back to the default for what it does not know", () => {
    expect(parse("date=2026-10-31").date).toBe("2026-11-02");
    expect(parse("date=14-03-2027").date).toBe("2026-11-02");
    expect(parse("time=brunch").time).toBe("any");
    expect(parse("type=<script>").type).toBeNull();
    expect(parse("near=6.9,79.8&radius=7").where).toEqual({ kind: "near", latitude: 6.9, longitude: 79.8, radiusKm: 10 });
    expect(parse("view=grid").view).toBe("list");
  });

  it("refuses a point outside Sri Lanka, and rounds one inside it to about 100 m", () => {
    expect(parse("near=13.0827,80.2707").where).toEqual({ kind: "anywhere" });
    expect(parse("near=6.0329451,80.2168012").where).toEqual({ kind: "near", latitude: 6.033, longitude: 80.217, radiusKm: 10 });
  });

  it("takes the point over a district when both are in the address", () => {
    expect(parse("near=6.9,79.8&district=GLL").where.kind).toBe("near");
  });
});

describe("toFindSearch", () => {
  const plain: FindQuery = { date: defaultDate(today), time: "any", type: null, where: { kind: "anywhere" }, view: "list", memorial: null };

  it("leaves the defaults out", () => {
    expect(toFindSearch(plain, today)).toBe("");
  });

  it("writes what was chosen, and reads back the same", () => {
    const chosen: FindQuery = { date: "2026-12-14", time: "evening", type: "KOVIL", where: { kind: "near", latitude: 7.2906, longitude: 80.6337, radiusKm: 50 }, view: "map", memorial: null };
    const search = toFindSearch(chosen, today);
    expect(search).toBe("?date=2026-12-14&time=evening&type=KOVIL&near=7.291%2C80.634&radius=50&view=map");
    expect(parse(search.slice(1))).toEqual({ ...chosen, where: { kind: "near", latitude: 7.291, longitude: 80.634, radiusKm: 50 } });
  });

  it("writes a district, and the default radius not at all", () => {
    expect(toFindSearch({ ...plain, where: { kind: "district", district: "GLL" } }, today)).toBe("?district=GLL");
    expect(toFindSearch({ ...plain, where: { kind: "near", latitude: 6.9, longitude: 79.8, radiusKm: 10 } }, today)).toBe("?near=6.9%2C79.8");
  });
});

describe("a memorial observance", () => {
  const plan = "0196f3a2-7b1c-7d2e-9f00-112233445566";

  it("is kept from the address only when both parts are well formed", () => {
    expect(parse(`plan=${plan}&rule=BUDDHIST_7TH_DAY`).memorial).toEqual({ plan, rule: "BUDDHIST_7TH_DAY" });
    expect(parse(`plan=${plan.toUpperCase()}&rule=BUDDHIST_7TH_DAY`).memorial).toEqual({ plan, rule: "BUDDHIST_7TH_DAY" });
    expect(parse(`plan=${plan}`).memorial).toBeNull();
    expect(parse("plan=not-a-plan&rule=BUDDHIST_7TH_DAY").memorial).toBeNull();
    expect(parse(`plan=${plan}&rule=bad rule`).memorial).toBeNull();
  });

  it("is written back into the address and a booking link", () => {
    const memorial = { plan, rule: "HINDU_31ST_DAY" };
    const plain: FindQuery = { date: defaultDate(today), time: "any", type: null, where: { kind: "anywhere" }, view: "list", memorial };
    expect(toFindSearch(plain, today)).toBe(`?plan=${plan}&rule=HINDU_31ST_DAY`);
    expect(memorialSearch(memorial)).toBe(`&plan=${plan}&rule=HINDU_31ST_DAY`);
    expect(memorialSearch(null)).toBe("");
  });
});

describe("the API's queries", () => {
  const query: FindQuery = { date: "2026-12-14", time: "midday", type: "TEMPLE", where: { kind: "near", latitude: 6.03294, longitude: 80.21681, radiusKm: 10 }, view: "map", memorial: null };

  it("sends the day, the filters and a rounded point, never the view", () => {
    expect(searchApiQuery(query)).toBe("time=midday&type=TEMPLE&near=6.033%2C80.217&radiusKm=10&date=2026-12-14");
    expect(monthApiQuery({ ...query, time: "any", type: null, where: { kind: "district", district: "GLL" } }, "2026-12")).toBe("district=GLL&month=2026-12");
  });
});

describe("coordinates", () => {
  it("rounds to 3 decimals", () => {
    expect(roundCoordinate(6.92714)).toBe(6.927);
    expect(roundCoordinate(79.86155)).toBe(79.862);
  });

  it("knows Sri Lanka's box", () => {
    expect(inSriLanka(6.9271, 79.8612)).toBe(true);
    expect(inSriLanka(5.49, 80)).toBe(false);
    expect(inSriLanka(7, 82.2)).toBe(false);
  });
});
