import { describe, expect, it } from "vitest";
import { inLocale } from "./names";
import { emptySlotForm, formAfterStale, malformedSlotFields, nameError, slotFormFrom, slotInputFrom } from "./slot-form";
import type { InstitutionMealSlot } from "./types";

const saved: InstitutionMealSlot = {
  publicId: "0193f0c2-0000-7000-8000-000000000001",
  name: "Lunch",
  nameSi: "දිවා දානය",
  nameTa: null,
  startTime: "11:30",
  endTime: "13:00",
  daysOfWeekMask: 62,
  bookingMode: "Shared",
  maxSponsors: 4,
  minHeadcount: 10,
  maxHeadcount: 60,
  estimatedCostPerHead: 450.5,
  minAdvanceDays: 2,
  maxAdvanceDays: 90,
  requiresApproval: false,
  allowedFulfilment: ["DonorBrings"],
  sortOrder: 0,
  isActive: true,
  version: "AAAAAAAAB9E=",
};

describe("slot form", () => {
  it("round-trips a saved slot, carrying its version", () => {
    expect(slotInputFrom(slotFormFrom(saved))).toEqual({
      name: "Lunch",
      nameSi: "දිවා දානය",
      nameTa: null,
      startTime: "11:30",
      endTime: "13:00",
      daysOfWeekMask: 62,
      bookingMode: "Shared",
      maxSponsors: 4,
      minHeadcount: 10,
      maxHeadcount: 60,
      estimatedCostPerHead: 450.5,
      minAdvanceDays: 2,
      maxAdvanceDays: 90,
      requiresApproval: false,
      allowedFulfilment: ["DonorBrings"],
      sortOrder: null,
      version: "AAAAAAAAB9E=",
    });
  });

  it("sends no sponsor count for an exclusive slot, and no version for a new one", () => {
    const input = slotInputFrom({ ...emptySlotForm(), name: " Dinner ", maxHeadcount: "40" });

    expect(input.maxSponsors).toBeNull();
    expect(input.name).toBe("Dinner");
    expect("version" in input).toBe(false);
  });

  it("names numbers that are not numbers, and leaves blanks to the API's defaults", () => {
    const form = { ...emptySlotForm(), bookingMode: "Shared" as const, maxSponsors: "four", minHeadcount: "", maxHeadcount: "1O", estimatedCostPerHead: "Rs 450" };

    expect(malformedSlotFields(form)).toEqual(["maxSponsors", "maxHeadcount", "estimatedCostPerHead"]);
    expect(malformedSlotFields({ ...form, bookingMode: "Exclusive" })).toEqual(["maxHeadcount", "estimatedCostPerHead"]);
    expect(slotInputFrom({ ...form, estimatedCostPerHead: "1,250.50" }).estimatedCostPerHead).toBe(1250.5);
  });
});

describe("inLocale", () => {
  it("falls back to English when the language has no name", () => {
    expect(inLocale("si", "Lunch", "දිවා දානය", null)).toBe("දිවා දානය");
    expect(inLocale("ta", "Lunch", "දිවා දානය", null)).toBe("Lunch");
    expect(inLocale("ta", "Lunch", null, "  ")).toBe("Lunch");
    expect(inLocale("en", "Lunch", "දිවා දානය", "மதிய உணவு")).toBe("Lunch");
  });
});

describe("after a refused save (QA DEF-006, DEF-007)", () => {
  it("names a taken name as taken, not as a missing one, though the field is marked", () => {
    expect(nameError(new Set(["name"]), "slot_name_taken")).toBe("taken");
    expect(nameError(new Set(), "slot_name_taken")).toBe("taken");
    expect(nameError(new Set(["name"]), null)).toBe("invalid");
    expect(nameError(new Set(["name"]), "invalid_details")).toBe("invalid");
    expect(nameError(new Set(["maxHeadcount"]), "stale")).toBeNull();
    expect(nameError(new Set(), undefined)).toBeNull();
  });

  it("after a stale save, holds the meal as someone else saved it, with its new version", () => {
    const changed = { ...saved, maxHeadcount: 25, version: "AAAAAAAAB+A=" };
    const other = { ...saved, publicId: "0193f0c2-0000-7000-8000-000000000002", maxHeadcount: 99, version: "AAAAAAAAB+B=" };
    const form = formAfterStale([other, changed], saved.publicId);
    expect(form).toEqual(slotFormFrom(changed));
    expect(form?.maxHeadcount).toBe("25");
    expect(form?.version).toBe("AAAAAAAAB+A=");
    // A switched-off meal can still be edited: it is reloaded too.
    expect(formAfterStale([{ ...changed, isActive: false }], saved.publicId)?.version).toBe("AAAAAAAAB+A=");
  });

  it("after a stale save, has no form when the meal is gone", () => {
    expect(formAfterStale([], saved.publicId)).toBeNull();
  });
});
