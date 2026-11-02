import type { PublicServiceType } from "@/lib/services/types";

/*  The booking API's shapes (DonorBookingEndpoints.cs, InstitutionBookingEndpoints.cs), as JSON
    sends them. Dates are "yyyy-MM-dd" and times "HH:mm", both Colombo wall time; ids are the
    public ones (a slot's publicId, a booking's bookingNo), never the database's.               */

export type BookingStatus = "Requested" | "Approved" | "Rejected" | "Cancelled" | "Completed" | "NoShow";
export type BookingMode = "Exclusive" | "Shared";
export type FulfilmentMode = "DonorBrings" | "InstitutionPrepares";

/** Every name a donor sees comes in three languages; the si and ta ones may be missing. */
export type Named = { name: string; nameSi: string | null; nameTa: string | null };

export type PublicInstitutionSummary = Named & {
  slug: string;
  type: string;
  typeName: string;
  typeNameSi: string | null;
  typeNameTa: string | null;
  district: string;
  districtSi: string | null;
  districtTa: string | null;
  city: string;
};

export type PublicMealSlot = Named & {
  publicId: string;
  startTime: string;
  endTime: string;
  /** One bit per day: Sunday 1, Monday 2 ... Saturday 64. */
  daysOfWeekMask: number;
  bookingMode: BookingMode;
  maxSponsors: number;
  minHeadcount: number;
  maxHeadcount: number;
  estimatedCostPerHead: number | null;
  minAdvanceDays: number;
  maxAdvanceDays: number;
  requiresApproval: boolean;
  allowedFulfilment: FulfilmentMode[];
};

export type PublicInstitution = PublicInstitutionSummary & {
  addressLine1: string;
  addressLine2: string | null;
  description: string | null;
  dietaryNotes: string | null;
  latitude: number;
  longitude: number;
  slots: PublicMealSlot[];
  /** Religious services it takes requests for; empty while its SERVICES module is off (V25, V26). */
  services: PublicServiceType[];
};

/** A meal open on the searched day (SearchEndpoints.cs, plan 06), and what is left of it. */
export type SearchSlot = Named & {
  mealSlot: string;
  startTime: string;
  endTime: string;
  /** Sponsor places left. */
  sponsors: number;
  /** The most people one booking may bring now. */
  headcount: number;
};

/** A place with at least one meal open on the searched day. */
export type SearchPlace = PublicInstitutionSummary & {
  latitude: number;
  longitude: number;
  /** From the searched point; null when no point was given. */
  distanceKm: number | null;
  slots: SearchSlot[];
};

export type SearchAnswer = {
  date: string;
  results: SearchPlace[];
  /** More matched than the 100 shown. */
  more: boolean;
};

/** How many places have a meal open on each day of a month (G6). */
export type SearchMonth = { month: string; days: { date: string; institutions: number }[] };

/** What the search page's filters offer, in three languages. */
export type SearchLookups = {
  types: (Named & { code: string; category: string })[];
  districts: (Named & { code: string })[];
};

/** Why a day cannot be booked (BookingErrors in the domain). */
export type DayRefusal =
  | "slot_inactive"
  | "institution_not_active"
  | "not_on_this_day"
  | "out_of_season"
  | "date_blocked"
  | "too_soon"
  | "too_far"
  | "slot_full"
  /** Full only through a regular donor's day: it can be asked for (plan 08, K6, K25). */
  | "held";

export type DayAvailability = {
  date: string;
  bookable: boolean;
  reason: DayRefusal | null;
  /** Sponsor places left. */
  sponsors: number;
  /** The most people one booking may bring now. */
  headcount: number;
};

export type Availability = {
  /** "yyyy-MM". */
  month: string;
  slots: { mealSlot: string; days: DayAvailability[] }[];
};

export type BookingRequest = {
  mealSlot: string;
  date: string;
  headcount: number;
  fulfilment: FulfilmentMode;
  inMemoryOf: string | null;
  notes: string | null;
  /** Book the same meal on the same day every year from next year on (plan 07, H5). */
  everyYear?: boolean;
  /** A memorial plan's publicId and the observance's rule code, for a booking made from the planner (H6). */
  memorialPlan?: string | null;
  memorialRule?: string | null;
};

export type DonorBooking = {
  bookingNo: string;
  status: BookingStatus;
  date: string;
  startTime: string;
  endTime: string;
  /** The institution's slug, for a link back to its page. */
  institution: string;
  institutionName: string;
  institutionNameSi: string | null;
  institutionNameTa: string | null;
  mealSlot: string;
  slotName: string;
  slotNameSi: string | null;
  slotNameTa: string | null;
  headcount: number;
  fulfilmentMode: FulfilmentMode;
  inMemoryOf: string | null;
  notes: string | null;
  createdAt: string;
  rejectReason: string | null;
  cancelReason: string | null;
  canCancel: boolean;
  /** The active yearly booking it belongs to, or null (H5). */
  yearly: string | null;
  /** One of the donor's regular days, set up by the institution (plan 08, K3). */
  regular: boolean;
};

/** One of the donor's active yearly bookings (GET /api/bookings/recurring). */
export type YearlyBooking = {
  publicId: string;
  institution: string;
  institutionName: string;
  institutionNameSi: string | null;
  institutionNameTa: string | null;
  mealSlot: string;
  slotName: string;
  slotNameSi: string | null;
  slotNameTa: string | null;
  startTime: string;
  endTime: string;
  month: number;
  day: number;
  /** The day it books next, or null after its last year. */
  nextDate: string | null;
  lastYear: number | null;
  headcount: number;
  fulfilmentMode: FulfilmentMode;
  inMemoryOf: string | null;
};

// ---- the Memorial Dana Planner (MemorialEndpoints.cs, plan 07 H6) ---------------------------------

export type MemorialChoice = Named & { code: string };

export type MemorialRule = Named & { code: string; offsetMonths: number; offsetDays: number; repeatsAnnually: boolean };

export type MemorialReligion = Named & { code: string; rules: MemorialRule[] };

export type ObservanceState = "booked" | "toBook" | "past";

export type MemorialObservance = Named & {
  /** The rule's code, sent back with a booking for this observance. */
  rule: string;
  repeatsAnnually: boolean;
  /** 1 the first time; for a yearly observance, which year. */
  number: number;
  date: string;
  state: ObservanceState;
  bookingNo: string | null;
  institution: string | null;
  institutionName: string | null;
  institutionNameSi: string | null;
  institutionNameTa: string | null;
  /** Booked every year by a yearly booking of the plan's. */
  everyYear: boolean;
};

export type MemorialPlan = {
  publicId: string;
  deceasedName: string;
  relationship: string | null;
  dateOfPassing: string;
  religion: string;
  notes: string | null;
  observances: MemorialObservance[];
};

export type MemorialPlanInput = {
  deceasedName: string;
  relationship: string | null;
  dateOfPassing: string;
  religion: string;
  notes: string | null;
};

// ---- co-sponsors (SharingEndpoints.cs, plan 07 H16-H19) -------------------------------------------

export type InvitationStatus = "Invited" | "Joined" | "Declined";

/** One invitation on the donor's booking. */
export type Invitation = {
  publicId: string;
  name: string;
  note: string | null;
  status: InvitationStatus;
  invitedAt: string;
  joinedAt: string | null;
  declinedAt: string | null;
};

export type ShareBookingState = "Open" | "Cancelled" | "Rejected" | "Past";

/** What a co-sponsor's link shows anyone (GET /api/share/{token}). */
export type ShareSummary = {
  institution: string;
  institutionName: string;
  institutionNameSi: string | null;
  institutionNameTa: string | null;
  mealName: string;
  mealNameSi: string | null;
  mealNameTa: string | null;
  date: string;
  startTime: string;
  donorName: string;
  inMemoryOf: string | null;
  invitation: InvitationStatus;
  booking: ShareBookingState;
};

/** A booking the signed-in person joined (GET /api/bookings/shared). */
export type SharedBooking = {
  bookingNo: string;
  status: BookingStatus;
  institutionSlug: string;
  institutionName: string;
  institutionNameSi: string | null;
  institutionNameTa: string | null;
  mealName: string;
  mealNameSi: string | null;
  mealNameTa: string | null;
  date: string;
  startTime: string;
  headcount: number;
  donorName: string;
  inMemoryOf: string | null;
  joinedAt: string;
};

/** Someone who joined a booking, as its institution sees them. */
export type SharedWith = { name: string; joinedAt: string };

// ---- the institution's side ---------------------------------------------------------------------

export type InstitutionMealSlot = Omit<PublicMealSlot, "allowedFulfilment"> & {
  allowedFulfilment: FulfilmentMode[];
  sortOrder: number;
  isActive: boolean;
  /** The RowVersion, base64: an edit sends it back so it cannot overwrite a newer one. */
  version: string;
};

export type MealSlotInput = {
  name: string;
  nameSi: string | null;
  nameTa: string | null;
  startTime: string;
  endTime: string;
  daysOfWeekMask: number;
  bookingMode: BookingMode;
  maxSponsors: number | null;
  minHeadcount: number | null;
  maxHeadcount: number | null;
  estimatedCostPerHead: number | null;
  minAdvanceDays: number | null;
  maxAdvanceDays: number | null;
  requiresApproval: boolean;
  allowedFulfilment: FulfilmentMode[];
  sortOrder: number | null;
  version?: string;
};

export type BlockedDate = {
  /** Tenant-internal; only the institution's own screens use it. */
  id: number;
  dateFrom: string;
  dateTo: string;
  /** The one slot blocked, or null for every slot. */
  mealSlot: string | null;
  mealSlotName: string | null;
  reason: string;
};

export type Listing = { slug: string; listed: boolean };

export type CalendarBooking = {
  bookingNo: string;
  status: BookingStatus;
  donorName: string;
  donorPhone: string;
  headcount: number;
  fulfilmentMode: FulfilmentMode;
  inMemoryOf: string | null;
  notes: string | null;
  createdAt: string;
  rejectReason: string | null;
  cancelReason: string | null;
  /** A regular donor's day (plan 08, K3): its sponsorship's publicId; null for any other booking. */
  standing: string | null;
};

/** A slot on a day that has been booked: its places and the bookings that took them. */
export type SlotDay = {
  date: string;
  mealSlot: string;
  slotName: string;
  slotNameSi: string | null;
  slotNameTa: string | null;
  startTime: string;
  endTime: string;
  maxSponsors: number;
  bookedSponsors: number;
  maxHeadcount: number;
  bookedHeadcount: number;
  status: "Open" | "Full";
  bookings: CalendarBooking[];
};

/** How a meal sits on today's table: places left, every place taken, or blocked for the day. */
export type TableSlotState = "Open" | "Full" | "Closed";

/** One meal of today's table (GET /api/institution/today). */
export type TableSlot = {
  mealSlot: string;
  name: string;
  nameSi: string | null;
  nameTa: string | null;
  startTime: string;
  endTime: string;
  maxSponsors: number;
  bookedSponsors: number;
  /** Bookings for it still waiting for an answer. */
  requested: number;
  state: TableSlotState;
};

/** Today in Colombo (yyyy-MM-dd) and its meals, by start time. */
export type TodaysTable = { date: string; slots: TableSlot[] };

/** One change of a booking on the dashboard's ledger (GET /api/institution/ledger); `status` is the one it moved to. */
export type LedgerEntry = {
  bookingNo: string;
  status: BookingStatus;
  /** When, UTC. */
  at: string;
  /** The day the meal is for. */
  date: string;
  slotName: string;
  slotNameSi: string | null;
  slotNameTa: string | null;
  donorName: string;
};

/** Where a booking sits on the calendar (GET /api/institution/bookings/{bookingNo}). */
export type BookingPlace = { bookingNo: string; date: string; mealSlot: string; status: BookingStatus };

export type Decision = "approve" | "reject" | "cancel" | "complete" | "no-show";
