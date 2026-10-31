/*  The platform lists' state in the address (plan 05, X4): search, filters, sort and page live in
    the query, so the back button and a shared link keep them. Parsing is forgiving: a value the
    list does not know falls back to its default instead of breaking the screen, and defaults are
    left out of the address, so a plain list has a plain URL.                                      */

export const PAGE_SIZE = 24;

export const userKinds = ["Donor", "InstitutionStaff", "PlatformStaff"] as const;
export const userStatuses = ["Active", "PendingVerification", "Suspended", "Deactivated"] as const;
export const institutionStatuses = ["Onboarding", "Active", "Suspended", "Deactivated"] as const;

export type UserQuery = {
  search: string;
  kind: (typeof userKinds)[number] | null;
  status: (typeof userStatuses)[number] | null;
  /** The slug of an institution they hold a role in now. */
  institution: string | null;
  sort: "newest" | "name";
  page: number;
};

export type InstitutionQuery = {
  search: string;
  status: (typeof institutionStatuses)[number] | null;
  /** An institution type's code. */
  type: string | null;
  /** A district's code. */
  district: string | null;
  sort: "name" | "newest";
  page: number;
};

type Params = { get(name: string): string | null };

const oneOf = <T extends string>(values: readonly T[], value: string | null): T | null =>
  value !== null && (values as readonly string[]).includes(value) ? (value as T) : null;

/** A code as the lookups give them: letters, digits, `_` and `-`, at most 30. */
const code = (value: string | null): string | null => (value && /^[A-Za-z0-9_-]{1,30}$/.test(value) ? value : null);

/** An institution's slug: lower-case letters, digits and `-`, as `org.Institutions.Slug` holds them. */
const slug = (value: string | null): string | null => (value && /^[a-z0-9-]{1,150}$/.test(value) ? value : null);

const pageOf = (value: string | null): number => {
  const page = Number(value);
  return Number.isInteger(page) && page >= 1 ? page : 1;
};

export function parseUserQuery(params: Params): UserQuery {
  return {
    search: params.get("q")?.trim() ?? "",
    kind: oneOf(userKinds, params.get("kind")),
    status: oneOf(userStatuses, params.get("status")),
    institution: slug(params.get("institution")),
    sort: params.get("sort") === "name" ? "name" : "newest",
    page: pageOf(params.get("page")),
  };
}

export function parseInstitutionQuery(params: Params): InstitutionQuery {
  return {
    search: params.get("q")?.trim() ?? "",
    status: oneOf(institutionStatuses, params.get("status")),
    type: code(params.get("type")),
    district: code(params.get("district")),
    sort: params.get("sort") === "newest" ? "newest" : "name",
    page: pageOf(params.get("page")),
  };
}

/** The address's query for a list's state, defaults left out ("" for a plain list). */
export function toQueryString(query: UserQuery | InstitutionQuery, defaultSort: string): string {
  const out = new URLSearchParams();
  if (query.search) out.set("q", query.search);
  for (const [name, value] of Object.entries(query)) {
    if (name === "search" || name === "sort" || name === "page" || value === null) continue;
    out.set(name, String(value));
  }
  if (query.sort !== defaultSort) out.set("sort", query.sort);
  if (query.page > 1) out.set("page", String(query.page));
  const text = out.toString();
  return text ? `?${text}` : "";
}

/** The API's query for a list's state: the same names, the search as `search`, and the page size. */
export function toApiQuery(query: UserQuery | InstitutionQuery): string {
  const out = new URLSearchParams({ search: query.search, sort: query.sort, page: String(query.page), pageSize: String(PAGE_SIZE) });
  for (const [name, value] of Object.entries(query)) {
    if (name !== "search" && name !== "sort" && name !== "page" && value !== null) out.set(name, String(value));
  }
  return out.toString();
}

/** "25-48" of a total: the rows a page shows, or null when it shows none. */
export function pageRange(page: number, pageSize: number, shown: number): { from: number; to: number } | null {
  if (shown === 0) return null;
  const from = (page - 1) * pageSize + 1;
  return { from, to: from + shown - 1 };
}

/** How many pages a total fills; at least one, so "page 1 of 1" reads right for an empty list. */
export function pageCount(total: number, pageSize: number): number {
  return Math.max(1, Math.ceil(total / pageSize));
}
