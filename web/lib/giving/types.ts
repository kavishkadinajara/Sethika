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
  /** An open need the gift goes toward (M15); items then need `quantity`. */
  need: string | null;
};

/*  Needs and pledges (plan 09, D.2), as NeedEndpoints.cs sends them. */

export type NeedStatus = "Draft" | "Open" | "Fulfilled" | "Closed" | "Cancelled";
export type NeedPriority = "Low" | "Normal" | "Urgent";
export type PledgeStatus = "Pledged" | "Delivered" | "Cancelled";

/** The two counters: `quantityPledged` is everything spoken for, so what arrived plus what open pledges promise (M29). */
export type NeedCounts = { quantityNeeded: number; quantityPledged: number; quantityReceived: number };

/** A need on the institution's own screen, any status. */
export type NeedListItem = NeedCounts & {
  publicId: string;
  title: string;
  description: string | null;
  category: string;
  unit: string;
  priority: NeedPriority;
  deadline: string | null;
  status: NeedStatus;
  openPledges: number;
  version: string;
};

/** A need on the public board (M12). */
export type PublicNeed = NeedCounts & {
  publicId: string;
  title: string;
  description: string | null;
  category: string;
  unit: string;
  priority: NeedPriority;
  deadline: string | null;
  institutionName: string;
  institutionNameSi: string | null;
  institutionNameTa: string | null;
  institutionSlug: string;
  city: string;
  district: string;
  districtSi: string | null;
  districtTa: string | null;
};

/** A pledge as the institution sees it. */
export type NeedPledge = {
  publicId: string;
  donorName: string;
  donorPhone: string | null;
  quantity: number;
  expectedDate: string | null;
  status: PledgeStatus;
  receivedQuantity: number | null;
  cancelReason: string | null;
  createdAt: string;
  receiptPublicId: string | null;
  receiptNo: string | null;
  version: string;
};

/** A pledge in the donor's My giving. */
export type MyPledge = {
  publicId: string;
  needTitle: string;
  unit: string;
  institutionName: string;
  institutionNameSi: string | null;
  institutionNameTa: string | null;
  institutionSlug: string;
  quantity: number;
  expectedDate: string | null;
  status: PledgeStatus;
  receivedQuantity: number | null;
  cancelReason: string | null;
  createdAt: string;
  receiptPublicId: string | null;
  receiptNo: string | null;
};

export type NeedLookups = { categories: LookupItem[]; units: LookupItem[] };

/** The add and edit sheets' body (NeedInput). */
export type NeedInput = {
  title: string;
  description: string | null;
  category: string;
  unit: string;
  quantity: number | null;
  priority: NeedPriority;
  deadline: string | null;
  version?: string;
};
