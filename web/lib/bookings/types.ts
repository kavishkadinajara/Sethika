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
  | "slot_full";

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
};

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
