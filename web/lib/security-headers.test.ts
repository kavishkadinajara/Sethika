import { describe, expect, it } from "vitest";
import { securityHeaderRules, securityHeaders } from "./security-headers";

const valueOf = (key: string) => securityHeaders.find((h) => h.key === key)?.value;

describe("security headers (W1)", () => {
  it("refuse framing by any site, in the CSP and for browsers that only read X-Frame-Options", () => {
    expect(valueOf("Content-Security-Policy")?.split(";").map((d) => d.trim())).toContain("frame-ancestors 'none'");
    expect(valueOf("X-Frame-Options")).toBe("DENY");
  });

  it("hold no CSP directive that could block a script, a style, a tile or the checkout", () => {
    const directives = valueOf("Content-Security-Policy")!.split(";").map((d) => d.trim().split(" ")[0]);
    expect(directives.sort()).toEqual(["base-uri", "frame-ancestors", "object-src"]);
  });

  it("turn sniffing off, trim the referrer, and keep geolocation for this origin only", () => {
    expect(valueOf("X-Content-Type-Options")).toBe("nosniff");
    expect(valueOf("Referrer-Policy")).toBe("strict-origin-when-cross-origin");
    expect(valueOf("Permissions-Policy")).toContain("geolocation=(self)");
    expect(valueOf("Permissions-Policy")).toContain("camera=()");
  });

  it("ask for HTTPS for two years", () => {
    expect(valueOf("Strict-Transport-Security")).toMatch(/^max-age=63072000(;|$)/);
  });

  it("apply to every path, the /api rewrite included", () => {
    const rules = securityHeaderRules();
    expect(rules).toHaveLength(1);
    expect(rules[0].source).toBe("/:path*");
    expect(rules[0].headers.map((h) => h.key)).toEqual(securityHeaders.map((h) => h.key));
  });
});
