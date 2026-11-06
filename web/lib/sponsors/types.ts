import type { BookingStatus, FulfilmentMode } from "@/lib/bookings/types";

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

/*  Standing sponsorships (plan 08, K2-K9), as /api/institution/sponsorships sends them. */

export type StandingPattern = "Once" | "Daily" | "Weekly" | "Monthly" | "Yearly";

/** A sponsorship (SponsorshipView). Weekdays are 0 Sunday ... 6 Saturday; month days 1 ... 31. */
export type Sponsorship = {
  publicId: string;
  donor: string;
  donorName: string;
  mealSlot: string;
  mealSlotName: string;
  pattern: StandingPattern;
  monthInterval: number;
  weekDays: number[];
  monthDays: number[];
  yearMonth: number | null;
  yearDay: number | null;
  startDate: string;
  endDate: string | null;
  headcount: number;
  fulfilment: FulfilmentMode;
  inMemoryOf: string | null;
  notes: string | null;
  status: "Active" | "Ended";
  endedAt: string | null;
  maxAdvanceDays: number;
  version: string;
};

/**
 * Where one of a sponsorship's next days stands: booked (see `bookingStatus`), cancelled on the
 * calendar, let go or moved (an exception's kind), open (in the window, not booked: the night's run
 * tries again) or upcoming (beyond the window).
 */
export type SponsorshipDayState = "booked" | "cancelled" | "open" | "upcoming" | "Skipped" | "Released" | "Blocked" | "MovedTo" | "MovedFrom";

export type SponsorshipDay = {
  date: string;
  state: SponsorshipDayState;
  bookingNo: string | null;
  bookingStatus: BookingStatus | null;
  reason: string | null;
};

export type SponsorshipDetails = { sponsorship: Sponsorship; days: SponsorshipDay[] };

/** The sponsorship form (SponsorshipInput): only the chosen pattern's own fields are sent. */
export type SponsorshipInput = {
  donor: string;
  mealSlot: string;
  pattern: StandingPattern;
  monthInterval?: number;
  weekDays?: number[];
  monthDays?: number[];
  yearMonth?: number;
  yearDay?: number;
  startDate: string;
  endDate?: string | null;
  headcount: number;
  fulfilment: FulfilmentMode;
  inMemoryOf: string | null;
  notes: string | null;
};

/** What became of a day booked while staff waited (K9). */
export type CreatedDay = {
  date: string;
  outcome: "booked" | "notBooked" | "alreadyDone" | "alreadyHeld" | "ended" | "busy";
  error: string | null;
  bookingNo: string | null;
};

export type CreatedSponsorship = { publicId: string; version: string; days: CreatedDay[]; later: number };

/** A day of the form's preview (PreviewDay): `reason` is a booking refusal code for a clash. */
export type PreviewDay = { date: string; state: "free" | "clash" | "later"; reason: string | null };

export type SwapDay = { sponsorship: string; date: string };

/*  Asking for a held day (plan 08, K6). */

export type DayRequestStatus = "Open" | "Given" | "Declined" | "Withdrawn";

/** A request in the institution's tray (InstitutionDayRequestView). */
export type InstitutionDayRequest = {
  requestNo: string;
  status: DayRequestStatus;
  date: string;
  mealSlot: string;
  mealSlotName: string;
  donorName: string;
  donorPhone: string | null;
  headcount: number;
  fulfilment: FulfilmentMode;
  note: string | null;
  createdAt: string;
  bookingNo: string | null;
  declineReason: string | null;
  /** The regular donors holding the day: more than one means staff choose whose to give. */
  holders: { sponsorship: string; donorName: string }[];
};

/** The donor's own request (MyDayRequestView). */
export type MyDayRequest = {
  requestNo: string;
  status: DayRequestStatus;
  date: string;
  institution: string;
  institutionName: string;
  institutionNameSi: string | null;
  institutionNameTa: string | null;
  mealSlot: string;
  mealSlotName: string;
  headcount: number;
  fulfilment: FulfilmentMode;
  createdAt: string;
  bookingNo: string | null;
  declineReason: string | null;
};

export type DayRequestInput = { mealSlot: string; date: string; headcount: number; fulfilment: FulfilmentMode; note: string | null };
