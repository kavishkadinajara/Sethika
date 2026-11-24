import type { BookingMode, FulfilmentMode, InstitutionMealSlot, MealSlotInput } from "./types";

/*  The meal slot form keeps every field as the inputs hold it (text), and turns it into the API's
    body only on save, so a half-typed number is never "corrected" under the person's fingers.
    The API checks everything again and names the fields at fault (SlotManagementHandler).       */

export type SlotForm = {
  name: string;
  nameSi: string;
  nameTa: string;
  startTime: string;
  endTime: string;
  daysOfWeekMask: number;
  bookingMode: BookingMode;
  maxSponsors: string;
  minHeadcount: string;
  maxHeadcount: string;
  estimatedCostPerHead: string;
  minAdvanceDays: string;
  maxAdvanceDays: string;
  requiresApproval: boolean;
  allowedFulfilment: FulfilmentMode[];
  /** The RowVersion an edit started from; none for a new slot. */
  version: string | null;
};

/** What a new slot starts as: lunch every day, one sponsor, approved by the institution. */
export function emptySlotForm(): SlotForm {
  return {
    name: "",
    nameSi: "",
    nameTa: "",
    startTime: "11:30",
    endTime: "13:00",
    daysOfWeekMask: 127,
    bookingMode: "Exclusive",
    maxSponsors: "4",
    minHeadcount: "1",
    maxHeadcount: "",
    estimatedCostPerHead: "",
    minAdvanceDays: "1",
    maxAdvanceDays: "365",
    requiresApproval: true,
    allowedFulfilment: ["DonorBrings", "InstitutionPrepares"],
    version: null,
  };
}

export function slotFormFrom(slot: InstitutionMealSlot): SlotForm {
  return {
    name: slot.name,
    nameSi: slot.nameSi ?? "",
    nameTa: slot.nameTa ?? "",
    startTime: slot.startTime,
    endTime: slot.endTime,
    daysOfWeekMask: slot.daysOfWeekMask,
    bookingMode: slot.bookingMode,
    maxSponsors: slot.bookingMode === "Shared" ? String(slot.maxSponsors) : "4",
    minHeadcount: String(slot.minHeadcount),
    maxHeadcount: String(slot.maxHeadcount),
    estimatedCostPerHead: slot.estimatedCostPerHead === null ? "" : String(slot.estimatedCostPerHead),
    minAdvanceDays: String(slot.minAdvanceDays),
    maxAdvanceDays: String(slot.maxAdvanceDays),
    requiresApproval: slot.requiresApproval,
    allowedFulfilment: slot.allowedFulfilment,
    version: slot.version,
  };
}

/**
 * Which message the name field shows: the API's "you already have a meal with this name" before
 * the form's own "enter a name" rule, which a taken name passes (QA DEF-007).
 */
export function nameError(invalid: ReadonlySet<string>, failureCode: string | null | undefined): "taken" | "invalid" | null {
  if (failureCode === "slot_name_taken") return "taken";
  return invalid.has("name") ? "invalid" : null;
}

/**
 * The form after a save refused as stale (plan 03, E17; QA DEF-006): the meal as it is now, with
 * its new version, so "it has been refreshed; check and try again" is true and the next save can
 * go through. Null when the meal is no longer there to edit: the sheet closes and the list reloads.
 */
export function formAfterStale(current: readonly InstitutionMealSlot[], publicId: string): SlotForm | null {
  const fresh = current.find((s) => s.publicId === publicId);
  return fresh ? slotFormFrom(fresh) : null;
}

/** The API's body. Blank is null (the API's default applies); text that is not a number is NaN-free null too, and the API names the field. */
export function slotInputFrom(form: SlotForm): MealSlotInput {
  return {
    name: form.name.trim(),
    nameSi: blankToNull(form.nameSi),
    nameTa: blankToNull(form.nameTa),
    startTime: form.startTime,
    endTime: form.endTime,
    daysOfWeekMask: form.daysOfWeekMask,
    bookingMode: form.bookingMode,
    // An exclusive slot has one sponsor; the API refuses any other number for it.
    maxSponsors: form.bookingMode === "Shared" ? whole(form.maxSponsors) : null,
    minHeadcount: whole(form.minHeadcount),
    maxHeadcount: whole(form.maxHeadcount),
    estimatedCostPerHead: money(form.estimatedCostPerHead),
    minAdvanceDays: whole(form.minAdvanceDays),
    maxAdvanceDays: whole(form.maxAdvanceDays),
    requiresApproval: form.requiresApproval,
    allowedFulfilment: form.allowedFulfilment,
    sortOrder: null,
    ...(form.version ? { version: form.version } : {}),
  };
}

/**
 * Fields whose text is not a number at all, named as the API names them. Checked before sending:
 * the body would carry them as null, which the API reads as "use the default", not as a mistake.
 */
export function malformedSlotFields(form: SlotForm): string[] {
  const numbers: [keyof SlotForm & string, (value: string) => number | null][] = [
    ["minHeadcount", whole],
    ["maxHeadcount", whole],
    ["estimatedCostPerHead", money],
    ["minAdvanceDays", whole],
    ["maxAdvanceDays", whole],
  ];
  if (form.bookingMode === "Shared") numbers.unshift(["maxSponsors", whole]);

  return numbers
    .filter(([field, parse]) => {
      const text = String(form[field]).trim();
      return text !== "" && parse(text) === null;
    })
    .map(([field]) => field);
}

function blankToNull(value: string): string | null {
  const trimmed = value.trim();
  return trimmed ? trimmed : null;
}

function whole(value: string): number | null {
  const trimmed = value.trim();
  if (!/^\d{1,6}$/.test(trimmed)) return null;
  return Number(trimmed);
}

function money(value: string): number | null {
  const trimmed = value.trim().replace(/,/g, "");
  if (!/^\d{1,8}(\.\d{1,2})?$/.test(trimmed)) return null;
  return Number(trimmed);
}
