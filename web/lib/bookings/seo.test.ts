import { describe, expect, it } from "vitest";
import {
  institutionJsonLd,
  institutionPath,
  jsonLdText,
  languageAlternates,
  localePath,
  metaDescription,
  robotsRules,
  sitemapEntries,
} from "./seo";
import type { PublicInstitution } from "./types";

const temple: PublicInstitution = {
  slug: "sri-bodhi-vihara",
  name: "Sri Bodhi Vihara",
  nameSi: "ශ්‍රී බෝධි විහාරය",
  nameTa: null,
  type: "TEMPLE",
  typeName: "Buddhist Temple",
  typeNameSi: null,
  typeNameTa: null,
  district: "Galle",
  districtSi: "ගාල්ල",
  districtTa: "காலி",
  city: "Hikkaduwa",
  addressLine1: "12 Temple Road",
  addressLine2: "Wewala",
  description: "A village temple by the sea.",
  dietaryNotes: null,
  latitude: 6.1395,
  longitude: 80.1063,
  slots: [],
  services: [],
};

describe("addresses in three languages", () => {
  it("leaves English unprefixed and prefixes Sinhala and Tamil, as the router does (A11)", () => {
    expect(localePath("en", "/i/x")).toBe("/i/x");
    expect(localePath("si", "/i/x")).toBe("/si/i/x");
    expect(localePath("ta", "/i/x")).toBe("/ta/i/x");
    expect(localePath("en", "/")).toBe("/");
    expect(localePath("si", "/")).toBe("/si");
  });

  it("encodes the slug", () => {
    expect(institutionPath("a b/c")).toBe("/i/a%20b%2Fc");
  });

  it("names every language and English as the default", () => {
    expect(languageAlternates("/i/x", "https://sethika.com")).toEqual({
      en: "https://sethika.com/i/x",
      si: "https://sethika.com/si/i/x",
      ta: "https://sethika.com/ta/i/x",
      "x-default": "https://sethika.com/i/x",
    });
  });

  it("lists each page once per language, each entry naming all three", () => {
    const entries = sitemapEntries(["/", "/i/x"], "https://sethika.com");
    expect(entries.map((e) => e.url)).toEqual([
      "https://sethika.com/",
      "https://sethika.com/si",
      "https://sethika.com/ta",
      "https://sethika.com/i/x",
      "https://sethika.com/si/i/x",
      "https://sethika.com/ta/i/x",
    ]);
    expect(entries[4].alternates?.languages).toEqual(languageAlternates("/i/x", "https://sethika.com"));
  });
});

describe("metaDescription", () => {
  it("is the institution's own words when it wrote some, with the spacing tidied", () => {
    expect(metaDescription("  A village   temple\nby the sea. ", "fallback")).toBe("A village temple by the sea.");
  });

  it("falls back when there is no description", () => {
    expect(metaDescription(null, "Sri Bodhi Vihara, Hikkaduwa.")).toBe("Sri Bodhi Vihara, Hikkaduwa.");
    expect(metaDescription("   ", "fallback")).toBe("fallback");
  });

  it("cuts a long one at a word, inside 160 characters", () => {
    const long = "word ".repeat(60).trim();
    const cut = metaDescription(long, "fallback");
    expect(cut.length).toBeLessThanOrEqual(160);
    expect(cut.endsWith("word…")).toBe(true);
    expect(metaDescription("x".repeat(200), "fallback")).toBe(`${"x".repeat(159)}…`);
  });
});

describe("institutionJsonLd", () => {
  it("describes the place in schema.org's terms, named in the page's language", () => {
    const data = institutionJsonLd(temple, "si", "https://sethika.com/si/i/sri-bodhi-vihara");

    expect(data).toEqual({
      "@context": "https://schema.org",
      "@type": "BuddhistTemple",
      name: "ශ්‍රී බෝධි විහාරය",
      alternateName: ["Sri Bodhi Vihara"],
      description: "A village temple by the sea.",
      url: "https://sethika.com/si/i/sri-bodhi-vihara",
      address: {
        "@type": "PostalAddress",
        streetAddress: "12 Temple Road, Wewala",
        addressLocality: "Hikkaduwa",
        addressRegion: "ගාල්ල",
        addressCountry: "LK",
      },
      geo: { "@type": "GeoCoordinates", latitude: 6.1395, longitude: 80.1063 },
    });
  });

  it("uses each faith's own type, and Place for a care home", () => {
    const typeOf = (type: string) => institutionJsonLd({ ...temple, type }, "en", "u")["@type"];
    expect(["TEMPLE", "KOVIL", "MOSQUE", "CHURCH", "ELDERS_HOME", "CHILDRENS_HOME"].map(typeOf)).toEqual([
      "BuddhistTemple",
      "HinduTemple",
      "Mosque",
      "Church",
      "Place",
      "Place",
    ]);
  });

  it("leaves out what the institution did not give", () => {
    const data = institutionJsonLd({ ...temple, nameSi: null, description: null, addressLine2: null }, "en", "u");
    expect(data).not.toHaveProperty("alternateName");
    expect(data).not.toHaveProperty("description");
    expect(data.address.streetAddress).toBe("12 Temple Road");
  });

  it("cannot be closed early by what an institution writes", () => {
    const text = jsonLdText(institutionJsonLd({ ...temple, description: "</script><script>alert(1)</script>" }, "en", "u"));
    expect(text).not.toContain("<");
    expect(JSON.parse(text).description).toBe("</script><script>alert(1)</script>");
  });
});

describe("robotsRules", () => {
  it("keeps crawlers out of the signed-in screens in every language, and points at the sitemap", () => {
    const robots = robotsRules("https://sethika.com");
    const rules = Array.isArray(robots.rules) ? robots.rules[0] : robots.rules;

    expect(rules.allow).toBe("/");
    expect(rules.disallow).toEqual(expect.arrayContaining(["/login", "/si/login", "/ta/login", "/admin", "/ta/bookings", "/si/services"]));

    // robots.txt matches by prefix: no rule may reach a public page.
    const disallowed = [rules.disallow ?? []].flat();
    for (const page of ["/", "/si", "/i/x", "/ta/i/x", "/institutions", "/si/institutions", "/find"]) {
      expect(disallowed.filter((rule) => page.startsWith(rule))).toEqual([]);
    }
    expect(robots.sitemap).toBe("https://sethika.com/sitemap.xml");
  });
});
