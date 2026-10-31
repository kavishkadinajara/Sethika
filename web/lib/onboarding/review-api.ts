import { empty, json, jsonBody, send, type OnboardingFailure, type OnboardingResult } from "./api";
import { markShellStale } from "@/lib/shell/summary";
import type { ChecklistAnswer, Lookups, ReviewQueuePage, ReviewView } from "./types";

/*  Typed calls to /api/admin/registrations (RegistrationReviewEndpoints.cs), through the
    session's apiFetch (api.ts's send). Approve and reject need a step-up: the API answers 403 stepup_required,
    apiFetch hands that to StepUpHost, which asks for the texted code and retries the call - so
    a screen calls them like any other and gets the final answer.                           */

const BASE = "/api/admin/registrations";
const at = (requestNo: string) => `${BASE}/${encodeURIComponent(requestNo)}`;

export async function getReviewLookups(language: string): Promise<OnboardingResult<Lookups>> {
  return json(await send(`${BASE}/lookups?language=${encodeURIComponent(language)}`, { method: "GET" }));
}

/** A page of the queue. No status: the work still to do (Submitted, UnderReview, InfoRequested). */
export async function listForReview(status: string | null, page: number): Promise<OnboardingResult<ReviewQueuePage>> {
  const query = new URLSearchParams({ page: String(page), pageSize: "20" });
  if (status) query.set("status", status);
  return json(await send(`${BASE}?${query}`, { method: "GET" }));
}

export async function getForReview(requestNo: string): Promise<OnboardingResult<ReviewView>> {
  return json(await send(at(requestNo), { method: "GET" }));
}

export async function downloadForReview(requestNo: string, documentType: string): Promise<OnboardingResult<Blob>> {
  const result = await send(`${at(requestNo)}/documents/${encodeURIComponent(documentType)}/file`, { method: "GET" });
  return result.ok ? { ok: true, value: await result.value.blob() } : result;
}

export async function startReview(requestNo: string): Promise<OnboardingResult> {
  return empty(await send(`${at(requestNo)}/start-review`, { method: "POST" }));
}

/** Accepts a document, or refuses it with remarks the applicant will read. */
export async function verifyDocument(requestNo: string, documentType: string, verified: boolean, remarks: string | null): Promise<OnboardingResult> {
  return empty(await send(`${at(requestNo)}/documents/${encodeURIComponent(documentType)}/verification`, jsonBody("PUT", { verified, remarks })));
}

/** Records the answered items; all of them are saved, or none. */
export async function recordChecklist(requestNo: string, items: ChecklistAnswer[]): Promise<OnboardingResult> {
  return empty(await send(`${at(requestNo)}/checklist`, jsonBody("PUT", { items })));
}

export async function askQuestions(requestNo: string, questions: string[]): Promise<OnboardingResult> {
  return empty(await send(`${at(requestNo)}/questions`, jsonBody("POST", { questions })));
}

export async function approveApplication(requestNo: string): Promise<OnboardingResult<{ institutionCode: string; slug: string }>> {
  return decided(await json<{ institutionCode: string; slug: string }>(await send(`${at(requestNo)}/approve`, { method: "POST" })));
}

export async function rejectApplication(requestNo: string, reason: string, note: string | null): Promise<OnboardingResult> {
  return decided(await empty(await send(`${at(requestNo)}/reject`, jsonBody("POST", { reason, note }))));
}

/** A decided application leaves the queue: the sidebar's count need not wait for its next poll. */
function decided<T>(result: OnboardingResult<T>): OnboardingResult<T> {
  if (result.ok) markShellStale();
  return result;
}

export type { OnboardingFailure };
