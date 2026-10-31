import { apiFetch } from "@/lib/auth";
import { readFailure, type ApiFailure } from "@/lib/auth/api";
import type {
  ApplicationDetails,
  ApplicationSummary,
  ApplicationView,
  DocumentMeta,
  Lookups,
  OfficeBearer,
} from "./types";

/*  Typed calls to /api/onboarding/applications (OnboardingApplicationEndpoints.cs), all through
    the session's apiFetch: the applicant is signed in, and a 401 means the session needs a refresh.
    A refusal comes back as `{ code, fields }`; `fields` names the API's fields at fault, such as
    "proposedName" or "officeBearers[1].nic".                                                   */

export type OnboardingFailure = ApiFailure & { fields: string[] };

export type OnboardingResult<T = void> = { ok: true; value: T } | { ok: false; failure: OnboardingFailure };

const BASE = "/api/onboarding/applications";

export async function getLookups(language: string): Promise<OnboardingResult<Lookups>> {
  return json(await send(`${BASE}/lookups?language=${encodeURIComponent(language)}`, { method: "GET" }));
}

export async function listApplications(): Promise<OnboardingResult<ApplicationSummary[]>> {
  return json(await send(`${BASE}/`, { method: "GET" }));
}

export async function getApplication(requestNo: string): Promise<OnboardingResult<ApplicationView>> {
  return json(await send(`${BASE}/${encodeURIComponent(requestNo)}`, { method: "GET" }));
}

/** Starts a draft from the details; the answer is its request number. */
export async function createApplication(details: ApplicationDetails): Promise<OnboardingResult<string>> {
  const result = await json<{ requestNo: string }>(await send(`${BASE}/`, jsonBody("POST", toDetailsBody(details))));
  return result.ok ? { ok: true, value: result.value.requestNo } : result;
}

export async function updateApplication(requestNo: string, details: ApplicationDetails): Promise<OnboardingResult> {
  return empty(await send(`${BASE}/${encodeURIComponent(requestNo)}`, jsonBody("PUT", toDetailsBody(details))));
}

export async function replaceOfficeBearers(requestNo: string, bearers: OfficeBearer[]): Promise<OnboardingResult> {
  const officeBearers = bearers.map((b) => ({ ...b, email: blankToNull(b.email) }));
  return empty(await send(`${BASE}/${encodeURIComponent(requestNo)}/office-bearers`, jsonBody("PUT", { officeBearers })));
}

/** Uploads, or replaces, the file for one document type. */
export async function uploadDocument(
  requestNo: string,
  documentType: string,
  file: File,
  meta: DocumentMeta,
): Promise<OnboardingResult> {
  const form = new FormData();
  form.append("file", file);
  for (const [key, value] of Object.entries(meta)) {
    if (value.trim()) form.append(key, value.trim());
  }
  return empty(
    await send(`${BASE}/${encodeURIComponent(requestNo)}/documents/${encodeURIComponent(documentType)}`, {
      method: "PUT",
      body: form,
    }),
  );
}

/** The uploaded file's bytes. Through apiFetch, because the download needs the Bearer token. */
export async function downloadDocument(requestNo: string, documentType: string): Promise<OnboardingResult<Blob>> {
  const result = await send(
    `${BASE}/${encodeURIComponent(requestNo)}/documents/${encodeURIComponent(documentType)}/file`,
    { method: "GET" },
  );
  return result.ok ? { ok: true, value: await result.value.blob() } : result;
}

/** Submits with the declaration the applicant read, identified by its content hash. */
export async function submitApplication(requestNo: string, declarationSha256: string): Promise<OnboardingResult> {
  return empty(await send(`${BASE}/${encodeURIComponent(requestNo)}/submit`, jsonBody("POST", { declarationSha256 })));
}

/** Answers every open question, in the order asked. */
export async function answerQuestions(requestNo: string, answers: string[]): Promise<OnboardingResult> {
  return empty(await send(`${BASE}/${encodeURIComponent(requestNo)}/answers`, jsonBody("POST", { answers })));
}

export async function withdrawApplication(requestNo: string): Promise<OnboardingResult> {
  return empty(await send(`${BASE}/${encodeURIComponent(requestNo)}/withdraw`, { method: "POST" }));
}

// ---- the request body ---------------------------------------------------------------------------

/**
 * The form keeps every field as text; the API wants numbers as numbers and blanks as null, so a
 * blank optional field is "not given" rather than an empty string the API would refuse.
 */
export function toDetailsBody(details: ApplicationDetails): Record<string, string | number | null> {
  const body: Record<string, string | number | null> = {};
  for (const [key, value] of Object.entries(details)) body[key] = blankToNull(value);
  for (const key of ["establishedYear", "residentCapacity", "latitude", "longitude"] as const) {
    body[key] = toNumber(details[key]);
  }
  return body;
}

/** A number, null when blank, and NaN-free: text that is not a number is sent as given for the API to name. */
function toNumber(value: string): number | string | null {
  const trimmed = value.trim();
  if (!trimmed) return null;
  const number = Number(trimmed);
  return Number.isFinite(number) ? number : trimmed;
}

function blankToNull(value: string): string | null {
  const trimmed = value.trim();
  return trimmed ? trimmed : null;
}

// ---- plumbing, shared with review-api.ts --------------------------------------------------------

export function jsonBody(method: string, body: unknown): RequestInit {
  return { method, headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) };
}

export async function send(path: string, init: RequestInit): Promise<OnboardingResult<Response>> {
  let response: Response;
  try {
    response = await apiFetch(path, { cache: "no-store", ...init });
  } catch {
    // No answer at all (offline, API down): status 0, as lib/auth/api.ts reports it.
    return {
      ok: false,
      failure: { status: 0, code: null, retryAfter: null, attemptsRemaining: null, passwordFailures: [], fields: [] },
    };
  }
  if (response.ok) return { ok: true, value: response };
  return { ok: false, failure: await readOnboardingFailure(response) };
}

/**
 * The API's `{ code, fields? }`, with the usual status, Retry-After and code. A role refusal names
 * permissions instead of fields (`{ code, permissions }`, InstitutionRoleEndpoints.cs); those come
 * back in `fields` too, so the role editor can mark them.
 */
export async function readOnboardingFailure(response: Response): Promise<OnboardingFailure> {
  const fields = await readFields(response.clone());
  return { ...(await readFailure(response)), fields };
}

async function readFields(response: Response): Promise<string[]> {
  if (!(response.headers.get("content-type") ?? "").includes("json")) return [];
  try {
    const body = (await response.json()) as { fields?: unknown; permissions?: unknown };
    const list = Array.isArray(body.fields) ? body.fields : Array.isArray(body.permissions) ? body.permissions : [];
    return list.filter((f): f is string => typeof f === "string");
  } catch {
    return [];
  }
}

export async function json<T>(result: OnboardingResult<Response>): Promise<OnboardingResult<T>> {
  if (!result.ok) return result;
  return { ok: true, value: (await result.value.json()) as T };
}

export async function empty(result: OnboardingResult<Response>): Promise<OnboardingResult> {
  return result.ok ? { ok: true, value: undefined } : result;
}
