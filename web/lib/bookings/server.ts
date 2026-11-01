import { headers } from "next/headers";
import { cache } from "react";
import { apiOrigin } from "@/lib/site";
import type { PublicInstitution, PublicInstitutionSummary } from "./types";

/*  The anonymous institution reads, made by the web server while it renders (plan 06, B.3).
    Server only: they read the request's headers, which a client component cannot.

    The API limits these reads per client address. Called from here, every page would come from
    the web server's one address and share one budget, so the visitor's address goes along in
    X-Forwarded-For. The last entry is taken: it is the one the nearest proxy (Vercel, or the
    host's own) wrote, while earlier ones are whatever the client sent. The API believes it only
    from a proxy it trusts (Program.cs, ForwardedHeaders).                                     */

export type ServerRead<T> = { status: "ok"; value: T } | { status: "not_found" } | { status: "unavailable" };

async function visitorAddress(): Promise<Record<string, string>> {
  const forwarded = (await headers()).get("x-forwarded-for");
  const last = forwarded?.split(",").at(-1)?.trim();
  return last ? { "x-forwarded-for": last } : {};
}

async function get(path: string): Promise<Response> {
  return fetch(`${apiOrigin()}${path}`, {
    headers: { accept: "application/json", ...(await visitorAddress()) },
    // Read for this request: a page must not show an institution that has since gone.
    cache: "no-store",
  });
}

/**
 * An active institution's public page data. Once per request, however many times it is asked for:
 * the page and its metadata both need it. `unavailable` when the API cannot answer (down, or
 * this visitor's limit spent): the page then loads it in the browser, as it did before B.3.
 */
export const getPublicInstitution = cache(async (slug: string): Promise<ServerRead<PublicInstitution>> => {
  try {
    const response = await get(`/api/institutions/${encodeURIComponent(slug)}`);
    if (response.status === 404) return { status: "not_found" };
    if (!response.ok) return { status: "unavailable" };
    return { status: "ok", value: (await response.json()) as PublicInstitution };
  } catch {
    return { status: "unavailable" };
  }
});

/** The publicly listed institutions, for the sitemap. Throws if the API cannot answer: a sitemap that silently lost every institution would be worse than none. */
export async function listPublicInstitutions(): Promise<PublicInstitutionSummary[]> {
  const response = await get("/api/institutions");
  if (!response.ok) throw new Error(`GET /api/institutions answered ${response.status}`);
  return (await response.json()) as PublicInstitutionSummary[];
}
