/*  The donor booking API's shapes (DonorBookingEndpoints.cs, IDonorBookingQueries.cs), as JSON
    sends them; the same as web/lib/bookings/types.ts, copied rather than imported. Dates are
    "yyyy-MM-dd" and times "HH:mm", both Colombo wall time; ids are the public ones (a slot's
    publicId, a booking's bookingNo, an institution's slug), never the database's.             */

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

export type BookingCreated = { bookingNo: string; status: BookingStatus };

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
  /** Whether the donor may still cancel it themselves (plan 03, Q1). The API decides, not the app. */
  canCancel: boolean;
};
