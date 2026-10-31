import { describe, expect, it } from "vitest";
import { pageCount, pageRange, parseInstitutionQuery, parseUserQuery, toApiQuery, toQueryString } from "./list-query";

const params = (query: string) => new URLSearchParams(query);

describe("the accounts list's address", () => {
  it("reads every part, and leaves the defaults out when written back", () => {
    const query = parseUserQuery(params("q=%20Amal%20&kind=Donor&status=Suspended&sort=name&page=3"));
    expect(query).toEqual({ search: "Amal", kind: "Donor", status: "Suspended", sort: "name", page: 3 });
    expect(toQueryString(query, "newest")).toBe("?q=Amal&kind=Donor&status=Suspended&sort=name&page=3");
    expect(toQueryString(parseUserQuery(params("")), "newest")).toBe("");
  });

  it("falls back to the default for anything it does not know", () => {
    expect(parseUserQuery(params("kind=Robot&status=Gone&sort=oldest&page=-2"))).toEqual({
      search: "",
      kind: null,
      status: null,
      sort: "newest",
      page: 1,
    });
    expect(parseUserQuery(params("page=2.5")).page).toBe(1);
    expect(parseUserQuery(params("page=abc")).page).toBe(1);
  });

  it("asks the API for the same state, with the page size", () => {
    expect(toApiQuery(parseUserQuery(params("q=Amal&kind=Donor&page=2")))).toBe("search=Amal&sort=newest&page=2&pageSize=24&kind=Donor");
  });
});

describe("the institutions list's address", () => {
  it("reads codes for the type and the district, and its own default sort", () => {
    const query = parseInstitutionQuery(params("status=Active&type=TEMPLE&district=CMB&sort=newest"));
    expect(query).toEqual({ search: "", status: "Active", type: "TEMPLE", district: "CMB", sort: "newest", page: 1 });
    expect(toQueryString(query, "name")).toBe("?status=Active&type=TEMPLE&district=CMB&sort=newest");
    expect(toQueryString(parseInstitutionQuery(params("sort=name")), "name")).toBe("");
  });

  it("refuses a code that is not one, so nothing odd reaches the API", () => {
    const query = parseInstitutionQuery(params(`type=${"X".repeat(31)}&district=a%20b`));
    expect(query.type).toBeNull();
    expect(query.district).toBeNull();
  });
});

describe("the page's words", () => {
  it("names the rows a page shows", () => {
    expect(pageRange(1, 24, 24)).toEqual({ from: 1, to: 24 });
    expect(pageRange(3, 24, 5)).toEqual({ from: 49, to: 53 });
    expect(pageRange(9, 24, 0)).toBeNull();
  });

  it("counts the pages, one at least", () => {
    expect(pageCount(0, 24)).toBe(1);
    expect(pageCount(24, 24)).toBe(1);
    expect(pageCount(25, 24)).toBe(2);
  });
});
