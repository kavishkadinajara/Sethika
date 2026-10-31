/** The readable double-submit cookie the API sets beside the httpOnly refresh cookie (A2). */
export const CSRF_COOKIE = "sethika-csrf";

/** Reads one cookie from a `document.cookie`-style string. */
export function readCookie(cookieString: string, name: string): string | null {
  for (const part of cookieString.split(";")) {
    const separator = part.indexOf("=");
    if (separator === -1) continue;
    if (part.slice(0, separator).trim() === name) {
      const value = part.slice(separator + 1).trim();
      return value === "" ? null : decodeURIComponent(value);
    }
  }
  return null;
}
