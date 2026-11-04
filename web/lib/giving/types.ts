/*  Giving (plan 09, D.1), as GivingEndpoints.cs sends it. Kinds are the stored values. */

export type DonationKind = "Money" | "InKind";

/** The methods staff may choose at the counter (DonationHandler.CounterMethods). */
export type CounterMethod = "CASH" | "BANK_TRANSFER" | "CHEQUE" | "CARD";

export type LookupItem = { code: string; name: string; nameSi: string | null; nameTa: string | null };

/** A gift in the institution's month list: voided ones too, marked (M6). */
export type DonationListItem = {
  publicId: string;
  receiptNo: string;
  kind: DonationKind;
  amount: number | null;
  method: string | null;
  items: string | null;
  quantity: number | null;
  donatedAt: string;
  donorName: string;
  fromDonorBook: boolean;
  recordedByName: string;
  recordedByMe: boolean;
  isVoided: boolean;
  voidedAt: string | null;
  version: string;
};

export type ReceiptInstitution = {
  name: string;
  nameSi: string | null;
  nameTa: string | null;
  slug: string;
  addressLine1: string;
  addressLine2: string | null;
  city: string;
  phone: string;
};

/** One printable receipt (M4). */
export type Receipt = {
  publicId: string;
  receiptNo: string;
  kind: DonationKind;
  amount: number | null;
  currencyCode: string;
  method: string | null;
  items: string | null;
  quantity: number | null;
  estimatedValue: number | null;
  note: string | null;
  donatedAt: string;
  recordedAt: string;
  donorName: string;
  recordedByName: string;
  institution: ReceiptInstitution;
  isVoided: boolean;
  voidReason: string | null;
  voidedAt: string | null;
  voidedByName: string | null;
};

/** A gift in the donor's My giving. */
export type MyDonation = {
  publicId: string;
  receiptNo: string;
  institutionName: string;
  institutionNameSi: string | null;
  institutionNameTa: string | null;
  institutionSlug: string;
  kind: DonationKind;
  amount: number | null;
  items: string | null;
  quantity: number | null;
  donatedAt: string;
  isVoided: boolean;
};

/** The record sheet's body (DonationInput). One of `donor` (a donor book row) or `donorName` (a walk-in). */
export type DonationInput = {
  kind: DonationKind;
  amount: number | null;
  method: CounterMethod | null;
  items: string | null;
  quantity: number | null;
  estimatedValue: number | null;
  date: string;
  donor: string | null;
  donorName: string | null;
  donorPhone: string | null;
  note: string | null;
};
