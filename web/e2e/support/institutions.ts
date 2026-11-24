import type { APIRequestContext } from "@playwright/test";
import { apiAs, createAccount, grantPlatformRole, lastTextId, latestCode, type Account } from "./accounts";
import { submittedApplication } from "./applications";

/*  An approved institution, made the way the platform makes one: an application submitted by its
    owner-to-be, reviewed and approved by an administrator - through the API, step-up and all. The
    approval is what clones the roles and enables the modules, so the institution and its Owner are
    exactly what a real one gets. The screens for this are review.spec.ts's subject.             */

const REVIEW = "/api/admin/registrations";

async function ok(response: Awaited<ReturnType<APIRequestContext["post"]>>, what: string) {
  if (!response.ok()) throw new Error(`${what}: ${response.status()} ${await response.text()}`);
  return response;
}

export type Institution = { owner: Account; slug: string; name: string };

export async function approvedInstitution(baseURL: string, name: string, language: "en" | "si" | "ta" = "en"): Promise<Institution> {
  const owner = await createAccount(baseURL, "E2E Owner", language);
  const applicantApi = await apiAs(baseURL, owner);
  const requestNo = await submittedApplication(applicantApi, name);
  await applicantApi.dispose();

  const admin = await createAccount(baseURL, "E2E Admin");
  grantPlatformRole(admin, "PLATFORM_ADMIN");
  const api = await apiAs(baseURL, admin);
  try {
    const at = `${REVIEW}/${encodeURIComponent(requestNo)}`;
    await ok(await api.post(`${at}/start-review`), "Start review");

    const view = (await (await ok(await api.get(at), "Review view")).json()) as {
      application: { documents: { documentType: string }[] };
      checklist: { code: string }[];
    };
    for (const document of view.application.documents) {
      await ok(await api.put(`${at}/documents/${document.documentType}/verification`, { data: { verified: true, remarks: null } }), "Verify document");
    }
    await ok(await api.put(`${at}/checklist`, { data: { items: view.checklist.map((c) => ({ code: c.code, passed: true, remarks: null })) } }), "Checklist");

    // Approving needs a step-up: a texted code, verified against this token.
    const before = lastTextId(admin.phone);
    await ok(await api.post("/api/auth/stepup/request"), "Step-up request");
    const { code } = await latestCode(admin.phone, before);
    await ok(await api.post("/api/auth/stepup/verify", { data: { code } }), "Step-up verify");

    const approved = (await (await ok(await api.post(`${at}/approve`), "Approve")).json()) as { slug: string };
    return { owner, slug: approved.slug, name };
  } finally {
    await api.dispose();
  }
}

export type SlotOptions = {
  name?: string;
  bookingMode?: "Exclusive" | "Shared";
  maxSponsors?: number | null;
  requiresApproval?: boolean;
};

/** A meal slot made by the institution's owner through the API; its public id. */
export async function slotOf(baseURL: string, institution: Institution, options: SlotOptions = {}): Promise<string> {
  const api = await apiAs(baseURL, institution.owner);
  try {
    const response = await ok(
      await api.post("/api/institution/meal-slots", {
        data: {
          name: options.name ?? "Lunch",
          nameSi: null,
          nameTa: null,
          startTime: "11:30",
          endTime: "13:00",
          daysOfWeekMask: 127,
          bookingMode: options.bookingMode ?? "Exclusive",
          maxSponsors: options.bookingMode === "Shared" ? (options.maxSponsors ?? 4) : null,
          minHeadcount: 5,
          maxHeadcount: 50,
          estimatedCostPerHead: 450,
          minAdvanceDays: 1,
          maxAdvanceDays: 120,
          requiresApproval: options.requiresApproval ?? true,
          allowedFulfilment: ["DonorBrings", "InstitutionPrepares"],
          sortOrder: null,
        },
      }),
      "Create slot",
    );
    return ((await response.json()) as { publicId: string }).publicId;
  } finally {
    await api.dispose();
  }
}

/** A booking by <donor> through the API; its number. */
export async function bookingBy(baseURL: string, donor: Account, mealSlot: string, date: string): Promise<string> {
  const api = await apiAs(baseURL, donor);
  try {
    const response = await ok(
      await api.post("/api/bookings", { data: { mealSlot, date, headcount: 10, fulfilment: "DonorBrings", inMemoryOf: null, notes: null } }),
      "Book",
    );
    return ((await response.json()) as { bookingNo: string }).bookingNo;
  } finally {
    await api.dispose();
  }
}

/** Puts <institution> in the public list, which takes a meal to offer; `unlist` takes it out again. */
export async function listed(baseURL: string, institution: Institution) {
  await slotOf(baseURL, institution);
  const api = await apiAs(baseURL, institution.owner);
  try {
    await ok(await api.put("/api/institution/listing", { data: { listed: true } }), "List");
  } finally {
    await api.dispose();
  }
}

/** Takes <institution> out of the public list again, so runs do not fill it with test places. */
export async function unlist(baseURL: string, institution: Institution) {
  const api = await apiAs(baseURL, institution.owner);
  try {
    await api.put("/api/institution/listing", { data: { listed: false } });
  } finally {
    await api.dispose();
  }
}

/** A date <days> after today in Colombo, yyyy-MM-dd. */
export function colomboDate(days: number): string {
  const today = new Intl.DateTimeFormat("en-CA", { timeZone: "Asia/Colombo" }).format(new Date());
  const [y, m, d] = today.split("-").map(Number);
  return new Date(Date.UTC(y, m - 1, d + days)).toISOString().slice(0, 10);
}
