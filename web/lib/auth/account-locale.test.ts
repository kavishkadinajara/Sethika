import { describe, expect, it } from "vitest";
import { accountLocaleToAdopt } from "./account-locale";

const locales = ["en", "si", "ta"] as const;

describe("accountLocaleToAdopt (A11: the account's language before the cookie)", () => {
  it.each([
    ["si", "en", "si"],
    ["ta", "si", "ta"],
    ["en", "ta", "en"],
  ])("account %j on a %j page moves to %j", (account, current, expected) => {
    expect(accountLocaleToAdopt(account, current, locales)).toBe(expected);
  });

  it("stays when the page is already in the account's language", () => {
    expect(accountLocaleToAdopt("si", "si", locales)).toBeNull();
  });

  it("stays when the account's language is unknown", () => {
    expect(accountLocaleToAdopt(null, "si", locales)).toBeNull();
  });

  it("stays for a language this site does not serve", () => {
    expect(accountLocaleToAdopt("fr", "en", locales)).toBeNull();
  });
});
