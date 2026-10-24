import { errorCode } from "@/auth/session-client";
import { API_BASE_URL, APP_USER_AGENT } from "@/config";
import { hasMessage, type Translate } from "@/i18n/translate";

/*  Anonymous calls (codes, legal documents) and turning an API answer into words. Signed-in
    calls go through the session client's fetch() instead, which adds the Bearer token.      */

export type ApiFailure = { message: string; code: string | null; body: Record<string, unknown> | null };

/** POSTs JSON to an anonymous endpoint. Network failures reject; HTTP errors resolve. */
export function postJson(path: string, body: unknown): Promise<Response> {
  return fetch(`${API_BASE_URL}${path}`, {
    method: "POST",
    headers: { "Content-Type": "application/json", "User-Agent": APP_USER_AGENT },
    body: JSON.stringify(body),
  });
}

export function getJson(path: string): Promise<Response> {
  return fetch(`${API_BASE_URL}${path}`, { headers: { "User-Agent": APP_USER_AGENT } });
}

/** The words for a refused request, in the screen's language. */
export async function describeFailure(response: Response, t: Translate): Promise<ApiFailure> {
  const code = await errorCode(response);
  let body: Record<string, unknown> | null = null;
  try {
    body = (await response.clone().json()) as Record<string, unknown>;
  } catch {
    // Not JSON: a proxy's error page, a 502.
  }

  if (response.status === 429) {
    return { message: t("Errors.tooManyRequestsNoTime"), code, body };
  }

  if (code === "otp_incorrect") {
    const count = typeof body?.attemptsRemaining === "number" ? body.attemptsRemaining : 0;
    return { message: t("Errors.otp_incorrect", { count }), code, body };
  }

  const key = `Errors.${code}`;
  return { message: code && hasMessage(key) ? t(key) : t("Errors.unexpected"), code, body };
}

export function networkFailure(t: Translate): ApiFailure {
  return { message: t("Errors.network"), code: null, body: null };
}
