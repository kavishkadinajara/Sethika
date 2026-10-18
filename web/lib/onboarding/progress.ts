import type {
  ApplicationDetails,
  ApplicationStatus,
  ApplicationView,
  Lookups,
  OfficeBearer,
  RequiredDocument,
} from "./types";

/*  The application form's step logic, kept free of React so it is unit-tested (progress.test.ts).
    The API decides every rule again; these only decide what the screen offers and where a
    resumed draft opens.                                                                        */

export const wizardSteps = ["details", "bearers", "documents", "declaration"] as const;
export type WizardStep = (typeof wizardSteps)[number];

/** More than enough for a committee; the API's own limit (ApplicationInput.MaxOfficeBearers). */
export const MAX_OFFICE_BEARERS = 15;

/** A lookup's name in the screen's language, English when there is none. */
export function localName(option: { name: string; nameSi: string | null; nameTa: string | null }, locale: string): string {
  return (locale === "si" ? option.nameSi : locale === "ta" ? option.nameTa : null) || option.name;
}

/** The applicant may change details, office bearers and documents only here (plan 02, B3). */
export function isEditable(status: ApplicationStatus): boolean {
  return status === "Draft" || status === "InfoRequested";
}

/** Withdrawal is open until a reviewer has the application or a decision is made. */
export function canWithdraw(status: ApplicationStatus): boolean {
  return status === "Draft" || status === "Submitted" || status === "InfoRequested";
}

/** The documents an institution type provides, mandatory first, in the catalogue's order. */
export function documentsFor(lookups: Lookups, institutionType: string): RequiredDocument[] {
  return lookups.documents
    .filter((d) => d.institutionType === institutionType)
    .sort((a, b) => Number(b.isMandatory) - Number(a.isMandatory) || a.sortOrder - b.sortOrder);
}

export type Missing = {
  officeBearers: boolean;
  signatory: boolean;
  /** Codes of mandatory documents with no upload. */
  documents: string[];
};

/** What submit would refuse, as far as the screen can tell. */
export function missingBeforeSubmit(view: ApplicationView, lookups: Lookups): Missing {
  const uploaded = new Set(view.documents.map((d) => d.documentType));
  return {
    officeBearers: view.officeBearers.length === 0,
    signatory: !view.officeBearers.some((b) => b.isSignatory),
    documents: documentsFor(lookups, view.institutionType)
      .filter((d) => d.isMandatory && !uploaded.has(d.code))
      .map((d) => d.code),
  };
}

export function isReadyToSubmit(missing: Missing): boolean {
  return !missing.officeBearers && !missing.signatory && missing.documents.length === 0;
}

/** Where a resumed draft opens: the first step with something left to do. */
export function resumeStep(view: ApplicationView, lookups: Lookups): WizardStep {
  const missing = missingBeforeSubmit(view, lookups);
  if (missing.officeBearers || missing.signatory) return "bearers";
  if (missing.documents.length > 0) return "documents";
  return "declaration";
}

export function emptyDetails(): ApplicationDetails {
  return {
    institutionType: "",
    proposedName: "",
    nameSi: "",
    nameTa: "",
    officialRegistrationNo: "",
    registeringAuthority: "",
    registrationDate: "",
    establishedYear: "",
    residentCapacity: "",
    description: "",
    addressLine1: "",
    addressLine2: "",
    city: "",
    district: "",
    gnDivision: "",
    postalCode: "",
    latitude: "",
    longitude: "",
    officePhone: "",
    officeEmail: "",
    website: "",
    applicantFullName: "",
    applicantNic: "",
    applicantDesignation: "",
    applicantPhone: "",
    applicantEmail: "",
  };
}

/** The form's text fields from a saved application: numbers as text, nulls as blanks. */
export function detailsFromView(view: ApplicationView): ApplicationDetails {
  const text = (value: string | number | null) => (value === null ? "" : String(value));
  const details = emptyDetails();
  for (const key of Object.keys(details) as (keyof ApplicationDetails)[]) {
    details[key] = text(view[key]);
  }
  return details;
}

export function emptyBearer(isSignatory = false): OfficeBearer {
  return { fullName: "", nic: "", designation: "", phone: "", email: "", isSignatory };
}

export function bearersFromView(view: ApplicationView): OfficeBearer[] {
  return view.officeBearers.map((b) => ({ ...b, email: b.email ?? "" }));
}

/**
 * The API names a bearer's field as `officeBearers[1].nic`. Returns, per row, the fields at
 * fault; `officeBearers` alone (too many, or none) goes under -1.
 */
export function bearerFieldErrors(fields: string[]): Map<number, Set<keyof OfficeBearer | "row">> {
  const errors = new Map<number, Set<keyof OfficeBearer | "row">>();
  const add = (row: number, field: keyof OfficeBearer | "row") => {
    const set = errors.get(row) ?? new Set();
    set.add(field);
    errors.set(row, set);
  };

  for (const name of fields) {
    if (name === "officeBearers") {
      add(-1, "row");
      continue;
    }
    const match = /^officeBearers\[(\d+)\]\.(fullName|nic|designation|phone|email)$/.exec(name);
    if (match) add(Number(match[1]), match[2] as keyof OfficeBearer);
  }
  return errors;
}

export type FileProblem = "type" | "size" | "empty";

/**
 * Checks a chosen file against the document type before uploading it, so a wrong file is named
 * at once. The API checks again, including the file's first bytes, which the browser cannot.
 */
export function fileProblem(document: Pick<RequiredDocument, "allowedExtensions" | "maxSizeMb">, file: { name: string; size: number }): FileProblem | null {
  if (file.size === 0) return "empty";
  const dot = file.name.lastIndexOf(".");
  const extension = dot >= 0 ? file.name.slice(dot).toLowerCase() : "";
  const allowed = document.allowedExtensions.split(",").map((e) => e.trim().toLowerCase());
  if (!extension || !allowed.includes(extension)) return "type";
  if (file.size > document.maxSizeMb * 1024 * 1024) return "size";
  return null;
}

/** The accept attribute for a file input: ".pdf,.jpg,.jpeg,.png". */
export function acceptAttribute(document: Pick<RequiredDocument, "allowedExtensions">): string {
  return document.allowedExtensions
    .split(",")
    .map((e) => e.trim())
    .filter(Boolean)
    .join(",");
}

/** An OpenStreetMap link to the pin, for the applicant (and later the reviewer) to compare. */
export function mapLink(latitude: string | number, longitude: string | number): string | null {
  const lat = Number(latitude);
  const lon = Number(longitude);
  if (!Number.isFinite(lat) || !Number.isFinite(lon) || (String(latitude).trim() === "" || String(longitude).trim() === "")) return null;
  return `https://www.openstreetmap.org/?mlat=${lat}&mlon=${lon}#map=17/${lat}/${lon}`;
}
