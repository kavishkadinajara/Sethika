import { apiFetch, session } from "./index";

/*  Typed calls to the auth endpoints (src/Sethika.Api/Endpoints/AuthEndpoints.cs,
    StepUpEndpoints.cs, LegalEndpoints.cs). Every failure comes back in one shape, so screens
    map `code` to a translated message instead of parsing anything.

    Anonymous endpoints use plain fetch, not apiFetch: a 401 from /login is "wrong password",
    and must not set off a token refresh.                                                  */

/** The rules a password can break, as the API names them (PasswordFailure). */
export const passwordRules = [
  "TooShort",
  "TooLong",
  "MissingUppercase",
  "MissingLowercase",
  "MissingDigit",
  "MissingNonAlphanumeric",
  "ContainsPersonalData",
  "RecentlyUsed",
] as const;
export type PasswordRule = (typeof passwordRules)[number];

export type ApiFailure = {
  /** 0 when the request never got an answer (offline, API down). */
  status: number;
  /** The `{ "code": ... }` of ApiErrors.cs, e.g. "invalid_credentials". */
  code: string | null;
  /** Seconds, from Retry-After on a 429. */
  retryAfter: number | null;
  /** From otp_incorrect. */
  attemptsRemaining: number | null;
  /** From password_rejected. */
  passwordFailures: PasswordRule[];
};

export type Result<T = void> = { ok: true; value: T } | { ok: false; failure: ApiFailure };

export type LegalDocument = {
  code: string;
  version: string;
  language: string;
  title: string;
  content: string;
  contentSha256: string;
};

export type SignUpInput = {
  phoneNumber: string;
  verificationTicket: string;
  fullName: string;
  email: string | null;
  password: string;
  preferredLanguage: string;
  acceptedDocumentHashes: string[];
};

// ---- anonymous: signup and sign-in --------------------------------------------------------------

export function requestSignupCode(phoneNumber: string, language: string) {
  return call(publicPost("/api/auth/otp/request", { phoneNumber, purpose: "signup", language }));
}

/** A correct code returns the single-use ticket signup needs (plan A17), valid 15 minutes. */
export async function verifySignupCode(phoneNumber: string, code: string): Promise<Result<string>> {
  const result = await call(publicPost("/api/auth/otp/verify", { phoneNumber, purpose: "signup", code }));
  if (!result.ok) return result;
  const body = (await result.value.json()) as { ticket: string };
  return { ok: true, value: body.ticket };
}

export async function getLegalDocuments(language: string): Promise<Result<LegalDocument[]>> {
  const result = await call(
    send(`/api/legal/current?language=${encodeURIComponent(language)}`, { method: "GET" }),
  );
  if (!result.ok) return result;
  return { ok: true, value: (await result.value.json()) as LegalDocument[] };
}

/**
 * Creates the donor account. The API signs the new donor in at once (201 with a session);
 * `signedIn` is false in the rare case it created the account but could not start the session,
 * and the screen then sends the donor to the login page.
 */
export async function signUp(input: SignUpInput): Promise<Result<{ signedIn: boolean }>> {
  const result = await call(publicPost("/api/auth/signup", input));
  if (!result.ok) return result;
  const isSession = (result.value.headers.get("content-type") ?? "").includes("json");
  if (isSession) await session.establish(result.value);
  return { ok: true, value: { signedIn: isSession } };
}

/** `login` is a phone number or an email address. */
export async function logIn(login: string, password: string): Promise<Result> {
  const result = await call(publicPost("/api/auth/login", { login, password }));
  if (!result.ok) return result;
  await session.establish(result.value);
  return { ok: true, value: undefined };
}

/**
 * Asks for a sign-in code. The API answers 202 whether or not the number has an account, and texts
 * only one that does, so a screen must never say the code "was sent".
 */
export function requestSignInCode(phoneNumber: string) {
  return call(publicPost("/api/auth/otp/request", { phoneNumber, purpose: "login" }));
}

/**
 * Signs in with a code from requestSignInCode. A wrong, expired or used code and a number with no
 * account are all 401 otp_invalid, with no attempts-left count: telling them apart would tell a
 * stranger which numbers are registered.
 */
export async function signInWithCode(phoneNumber: string, code: string): Promise<Result> {
  const result = await call(publicPost("/api/auth/otp/sign-in", { phoneNumber, code }));
  if (!result.ok) return result;
  await session.establish(result.value);
  return { ok: true, value: undefined };
}

// ---- signed in: step-up -------------------------------------------------------------------------

/** Texts a code to the account's own confirmed phone. */
export function requestStepUpCode() {
  return call(apiFetch("/api/auth/stepup/request", { method: "POST" }));
}

/** On success the current access token may perform RequiresMfa actions until it is refreshed (A20). */
export function verifyStepUpCode(code: string) {
  return call(
    apiFetch("/api/auth/stepup/verify", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ code }),
    }),
  );
}

// ---- plumbing -----------------------------------------------------------------------------------

function send(path: string, init: RequestInit) {
  return fetch(path, { credentials: "same-origin", cache: "no-store", ...init });
}

function publicPost(path: string, body: unknown) {
  return send(path, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
}

async function call(pending: Promise<Response>): Promise<Result<Response>> {
  let response: Response;
  try {
    response = await pending;
  } catch {
    return { ok: false, failure: failure(0) };
  }
  if (response.ok) return { ok: true, value: response };
  return { ok: false, failure: await readFailure(response) };
}

function failure(status: number, extra: Partial<ApiFailure> = {}): ApiFailure {
  return { status, code: null, retryAfter: null, attemptsRemaining: null, passwordFailures: [], ...extra };
}

/** Reads an error response: `{ code, attemptsRemaining?, failures? }` and Retry-After. */
export async function readFailure(response: Response): Promise<ApiFailure> {
  const retryAfterHeader = Number(response.headers.get("retry-after"));
  const retryAfter = Number.isFinite(retryAfterHeader) && retryAfterHeader > 0 ? retryAfterHeader : null;

  let body: { code?: unknown; attemptsRemaining?: unknown; failures?: unknown } = {};
  if ((response.headers.get("content-type") ?? "").includes("json")) {
    try {
      body = await response.json();
    } catch {
      body = {};
    }
  }

  return failure(response.status, {
    code: typeof body.code === "string" ? body.code : null,
    retryAfter,
    attemptsRemaining: typeof body.attemptsRemaining === "number" ? body.attemptsRemaining : null,
    passwordFailures: Array.isArray(body.failures)
      ? body.failures.filter((f): f is PasswordRule => (passwordRules as readonly string[]).includes(f))
      : [],
  });
}
