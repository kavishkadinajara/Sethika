import type { APIRequestContext } from "@playwright/test";
import { lastTextId, latestCode, type Account } from "../../support/accounts";
import { sql } from "../../support/sql";
import { signedIn, stepUpApi } from "./api";
import { account } from "./world";

/*  The applicant's and the reviewer's calls for the F05 / F06 cases, the same ones the screens
    make (support/applications.ts does this for the e2e suite). Every application a case opens
    for a persona is closed again by the case - withdrawn, or rejected once it is past the point
    an applicant can withdraw - so A1 stays under the open-application limit (3) run after run. */

export const APPS = "/api/onboarding/applications";
export const REVIEW = "/api/admin/registrations";

export type Lookups = {
  institutionTypes: { code: string; name: string }[];
  districts: { code: string; name: string }[];
  documents: { institutionType: string; code: string; isMandatory: boolean; maxSizeMb: number }[];
  declaration: { contentSha256: string; version: string } | null;
  rejectReasons?: { code: string; name: string }[];
};

export const at = (requestNo: string) => `${APPS}/${encodeURIComponent(requestNo)}`;
export const reviewAt = (requestNo: string) => `${REVIEW}/${encodeURIComponent(requestNo)}`;

export async function lookups(api: APIRequestContext): Promise<Lookups> {
  return (await (await api.get(`${APPS}/lookups?language=en`)).json()) as Lookups;
}

let counter = 0;

/** A registration number no one else holds: QA/<label>/<time>/<n>. */
export function registrationNo(label = "F05"): string {
  counter++;
  return `QA/${label}/${Date.now()}/${counter}`;
}

/** Details that pass every rule (fictional), for <type>, named <name>. */
export function details(type: string, name: string, overrides: Record<string, unknown> = {}) {
  return {
    institutionType: type,
    proposedName: name,
    officialRegistrationNo: registrationNo(),
    registeringAuthority: "QA Registrar (fictional)",
    registrationDate: "2014-05-20",
    establishedYear: 1975,
    addressLine1: "7 Lake Road",
    city: "Kurunegala",
    district: "KRN",
    latitude: 7.4863,
    longitude: 80.3647,
    officePhone: "0372222333",
    applicantFullName: "Imran Farook",
    applicantNic: "198012345678",
    applicantDesignation: "Secretary",
    applicantPhone: "0770100051",
    ...overrides,
  };
}

export const bearer1 = { fullName: "QA Bearer One", nic: "197512345678", designation: "Chairman", phone: "0771112223", email: null, isSignatory: true };

export const pdfBytes = (label = "QA") => Buffer.from(`%PDF-1.4\n% ${label}\n%%EOF\n`);

/** A file for an upload: a small PDF unless <buffer> / <name> say otherwise. */
export function upload(code: string, file: { name?: string; mimeType?: string; buffer?: Buffer } = {}, fields: Record<string, string> = {}) {
  return {
    multipart: {
      file: { name: file.name ?? `${code.toLowerCase()}.pdf`, mimeType: file.mimeType ?? "application/pdf", buffer: file.buffer ?? pdfBytes(code) },
      referenceNo: `QA-${code}`,
      issuedDate: "2014-05-20",
      expiryDate: "2030-12-31",
      ...fields,
    },
  };
}

async function must(response: Awaited<ReturnType<APIRequestContext["post"]>>, what: string) {
  if (!response.ok()) throw new Error(`${what}: ${response.status()} ${await response.text()}`);
  return response;
}

/** A draft with details only. */
export async function draft(api: APIRequestContext, type: string, name: string, overrides: Record<string, unknown> = {}): Promise<string> {
  const created = await must(await api.post(`${APPS}/`, { data: details(type, name, overrides) }), `Create ${name}`);
  return ((await created.json()) as { requestNo: string }).requestNo;
}

/** Office bearers and every mandatory document of <type>. */
export async function complete(api: APIRequestContext, requestNo: string, type: string, skip: string[] = []) {
  const all = await lookups(api);
  await must(await api.put(`${at(requestNo)}/office-bearers`, { data: { officeBearers: [bearer1] } }), "Office bearers");
  for (const document of all.documents.filter((d) => d.institutionType === type && d.isMandatory && !skip.includes(d.code))) {
    await must(await api.put(`${at(requestNo)}/documents/${document.code}`, upload(document.code)), `Upload ${document.code}`);
  }
}

export async function submit(api: APIRequestContext, requestNo: string) {
  const { declaration } = await lookups(api);
  return api.post(`${at(requestNo)}/submit`, { data: { declarationSha256: declaration!.contentSha256 } });
}

/** A submitted application of <type>, made by <api>. */
export async function submitted(api: APIRequestContext, type: string, name: string, overrides: Record<string, unknown> = {}): Promise<string> {
  const requestNo = await draft(api, type, name, overrides);
  await complete(api, requestNo, type);
  await must(await submit(api, requestNo), `Submit ${name}`);
  return requestNo;
}

/** P2 starts the review, verifies every document and passes the whole checklist. */
export async function reviewed(reviewer: APIRequestContext, requestNo: string) {
  await must(await reviewer.post(`${reviewAt(requestNo)}/start-review`), "Start review");
  const view = await reviewView(reviewer, requestNo);
  for (const document of view.application.documents) {
    await must(await reviewer.put(`${reviewAt(requestNo)}/documents/${document.documentType}/verification`, { data: { verified: true, remarks: null } }), "Verify");
  }
  await must(await reviewer.put(`${reviewAt(requestNo)}/checklist`, { data: { items: view.checklist.map((c) => ({ code: c.code, passed: true, remarks: null })) } }), "Checklist");
}

export type ReviewView = {
  application: { status: string; documents: { documentType: string; verificationStatus: string }[]; questions: { number: number; question: string; response: string | null }[] };
  checklist: { code: string; isMandatory?: boolean }[];
};

export async function reviewView(reviewer: APIRequestContext, requestNo: string): Promise<ReviewView> {
  return (await (await must(await reviewer.get(reviewAt(requestNo)), "Review view")).json()) as ReviewView;
}

/** The application's status, from the database. */
export function statusOf(requestNo: string): string {
  return sql("SELECT Status FROM org.RegistrationRequests WHERE RequestNo = '$(no)';", { no: requestNo })[0];
}

/**
 * Closes an application a case opened: withdrawn while the applicant still may, otherwise P2
 * makes sure it is under review and P1 rejects it ("OTHER", with step-up). Approved ones stay.
 */
export async function close(applicant: Account, requestNo: string) {
  const status = statusOf(requestNo);
  if (["Withdrawn", "Rejected", "Approved"].includes(status)) return;
  if (["Draft", "Submitted", "InfoRequested"].includes(status)) {
    const api = await signedIn(applicant);
    try {
      await must(await api.post(`${at(requestNo)}/withdraw`), `Withdraw ${requestNo}`);
    } finally {
      await api.dispose();
    }
    return;
  }
  const admin = await signedIn(account("P1"));
  try {
    await stepUpApi(admin, account("P1"));
    await must(await admin.post(`${reviewAt(requestNo)}/reject`, { data: { reason: "OTHER", note: "QA: closed by the case that opened it." } }), `Reject ${requestNo}`);
  } finally {
    await admin.dispose();
  }
}

/** The texts sent to <phone> after text <after> whose dedup key starts with <template>. */
export function textsSince(phone: string, after: number, template: string): string[] {
  return sql(
    "SELECT CONCAT(DedupKey, '|', Body) FROM notify.Notifications WHERE Channel = 'SMS' AND Id > $(after) AND RIGHT(Recipient, 9) = '$(last9)' AND DedupKey LIKE '$(template)%' ORDER BY Id;",
    // The sqlcmd guard takes no ":", so each one goes in as LIKE's one-character "_".
    { after: String(after), last9: phone.replace(/\D/g, "").slice(-9), template: template.replaceAll(":", "_") },
  );
}

/**
 * Waits for the outbox to text <template> to <phone> after <after>. Onboarding's events do not
 * wake the outbox processor (OTPs and bookings do), so they go with its once-a-minute run: ≤ 75 s.
 */
export async function textArrives(phone: string, after: number, template: string): Promise<string[]> {
  for (let i = 0; i < 150; i++) {
    if (i > 0) await new Promise((r) => setTimeout(r, 500));
    const rows = textsSince(phone, after, template);
    if (rows.length) return rows;
  }
  return [];
}

export { lastTextId, latestCode };
