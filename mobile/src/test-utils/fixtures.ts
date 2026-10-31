import type { Availability, DayAvailability, DonorBooking, PublicInstitution, PublicMealSlot } from "@/bookings/types";

/*  The API's answers as DonorBookingEndpoints.cs sends them, for the booking tests. The ids are
    public ones; the names are made up for tests and stand for no real institution.           */

export const LUNCH = "0193f0c2-0000-7000-8000-000000000001";
export const DINNER = "0193f0c2-0000-7000-8000-000000000002";

export function mealSlot(overrides: Partial<PublicMealSlot> = {}): PublicMealSlot {
  return {
    publicId: LUNCH,
    name: "Lunch",
    nameSi: "දිවා ආහාරය",
    nameTa: null,
    startTime: "11:30",
    endTime: "13:00",
    daysOfWeekMask: 127,
    bookingMode: "Shared",
    maxSponsors: 4,
    minHeadcount: 10,
    maxHeadcount: 60,
    estimatedCostPerHead: 450,
    minAdvanceDays: 2,
    maxAdvanceDays: 90,
    requiresApproval: false,
    allowedFulfilment: ["DonorBrings", "InstitutionPrepares"],
    ...overrides,
  };
}

export function institution(overrides: Partial<PublicInstitution> = {}): PublicInstitution {
  return {
    slug: "test-elders-home",
    name: "Test Elders' Home",
    nameSi: null,
    nameTa: null,
    type: "ELDERS_HOME",
    typeName: "Elders' home",
    typeNameSi: null,
    typeNameTa: null,
    district: "Kandy",
    districtSi: "මහනුවර",
    districtTa: "கண்டி",
    city: "Peradeniya",
    addressLine1: "12 Temple Road",
    addressLine2: null,
    description: null,
    dietaryNotes: null,
    latitude: 7.27,
    longitude: 80.6,
    slots: [mealSlot()],
    ...overrides,
  };
}

export function day(date: string, overrides: Partial<DayAvailability> = {}): DayAvailability {
  return { date, bookable: true, reason: null, sponsors: 4, headcount: 60, ...overrides };
}

/** A month where every day is open, except the ones given. */
export function month(value: string, slot: string, days: DayAvailability[] = []): Availability {
  const [year, m] = value.split("-").map(Number);
  const length = new Date(Date.UTC(year, m, 0)).getUTCDate();
  const all = Array.from({ length }, (_, i) => {
    const date = `${value}-${String(i + 1).padStart(2, "0")}`;
    return days.find((d) => d.date === date) ?? day(date);
  });
  return { month: value, slots: [{ mealSlot: slot, days: all }] };
}

export function booking(overrides: Partial<DonorBooking> = {}): DonorBooking {
  return {
    bookingNo: "BK-2026-000001",
    status: "Approved",
    date: "2026-11-20",
    startTime: "11:30",
    endTime: "13:00",
    institution: "test-elders-home",
    institutionName: "Test Elders' Home",
    institutionNameSi: null,
    institutionNameTa: null,
    mealSlot: LUNCH,
    slotName: "Lunch",
    slotNameSi: null,
    slotNameTa: null,
    headcount: 25,
    fulfilmentMode: "DonorBrings",
    inMemoryOf: null,
    notes: null,
    createdAt: "2026-10-20T08:00:00Z",
    rejectReason: null,
    cancelReason: null,
    canCancel: true,
    ...overrides,
  };
}
