/*  Browser security headers for every response this origin serves, pages and the /api proxy alike
    (hardening 2026-11-08, finding W1). next.config.ts applies them; kept here, free of Next, so a
    unit test can read exactly what is sent.

    - Framing is refused outright (`frame-ancestors 'none'`, and X-Frame-Options for older browsers):
      no screen here is meant to sit inside another site, and a framed approve, void or grant button
      is a click someone else chose (clickjacking).
    - The CSP holds only directives that cannot break a page: no script or style rules, which Next's
      inline scripts, Leaflet's tiles, SignalR and PayHere's checkout would each need tuned and
      walked. `base-uri` and `object-src` close two XSS footholds that nothing here uses.
    - Geolocation stays for this origin: /find's "near me" and the onboarding map ask for it.
    - HSTS is stated here, not left to the host, so it does not depend on which one serves the app.
      Browsers ignore it over plain http, so local development is unaffected.                     */

export type SecurityHeader = { key: string; value: string };

export const securityHeaders: readonly SecurityHeader[] = [
  { key: "Content-Security-Policy", value: "frame-ancestors 'none'; base-uri 'self'; object-src 'none'" },
  { key: "X-Frame-Options", value: "DENY" },
  { key: "X-Content-Type-Options", value: "nosniff" },
  { key: "Referrer-Policy", value: "strict-origin-when-cross-origin" },
  { key: "Permissions-Policy", value: "camera=(), microphone=(), payment=(), usb=(), geolocation=(self)" },
  { key: "Strict-Transport-Security", value: "max-age=63072000; includeSubDomains" },
];

/** The `headers()` entry for next.config.ts: every path, including the /api rewrite. */
export function securityHeaderRules() {
  return [{ source: "/:path*", headers: securityHeaders.map((h) => ({ ...h })) }];
}
