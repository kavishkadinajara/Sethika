/**
 * The name in the page's language when the institution gave one, else the English one: an
 * institution may name its slots only in English, and a Sinhala page must still show something.
 */
export function inLocale(locale: string, english: string, sinhala: string | null, tamil: string | null): string {
  const local = locale === "si" ? sinhala : locale === "ta" ? tamil : null;
  return local?.trim() ? local : english;
}

/** {@link inLocale} for anything with name, nameSi and nameTa. */
export function localName(locale: string, named: { name: string; nameSi: string | null; nameTa: string | null }): string {
  return inLocale(locale, named.name, named.nameSi, named.nameTa);
}
