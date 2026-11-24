import type { MetadataRoute } from "next";
import { routing } from "@/i18n/routing";
import { inLocale, localName } from "./names";
import type { PublicInstitution } from "./types";

/*  What search engines read of an institution's page (plan 06, G9): its addresses in the three
    languages, the snippet, and schema.org structured data. Pure, so it is unit-tested; the page,
    its metadata and the sitemap all build their addresses here and cannot disagree.          */

/** An institution's page, without a locale. */
export function institutionPath(slug: string): string {
  return `/i/${encodeURIComponent(slug)}`;
}

/** `path` in `locale`: English unprefixed, Sinhala and Tamil prefixed (A11, `localePrefix: "as-needed"`). */
export function localePath(locale: string, path: string): string {
  if (locale === routing.defaultLocale) return path;
  return path === "/" ? `/${locale}` : `/${locale}${path}`;
}

/** The page in each language, by language code, and `x-default` (English): for `hreflang` and the sitemap. */
export function languageAlternates(path: string, origin = ""): Record<string, string> {
  return Object.fromEntries([
    ...routing.locales.map((locale) => [locale, origin + localePath(locale, path)]),
    ["x-default", origin + localePath(routing.defaultLocale, path)],
  ]);
}

/**
 * Sitemap entries for `paths` (without a locale): one per page per language, each naming all
 * three, as search engines ask of a site in several languages.
 */
export function sitemapEntries(paths: string[], origin: string): MetadataRoute.Sitemap {
  return paths.flatMap((path) => {
    const languages = languageAlternates(path, origin);
    return routing.locales.map((locale) => ({ url: origin + localePath(locale, path), alternates: { languages } }));
  });
}

/**
 * The screens behind a sign-in, and the design review page. Nothing in them is for a search
 * engine, and a crawler would only be shown the sign-in form.
 */
const privatePaths = [
  "/access",
  "/account",
  "/admin",
  "/apply",
  "/bookings",
  "/design",
  "/forgot-password",
  "/login",
  "/select-institution",
  "/services",
  "/signup",
  "/stepup",
];

/** robots.txt: everything public may be crawled, the private screens in every language may not, and where the sitemap is. */
export function robotsRules(origin: string): MetadataRoute.Robots {
  return {
    rules: {
      userAgent: "*",
      allow: "/",
      disallow: routing.locales.flatMap((locale) => privatePaths.map((path) => localePath(locale, path))),
    },
    sitemap: `${origin}/sitemap.xml`,
  };
}

/** The longest snippet search engines show in full. */
const maxDescription = 160;

/** The search snippet: the institution's own description, cut at a word before 160 characters, or `fallback`. */
export function metaDescription(description: string | null, fallback: string): string {
  const text = description?.replace(/\s+/g, " ").trim();
  if (!text) return fallback;
  if (text.length <= maxDescription) return text;

  const cut = text.slice(0, maxDescription - 1);
  const space = cut.lastIndexOf(" ");
  return `${(space > maxDescription / 2 ? cut.slice(0, space) : cut).trimEnd()}…`;
}

/** schema.org's own types for places of worship; a care home is a Place. */
const schemaTypes: Record<string, string> = {
  TEMPLE: "BuddhistTemple",
  KOVIL: "HinduTemple",
  MOSQUE: "Mosque",
  CHURCH: "Church",
};

/** schema.org structured data for the page at `url`, named in `locale`. */
export function institutionJsonLd(institution: PublicInstitution, locale: string, url: string) {
  const name = localName(locale, institution);
  const otherNames = [institution.name, institution.nameSi, institution.nameTa]
    .map((n) => n?.trim())
    .filter((n): n is string => !!n && n !== name);

  return {
    "@context": "https://schema.org",
    "@type": schemaTypes[institution.type] ?? "Place",
    name,
    ...(otherNames.length > 0 && { alternateName: otherNames }),
    ...(institution.description && { description: institution.description }),
    url,
    address: {
      "@type": "PostalAddress",
      streetAddress: [institution.addressLine1, institution.addressLine2].filter(Boolean).join(", "),
      addressLocality: institution.city,
      addressRegion: inLocale(locale, institution.district, institution.districtSi, institution.districtTa),
      addressCountry: "LK",
    },
    geo: { "@type": "GeoCoordinates", latitude: institution.latitude, longitude: institution.longitude },
  };
}

/**
 * JSON for a `<script type="application/ld+json">`. `<` is escaped, so nothing an institution
 * writes (a description with `</script>` in it) can end the script and start markup.
 */
export function jsonLdText(value: unknown): string {
  return JSON.stringify(value).replace(/</g, "\\u003c");
}
