/*  Where this site and the API live, for code that runs on the server (plan 06, B.3): the
    institution pages render on the server, and canonical links and the sitemap need absolute
    addresses. Both are server-only settings (no NEXT_PUBLIC_), like API_BASE_URL in
    next.config.ts: the browser never needs them.                                            */

/** The API's origin, with the same default as next.config.ts's rewrite (the API's `http` profile). */
export function apiOrigin(): string {
  return (process.env.API_BASE_URL ?? "http://localhost:5182").replace(/\/+$/, "");
}

/** This site's public origin, for canonical links, `hreflang` and the sitemap. `SITE_URL` in production. */
export function siteOrigin(): string {
  return (process.env.SITE_URL ?? "http://localhost:3000").replace(/\/+$/, "");
}
