/*  The shapes of /api/onboarding/applications (OnboardingApplicationEndpoints.cs), as the API's
    JSON writes them: camelCase, dates as "yyyy-MM-dd", times as ISO strings.                */

export type ApplicationStatus =
  | "Draft"
  | "Submitted"
  | "UnderReview"
  | "InfoRequested"
  | "Approved"
  | "Rejected"
  | "Withdrawn";

/** What the applicant types about the institution and themselves (ApplicationDetails). */
export type ApplicationDetails = {
  institutionType: string;
  proposedName: string;
  nameSi: string;
  nameTa: string;
  officialRegistrationNo: string;
  registeringAuthority: string;
  /** yyyy-MM-dd, as an HTML date input gives it. */
  registrationDate: string;
  establishedYear: string;
  residentCapacity: string;
  description: string;
  addressLine1: string;
  addressLine2: string;
  city: string;
  district: string;
  gnDivision: string;
  postalCode: string;
  latitude: string;
  longitude: string;
  officePhone: string;
  officeEmail: string;
  website: string;
  applicantFullName: string;
  applicantNic: string;
  applicantDesignation: string;
  applicantPhone: string;
  applicantEmail: string;
};

export type OfficeBearer = {
  fullName: string;
  nic: string;
  designation: string;
  phone: string;
  email: string;
  isSignatory: boolean;
};

export type InstitutionTypeOption = { code: string; name: string; nameSi: string | null; nameTa: string | null; category: string };

export type DistrictOption = { code: string; name: string; nameSi: string | null; nameTa: string | null };

export type RequiredDocument = {
  institutionType: string;
  code: string;
  name: string;
  description: string | null;
  instructions: string | null;
  isMandatory: boolean;
  sortOrder: number;
  /** ".pdf,.jpg,.jpeg,.png" */
  allowedExtensions: string;
  maxSizeMb: number;
  requiresReferenceNo: boolean;
  requiresIssueDate: boolean;
  requiresExpiryDate: boolean;
};

export type Declaration = {
  code: string;
  version: string;
  language: string;
  title: string;
  content: string;
  contentSha256: string;
};

export type Lookups = {
  institutionTypes: InstitutionTypeOption[];
  districts: DistrictOption[];
  documents: RequiredDocument[];
  declaration: Declaration | null;
};

export type ApplicationSummary = {
  requestNo: string;
  proposedName: string;
  institutionType: string;
  status: ApplicationStatus;
  createdAt: string;
  submittedAt: string | null;
  decidedAt: string | null;
};

export type UploadedDocument = {
  documentType: string;
  fileName: string;
  sizeBytes: number;
  verificationStatus: "Pending" | "Verified" | "Rejected";
  reviewerRemarks: string | null;
  referenceNo: string | null;
  issuedBy: string | null;
  issuedDate: string | null;
  expiryDate: string | null;
  uploadedAt: string;
};

export type Question = { number: number; question: string; askedAt: string; response: string | null; respondedAt: string | null };

export type StatusChange = { fromStatus: string | null; toStatus: ApplicationStatus; changedAt: string };

/** An application as its applicant sees it (ApplicationView). */
export type ApplicationView = {
  requestNo: string;
  status: ApplicationStatus;
  institutionType: string;
  proposedName: string;
  nameSi: string | null;
  nameTa: string | null;
  officialRegistrationNo: string;
  registeringAuthority: string;
  registrationDate: string;
  establishedYear: number | null;
  residentCapacity: number | null;
  description: string | null;
  addressLine1: string;
  addressLine2: string | null;
  city: string;
  district: string;
  gnDivision: string | null;
  postalCode: string | null;
  latitude: number;
  longitude: number;
  officePhone: string;
  officeEmail: string | null;
  website: string | null;
  applicantFullName: string;
  applicantNic: string;
  applicantDesignation: string;
  applicantPhone: string;
  applicantEmail: string | null;
  createdAt: string;
  submittedAt: string | null;
  decidedAt: string | null;
  rejectReason: string | null;
  decisionNote: string | null;
  institutionCode: string | null;
  institutionSlug: string | null;
  officeBearers: (Omit<OfficeBearer, "email"> & { email: string | null })[];
  documents: UploadedDocument[];
  questions: Question[];
  history: StatusChange[];
};

/** A document upload's metadata; empty strings are "not given". */
export type DocumentMeta = { referenceNo: string; issuedBy: string; issuedDate: string; expiryDate: string };
