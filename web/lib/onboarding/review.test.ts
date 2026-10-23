import { describe, expect, it } from "vitest";
import {
  approvalBlockers,
  checklistAnswers,
  checklistDraft,
  isReadyToApprove,
  pageCount,
  parseQueueFilter,
  questionsToAsk,
  reviewStage,
} from "./review";
import { uploaded, view } from "./test-fixtures";
import type { ApplicationView, ReviewChecklistItem, ReviewView } from "./types";

function item(code: string, isMandatory: boolean, isPassed: boolean | null = null): ReviewChecklistItem {
  return { code, question: `${code}?`, isMandatory, isPassed, remarks: null, checkedAt: null };
}

function review(application: Partial<ApplicationView>, checklist: ReviewChecklistItem[] = []): ReviewView {
  return {
    application: view({ status: "UnderReview", ...application }),
    requiredDocuments: [
      { code: "REGISTRATION_CERTIFICATE", name: "Registration certificate", isMandatory: true },
      { code: "APPLICANT_NIC", name: "Applicant's NIC", isMandatory: true },
      { code: "PHOTO", name: "Photo", isMandatory: false },
    ],
    checklist,
    rejectReasons: [],
  };
}

describe("the queue", () => {
  it("takes only the statuses the API filters by, and the open work otherwise", () => {
    expect(parseQueueFilter("Approved")).toBe("Approved");
    expect(parseQueueFilter("Draft")).toBeNull();
    expect(parseQueueFilter("approved")).toBeNull();
    expect(parseQueueFilter(null)).toBeNull();
  });

  it("has at least one page, and a last partial page", () => {
    expect(pageCount(0, 20)).toBe(1);
    expect(pageCount(20, 20)).toBe(1);
    expect(pageCount(21, 20)).toBe(2);
  });
});

describe("what a reviewer may do", () => {
  it("starts a submitted application, reviews one under review, waits on questions, and is done after", () => {
    expect(reviewStage("Submitted")).toBe("start");
    expect(reviewStage("UnderReview")).toBe("review");
    expect(reviewStage("InfoRequested")).toBe("waiting");
    for (const status of ["Approved", "Rejected", "Withdrawn"] as const) expect(reviewStage(status)).toBe("closed");
  });
});

describe("what an approval would be refused for", () => {
  it("names every mandatory document not accepted yet - missing, waiting or refused - but not optional ones", () => {
    const blockers = approvalBlockers(review({ documents: [uploaded("REGISTRATION_CERTIFICATE", "Rejected")] }));
    expect(blockers.documents.map((d) => d.code)).toEqual(["REGISTRATION_CERTIFICATE", "APPLICANT_NIC"]);

    const waiting = approvalBlockers(review({ documents: [uploaded("REGISTRATION_CERTIFICATE", "Verified"), uploaded("APPLICANT_NIC")] }));
    expect(waiting.documents.map((d) => d.code)).toEqual(["APPLICANT_NIC"]);
  });

  it("names mandatory checklist items not answered yes, unanswered or no alike", () => {
    const blockers = approvalBlockers(review({}, [item("A", true, true), item("B", true, false), item("C", true), item("D", false)]));
    expect(blockers.checklist.map((i) => i.code)).toEqual(["B", "C"]);
  });

  it("is ready only under review, with every mandatory document accepted and item passed", () => {
    const documents = [uploaded("REGISTRATION_CERTIFICATE", "Verified"), uploaded("APPLICANT_NIC", "Verified")];
    const checklist = [item("A", true, true), item("D", false)];
    expect(isReadyToApprove(review({ documents }, checklist))).toBe(true);
    expect(isReadyToApprove(review({ documents, status: "InfoRequested" }, checklist))).toBe(false);
    expect(isReadyToApprove(review({ documents: documents.slice(1) }, checklist))).toBe(false);
    expect(isReadyToApprove(review({ documents }, [item("A", true, false)]))).toBe(false);
  });
});

describe("the checklist form", () => {
  it("starts from the saved answers and sends only answered items, blank remarks as null", () => {
    const draft = checklistDraft([{ ...item("A", true, true), remarks: "Seen the deed" }, item("B", true)]);
    expect(draft).toEqual({ A: { passed: true, remarks: "Seen the deed" }, B: { passed: null, remarks: "" } });

    // B not answered yet: left out, not sent as a "no".
    expect(checklistAnswers(draft)).toEqual([{ code: "A", passed: true, remarks: "Seen the deed" }]);

    draft.B = { passed: false, remarks: "   " };
    expect(checklistAnswers(draft)).toEqual([
      { code: "A", passed: true, remarks: "Seen the deed" },
      { code: "B", passed: false, remarks: null },
    ]);
  });
});

describe("questions", () => {
  it("are trimmed, and blank ones are not sent", () => {
    expect(questionsToAsk(["  Why?  ", "", "   ", "When?"])).toEqual(["Why?", "When?"]);
  });
});
