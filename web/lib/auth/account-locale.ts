/*  A11: a signed-in user's account language comes before the NEXT_LOCALE cookie, so a donor who
    chose Sinhala on one phone opens in Sinhala on the next. The cookie, Accept-Language and the
    default still decide for visitors (proxy.ts); this only ever moves a signed-in user.        */

/**
 * The locale to move to so the page matches the account, or null to stay. Null too for a
 * language this site does not serve, so a value the API grows later never breaks routing.
 */
export function accountLocaleToAdopt<L extends string>(
  accountLanguage: string | null,
  currentLocale: string,
  locales: readonly L[],
): L | null {
  if (!accountLanguage || accountLanguage === currentLocale) return null;
  return locales.find((locale) => locale === accountLanguage) ?? null;
}
