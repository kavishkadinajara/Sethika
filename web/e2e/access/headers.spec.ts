import { securityHeaders } from "../../lib/security-headers";
import { expect, test } from "../support/test";

/*  W1, hardening 2026-11-08: the browser security headers reach the browser.
    lib/security-headers.test.ts checks the web's list; this checks that Next sends it on a page,
    which a typo in next.config.ts would quietly stop. Next's headers() did not reach a response
    the /api rewrite carried (seen with `next dev`), so the API sends its own stricter set
    (ApiSecurityHeaders.cs), and that is what a proxied /api response must carry.             */

test("a page carries every web security header", async ({ request }) => {
  const response = await request.get("/en");
  expect(response.status()).toBe(200);

  const headers = response.headers();
  for (const { key, value } of securityHeaders) {
    expect(headers[key.toLowerCase()], key).toBe(value);
  }
});

test("an /api response through the rewrite carries the API's own headers", async ({ request }) => {
  const response = await request.get("/api/legal/current");
  expect(response.status()).toBe(200);

  const headers = response.headers();
  expect(headers["content-security-policy"]).toBe("default-src 'none'; frame-ancestors 'none'");
  expect(headers["x-frame-options"]).toBe("DENY");
  expect(headers["x-content-type-options"]).toBe("nosniff");
  expect(headers["referrer-policy"]).toBe("no-referrer");
});
