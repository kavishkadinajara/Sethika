import type { ApplicationView, UploadedDocument } from "./types";

/*  Sample data for the onboarding unit tests (progress.test.ts, review.test.ts). Not a test file:
    Vitest runs only *.test.ts.                                                                 */

export function view(overrides: Partial<ApplicationView> = {}): ApplicationView {
  return {
    requestNo: "REQ-2026-000001",
    status: "Draft",
    institutionType: "TEMPLE",
    proposedName: "Sri Sudharmaramaya",
    nameSi: null,
    nameTa: null,
    officialRegistrationNo: "BUD/GAL/1952/77",
    registeringAuthority: "Department of Buddhist Affairs",
    registrationDate: "1952-04-01",
    establishedYear: 1890,
    residentCapacity: null,
    description: null,
    addressLine1: "12 Temple Road",
    addressLine2: null,
    city: "Galle",
    district: "GLL",
    gnDivision: null,
    postalCode: "80000",
    latitude: 6.0535,
    longitude: 80.221,
    officePhone: "+94912234567",
    officeEmail: null,
    website: null,
    applicantFullName: "Kamal Perera",
    applicantNic: "199012345678",
    applicantDesignation: "Secretary",
    applicantPhone: "+94771000001",
    applicantEmail: null,
    createdAt: "2026-10-18T09:00:00Z",
    submittedAt: null,
    decidedAt: null,
    rejectReason: null,
    decisionNote: null,
    institutionCode: null,
    institutionSlug: null,
    officeBearers: [],
    documents: [],
    questions: [],
    history: [],
    ...overrides,
  };
}

export function uploaded(documentType: string, verificationStatus: UploadedDocument["verificationStatus"] = "Pending"): UploadedDocument {
  return {
    documentType,
    fileName: `${documentType}.pdf`,
    sizeBytes: 1000,
    verificationStatus,
    reviewerRemarks: null,
    referenceNo: null,
    issuedBy: null,
    issuedDate: null,
    expiryDate: null,
    uploadedAt: "2026-10-18T09:00:00Z",
  };
}
