import type { Donor } from "./types";

/**
 * A Sri Lankan number as people here write it: +94771234567 becomes 077 123 4567. Anything else
 * (a foreign number) is shown as stored.
 */
export function localPhone(e164: string): string {
  const match = /^\+94(\d{2})(\d{3})(\d{4})$/.exec(e164);
  return match ? `0${match[1]} ${match[2]} ${match[3]}` : e164;
}

/** Digits only, with a leading 94 read as the 0 people type: "+94 77 123" and "077 123" both give "077123". */
function phoneDigits(text: string): string {
  const digits = text.replace(/\D/g, "");
  return digits.startsWith("94") ? `0${digits.slice(2)}` : digits;
}

/** Latin accents only: Sinhala and Tamil vowel signs are combining marks too, and carry meaning. */
function fold(text: string): string {
  return text
    .normalize("NFD")
    .replace(/[\u0300-\u036f]/g, "")
    .toLocaleLowerCase();
}

/**
 * The donors a search finds: every word in the name (any case, Latin accents ignored, Sinhala and
 * Tamil as written), or the phone however it is typed. An empty search finds everyone. The book's
 * order is kept.
 */
export function searchDonors(donors: readonly Donor[], query: string): Donor[] {
  const trimmed = query.trim();
  if (trimmed === "") return [...donors];

  const digits = phoneDigits(trimmed);
  if (/^[\d\s+()-]+$/.test(trimmed) && digits.length >= 3) {
    return donors.filter((donor) => phoneDigits(donor.phone).includes(digits));
  }

  const words = fold(trimmed).split(/\s+/);
  return donors.filter((donor) => words.every((word) => fold(donor.fullName).includes(word)));
}
