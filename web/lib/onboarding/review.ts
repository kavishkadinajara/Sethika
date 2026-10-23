import type {
  ApplicationStatus,
  ChecklistAnswer,
  ReviewChecklistItem,
  ReviewRequiredDocument,
  ReviewView,
  UploadedDocument,
} from "./types";

/*  The reviewer's screen logic, kept free of React so it is unit-tested (review.test.ts). The API
    decides every rule again (RegistrationReviewHandler, the aggregate); these only decide what the
    screen offers, and say ahead of time what an approval would be refused for.              */

/** The API's limits (RegistrationReviewHandler), so the screen stops a person before the API does. */
export const MAX_QUESTIONS = 10;
export const MAX_QUESTION_LENGTH = 1000;
export const MAX_REMARKS_LENGTH = 500;
export const MAX_NOTE_LENGTH = 1000;

/** The queue's filters. Null is the work still to do: Submitted, UnderReview and InfoRequested. */
export const queueFilters = [null, "Submitted", "UnderReview", "InfoRequested", "Approved", "Rejected", "Withdrawn"] as const;
export type QueueFilter = (typeof queueFilters)[number];

/** A ?status= value the queue accepts, or null (the open work) for anything else. */
export function parseQueueFilter(value: string | null | undefined): QueueFilter {
  return (queueFilters as readonly (string | null)[]).includes(value ?? null) ? (value as QueueFilter) : null;
}

export function pageCount(total: number, pageSize: number): number {
  return Math.max(1, Math.ceil(total / pageSize));
}

/** What a reviewer may do at each status: start, then review (verify, check, ask, decide). */
export function reviewStage(status: ApplicationStatus): "start" | "review" | "waiting" | "closed" {
  if (status === "Submitted") return "start";
  if (status === "UnderReview") return "review";
  if (status === "InfoRequested") return "waiting";
  return "closed";
}

/** The live upload for a document type, if the applicant sent one. */
export function uploadFor(view: ReviewView, code: string): UploadedDocument | null {
  return view.application.documents.find((d) => d.documentType === code) ?? null;
}

export type ApprovalBlockers = {
  /** Mandatory documents not yet accepted (missing, waiting, or refused). */
  documents: ReviewRequiredDocument[];
  /** Mandatory checklist items not answered "yes". */
  checklist: ReviewChecklistItem[];
};

/** What approval would be refused for (Registration.CheckReadyForApproval), as far as the screen can tell. */
export function approvalBlockers(view: ReviewView): ApprovalBlockers {
  return {
    documents: view.requiredDocuments.filter((d) => d.isMandatory && uploadFor(view, d.code)?.verificationStatus !== "Verified"),
    checklist: view.checklist.filter((i) => i.isMandatory && i.isPassed !== true),
  };
}

export function isReadyToApprove(view: ReviewView): boolean {
  const blockers = approvalBlockers(view);
  return view.application.status === "UnderReview" && blockers.documents.length === 0 && blockers.checklist.length === 0;
}

/** The checklist as a form holds it: each item's answer (null for not yet) and remarks. */
export type ChecklistDraft = Record<string, { passed: boolean | null; remarks: string }>;

export function checklistDraft(items: ReviewChecklistItem[]): ChecklistDraft {
  return Object.fromEntries(items.map((i) => [i.code, { passed: i.isPassed, remarks: i.remarks ?? "" }]));
}

/** The answered items, as the API takes them; unanswered ones are left out, blank remarks sent as null. */
export function checklistAnswers(draft: ChecklistDraft): ChecklistAnswer[] {
  return Object.entries(draft)
    .filter(([, answer]) => answer.passed !== null)
    .map(([code, answer]) => ({ code, passed: answer.passed as boolean, remarks: answer.remarks.trim() || null }));
}

/** The questions worth sending: trimmed, blanks dropped. */
export function questionsToAsk(drafts: string[]): string[] {
  return drafts.map((q) => q.trim()).filter(Boolean);
}
