import { themeBootScript } from "./theme";

/**
 * Inline <script> for <head>: applies a saved light/dark choice while the HTML is still being
 * parsed, before anything is painted (Next.js guide "Preventing flash before hydration").
 * Reading the cookie here rather than with cookies() on the server keeps pages statically
 * renderable. <html> carries suppressHydrationWarning because this script changes it.
 */
export function ThemeScript() {
  return <script dangerouslySetInnerHTML={{ __html: themeBootScript }} />;
}
