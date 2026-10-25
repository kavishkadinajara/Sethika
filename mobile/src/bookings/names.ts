import type { Language } from "@/i18n/translate";

/**
 * The name in the screen's language when the institution gave one, else the English one: an
 * institution may name its meals only in English, and a Sinhala screen must still show something.
 */
export function inLocale(language: Language, english: string, sinhala: string | null, tamil: string | null): string {
  const local = language === "si" ? sinhala : language === "ta" ? tamil : null;
  return local?.trim() ? local : english;
}

/** {@link inLocale} for anything with name, nameSi and nameTa. */
export function localName(language: Language, named: { name: string; nameSi: string | null; nameTa: string | null }): string {
  return inLocale(language, named.name, named.nameSi, named.nameTa);
}

/**
 * Rupees as "LKR 4,500" (or "LKR 4,500.50"). Written out rather than through Intl's currency
 * format, which Hermes takes from the phone's ICU and which differs between phones; the ISO code
 * reads the same in all three languages, and the web's Sinhala page shows the same.
 */
export function formatLkr(amount: number): string {
  const cents = Math.round(amount * 100);
  const whole = Math.trunc(cents / 100);
  const fraction = Math.abs(cents % 100);
  const grouped = String(Math.abs(whole)).replace(/\B(?=(\d{3})+(?!\d))/g, ",");
  return `LKR ${cents < 0 ? "-" : ""}${grouped}${fraction ? `.${String(fraction).padStart(2, "0")}` : ""}`;
}
