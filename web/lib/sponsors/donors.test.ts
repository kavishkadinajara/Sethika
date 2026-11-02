import { describe, expect, it } from "vitest";
import { localPhone, searchDonors } from "./donors";
import type { Donor } from "./types";

const donor = (fullName: string, phone: string): Donor => ({
  publicId: fullName,
  fullName,
  phone,
  notes: null,
  language: "si",
  hasAccount: false,
  textConsentAt: null,
  isActive: true,
  activeSponsorships: 0,
  version: "AAAAAAAAB9E=",
});

const book = [
  donor("Sunil Perera", "+94771234567"),
  donor("Kamala Perera", "+94719876543"),
  donor("José Fernando", "+94112345678"),
  donor("Renée Silva", "+94812000111"),
  donor("සුනිල් පෙරේරා", "+94765550000"),
  donor("Abroad Friend", "+447700900123"),
];

const names = (query: string) => searchDonors(book, query).map((d) => d.fullName);

describe("localPhone", () => {
  it("writes a Sri Lankan number the way people here do", () => {
    expect(localPhone("+94771234567")).toBe("077 123 4567");
    expect(localPhone("+94112345678")).toBe("011 234 5678");
  });

  it("leaves any other number as stored", () => {
    expect(localPhone("+447700900123")).toBe("+447700900123");
    expect(localPhone("+9477123")).toBe("+9477123");
  });
});

describe("searchDonors", () => {
  it("finds everyone, in the book's order, for an empty search", () => {
    expect(names("")).toEqual(book.map((d) => d.fullName));
    expect(names("   ")).toHaveLength(book.length);
  });

  it("needs every word of the name, in any case and order", () => {
    expect(names("perera")).toEqual(["Sunil Perera", "Kamala Perera"]);
    expect(names("PERERA sunil")).toEqual(["Sunil Perera"]);
    expect(names("perera nimal")).toEqual([]);
  });

  it("ignores Latin accents, and matches Sinhala as written", () => {
    expect(names("jose")).toEqual(["José Fernando"]);
    // Here the accent sits inside the word: only folding it away finds her.
    expect(names("renee")).toEqual(["Renée Silva"]);
    expect(names("සුනිල්")).toEqual(["සුනිල් පෙරේරා"]);
    // A vowel sign is part of the letter: without it, it is another word.
    expect(names("සුනල්")).toEqual([]);
  });

  it("finds a phone however it is typed", () => {
    expect(names("077 123")).toEqual(["Sunil Perera"]);
    expect(names("+94 77 123 4567")).toEqual(["Sunil Perera"]);
    expect(names("0771234567")).toEqual(["Sunil Perera"]);
    expect(names("71234")).toEqual(["Sunil Perera"]);
    expect(names("7700")).toEqual(["Abroad Friend"]);
  });

  it("reads two digits as too few to be a phone", () => {
    expect(names("07")).toEqual([]);
  });
});
