import { IntlMessageFormat } from "intl-messageformat";

import { cardinal, installPluralRules } from "./plural-rules";

/*  The phone's engine has no Intl.PluralRules; these check the stand-in against CLDR for the
    app's three languages, and that a plural message formats through it.                     */

describe("cardinal", () => {
  it("follows CLDR for English: one is exactly 1", () => {
    expect([0, 1, 2, 1.5, 21].map((n) => cardinal("en", n))).toEqual(["other", "one", "other", "other", "other"]);
  });

  it("follows CLDR for Sinhala: one is 0 and 1", () => {
    expect([0, 1, 2, 0.1, 5].map((n) => cardinal("si", n))).toEqual(["one", "one", "other", "one", "other"]);
  });

  it("follows CLDR for Tamil: one is exactly 1", () => {
    expect([0, 1, 2].map((n) => cardinal("ta", n))).toEqual(["other", "one", "other"]);
  });
});

describe("installPluralRules", () => {
  it("leaves an engine's own PluralRules alone", () => {
    const own = function PluralRules() {};
    const intl = { PluralRules: own };
    expect(installPluralRules(intl)).toBe(false);
    expect(intl.PluralRules).toBe(own);
  });

  it("formats plural messages where the engine has none, as Hermes", () => {
    const real = Intl.PluralRules;
    try {
      // @ts-expect-error: removing it to be the phone's engine
      delete Intl.PluralRules;
      expect(installPluralRules()).toBe(true);
      const places = new IntlMessageFormat("{count, plural, one {# place} other {# places}}", "en");
      expect(places.format({ count: 1 })).toBe("1 place");
      expect(places.format({ count: 8 })).toBe("8 places");
      const when = new IntlMessageFormat("{days, plural, =0 {Today} =1 {Tomorrow} other {In # days}}", "en");
      expect(when.format({ days: 1 })).toBe("Tomorrow");
      expect(when.format({ days: 3 })).toBe("In 3 days");
    } finally {
      (Intl as { PluralRules: unknown }).PluralRules = real;
    }
  });
});
