/*  The institution's donor book (plan 08, K1), as GET/POST/PUT /api/institution/donors send it. */

/** The language of a donor's texts (K10). */
export type DonorLanguage = "si" | "ta" | "en";

/** A person in the donor book (DonorView). */
export type Donor = {
  publicId: string;
  fullName: string;
  /** E.164, for example +94771234567. */
  phone: string;
  notes: string | null;
  language: DonorLanguage;
  /** Linked to a Sethika account (K14): they see their standing days in the app. */
  hasAccount: boolean;
  /** When staff recorded that the person agreed to receive texts (UTC, ISO); null if not (Q2). */
  textConsentAt: string | null;
  isActive: boolean;
  activeSponsorships: number;
  /** The RowVersion an edit sends back (base64). */
  version: string;
};

/** The donor form (DonorInput). */
export type DonorInput = {
  fullName: string;
  phone: string;
  notes: string | null;
  language: DonorLanguage;
  textConsent: boolean;
  version?: string;
};
