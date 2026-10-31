/**
 * Where to go after signing in: the `next` query parameter, if it is a path on this site.
 * Anything else ("//evil.example", "https://...", "/\\evil", "/api/...") falls back to "/",
 * so a crafted login link cannot send a donor to another site (open redirect).
 * Paths are locale-less ("/select-institution"); the locale-aware router adds the prefix.
 */
export function safeNextPath(raw: string | null | undefined, fallback = "/"): string {
  if (!raw || !raw.startsWith("/") || raw.startsWith("//") || raw.includes("\\")) return fallback;
  if (/[\u0000-\u001f]/.test(raw)) return fallback;
  if (raw === "/api" || raw.startsWith("/api/")) return fallback;
  return raw;
}

/** Reads `next` from the current URL. Call in event handlers, not during render (prerendering). */
export function readNextPath(fallback = "/"): string {
  if (typeof window === "undefined") return fallback;
  return safeNextPath(new URLSearchParams(window.location.search).get("next"), fallback);
}
