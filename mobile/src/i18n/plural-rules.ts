/*  Hermes on the phone (Expo Go included) has no Intl.PluralRules, and intl-messageformat needs
    it for every {count, plural, ...} message: without it a screen with "2 places" crashes. The
    app speaks three languages, whose cardinal rules (CLDR) are short, so they are written here
    instead of adding @formatjs/intl-pluralrules (PHASE-F-MOBILE.md, F16: no new packages yet).
    Installed only where the engine has none; Node and browsers keep their own.             */

type Category = "zero" | "one" | "two" | "few" | "many" | "other";

/** CLDR cardinal rules for en, si and ta; any other locale gets the English rule. */
export function cardinal(locale: string, n: number): Category {
  const value = Math.abs(n);
  const integer = Math.floor(value);
  const fraction = String(value).split(".")[1] ?? ""; // f, the visible fraction digits
  const visible = fraction.length; // v
  const language = locale.toLowerCase().split(/[-_]/)[0];

  if (language === "si") {
    // one: n = 0,1 or i = 0 and f = 1
    return value === 0 || value === 1 || (integer === 0 && fraction === "1") ? "one" : "other";
  }
  if (language === "ta") {
    // one: n = 1
    return value === 1 ? "one" : "other";
  }
  // en: one: i = 1 and v = 0
  return integer === 1 && visible === 0 ? "one" : "other";
}

class PluralRulesShim {
  private readonly locale: string;
  private readonly type: "cardinal" | "ordinal";

  constructor(locales?: string | string[], options?: { type?: "cardinal" | "ordinal" }) {
    this.locale = (Array.isArray(locales) ? locales[0] : locales) ?? "en";
    this.type = options?.type ?? "cardinal";
  }

  select(n: number): Category {
    // No message here uses selectordinal; "other" keeps one if it ever appears.
    return this.type === "ordinal" ? "other" : cardinal(this.locale, n);
  }

  resolvedOptions() {
    return { locale: this.locale, type: this.type, pluralCategories: ["one", "other"] as Category[] };
  }

  static supportedLocalesOf(locales: string | string[]): string[] {
    return Array.isArray(locales) ? locales : [locales];
  }
}

/** Puts the shim on Intl when the engine has no PluralRules. Returns whether it did. */
export function installPluralRules(intl: { PluralRules?: unknown } = Intl as never): boolean {
  if (typeof intl.PluralRules === "function") return false;
  intl.PluralRules = PluralRulesShim;
  return true;
}
