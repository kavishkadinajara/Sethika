import { describe, expect, it } from "vitest";
import { toDetailsBody } from "./api";
import {
  bearerFieldErrors,
  canWithdraw,
  detailsFromView,
  documentsFor,
  emptyDetails,
  fileProblem,
  isEditable,
  isReadyToSubmit,
  mapLink,
  missingBeforeSubmit,
  resumeStep,
} from "./progress";
import { uploaded, view } from "./test-fixtures";
import type { ApplicationStatus, Lookups, RequiredDocument } from "./types";

function document(code: string, isMandatory: boolean, sortOrder: number, institutionType = "TEMPLE"): RequiredDocument {
  return {
    institutionType,
    code,
    name: code,
    description: null,
    instructions: null,
    isMandatory,
    sortOrder,
    allowedExtensions: ".pdf,.jpg,.jpeg,.png",
    maxSizeMb: 5,
    requiresReferenceNo: false,
    requiresIssueDate: false,
    requiresExpiryDate: false,
  };
}

const lookups: Lookups = {
  institutionTypes: [],
  districts: [],
  documents: [
    document("PROOF_OF_ADDRESS", false, 3),
    document("REGISTRATION_CERTIFICATE", true, 1),
    document("APPLICANT_NIC", true, 2),
    document("CHILD_WELFARE_LICENCE", true, 1, "CHILDRENS_HOME"),
  ],
  declaration: null,
};

const signatory = { fullName: "Chief Trustee", nic: "561234567V", designation: "Trustee", phone: "+94771234567", email: null, isSignatory: true };

describe("which states allow what", () => {
  const all: ApplicationStatus[] = ["Draft", "Submitted", "UnderReview", "InfoRequested", "Approved", "Rejected", "Withdrawn"];

  it("edits only a draft or an application with open questions", () => {
    expect(all.filter(isEditable)).toEqual(["Draft", "InfoRequested"]);
  });

  it("withdraws until a reviewer has it or a decision is made", () => {
    expect(all.filter(canWithdraw)).toEqual(["Draft", "Submitted", "InfoRequested"]);
  });
});

describe("documentsFor", () => {
  it("is the type's own documents, mandatory first, then in the catalogue's order", () => {
    expect(documentsFor(lookups, "TEMPLE").map((d) => d.code)).toEqual(["REGISTRATION_CERTIFICATE", "APPLICANT_NIC", "PROOF_OF_ADDRESS"]);
  });
});

describe("what submit would refuse, and where a draft resumes", () => {
  it("a new draft lacks office bearers, a signatory and every mandatory document, and opens on office bearers", () => {
    const missing = missingBeforeSubmit(view(), lookups);
    expect(missing).toEqual({ officeBearers: true, signatory: true, documents: ["REGISTRATION_CERTIFICATE", "APPLICANT_NIC"] });
    expect(isReadyToSubmit(missing)).toBe(false);
    expect(resumeStep(view(), lookups)).toBe("bearers");
  });

  it("office bearers without a signatory are not enough", () => {
    const draft = view({ officeBearers: [{ ...signatory, isSignatory: false }] });
    expect(missingBeforeSubmit(draft, lookups).signatory).toBe(true);
    expect(resumeStep(draft, lookups)).toBe("bearers");
  });

  it("with a signatory it opens on documents, and an optional one is never required", () => {
    const draft = view({ officeBearers: [signatory], documents: [uploaded("REGISTRATION_CERTIFICATE")] });
    expect(missingBeforeSubmit(draft, lookups).documents).toEqual(["APPLICANT_NIC"]);
    expect(resumeStep(draft, lookups)).toBe("documents");
  });

  it("with every mandatory document it is ready and opens on the declaration", () => {
    const draft = view({ officeBearers: [signatory], documents: [uploaded("REGISTRATION_CERTIFICATE"), uploaded("APPLICANT_NIC")] });
    expect(isReadyToSubmit(missingBeforeSubmit(draft, lookups))).toBe(true);
    expect(resumeStep(draft, lookups)).toBe("declaration");
  });
});

describe("the details form", () => {
  it("reads a saved application back as text, nulls as blanks", () => {
    const details = detailsFromView(view());
    expect(details.establishedYear).toBe("1890");
    expect(details.latitude).toBe("6.0535");
    expect(details.residentCapacity).toBe("");
    expect(details.nameSi).toBe("");
  });

  it("sends blanks as null and numbers as numbers, and leaves text that is not a number for the API to name", () => {
    const body = toDetailsBody({ ...emptyDetails(), proposedName: "  Sri Sudharmaramaya ", latitude: "6.0535", establishedYear: "about 1890" });
    expect(body.proposedName).toBe("Sri Sudharmaramaya");
    expect(body.nameSi).toBeNull();
    expect(body.latitude).toBe(6.0535);
    expect(body.longitude).toBeNull();
    expect(body.establishedYear).toBe("about 1890");
  });
});

describe("bearerFieldErrors", () => {
  it("groups the API's field names by row, and the list itself under -1", () => {
    const errors = bearerFieldErrors(["officeBearers[1].nic", "officeBearers[1].phone", "officeBearers[0].fullName", "officeBearers", "proposedName"]);
    expect([...errors.get(1)!]).toEqual(["nic", "phone"]);
    expect([...errors.get(0)!]).toEqual(["fullName"]);
    expect([...errors.get(-1)!]).toEqual(["row"]);
    expect(errors.size).toBe(3);
  });
});

describe("fileProblem", () => {
  const certificate = document("REGISTRATION_CERTIFICATE", true, 1);

  it("takes an allowed extension in any case, up to the size limit exactly", () => {
    expect(fileProblem(certificate, { name: "Certificate.PDF", size: 5 * 1024 * 1024 })).toBeNull();
  });

  it("refuses another type, a file with no extension, one over the limit and an empty one", () => {
    expect(fileProblem(certificate, { name: "certificate.exe", size: 10 })).toBe("type");
    expect(fileProblem(certificate, { name: "certificate", size: 10 })).toBe("type");
    expect(fileProblem(certificate, { name: "certificate.pdf", size: 5 * 1024 * 1024 + 1 })).toBe("size");
    expect(fileProblem(certificate, { name: "certificate.pdf", size: 0 })).toBe("empty");
  });
});

describe("mapLink", () => {
  it("points OpenStreetMap at the pin, and is null until both coordinates are numbers", () => {
    expect(mapLink("6.0535", "80.221")).toBe("https://www.openstreetmap.org/?mlat=6.0535&mlon=80.221#map=17/6.0535/80.221");
    expect(mapLink("", "80.221")).toBeNull();
    expect(mapLink("north", "80.221")).toBeNull();
  });
});
