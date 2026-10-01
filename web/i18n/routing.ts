import { defineRouting } from "next-intl/routing";

/**
 * A11: English is unprefixed (`/login`), Sinhala and Tamil are prefixed (`/si/login`,
 * `/ta/login`), so Phase B's public pages are indexable per language.
 *
 * Resolution without a prefix: NEXT_LOCALE cookie, then Accept-Language, then `en`.
 * The signed-in user's PreferredLanguage goes in front of these once the API serves it.
 */
export const routing = defineRouting({
  locales: ["en", "si", "ta"],
  defaultLocale: "en",
  localePrefix: "as-needed",
  localeCookie: {
    name: "NEXT_LOCALE",
    maxAge: 60 * 60 * 24 * 365,
  },
});
