"use client";

import { useSyncExternalStore } from "react";
import { sessionHintScript } from "@/components/shell/session-hint";
import { themeBootScript } from "./theme";

const noSubscription = () => () => {};

/**
 * Inline <script> for <head>: applies a saved light/dark choice while the HTML is still being
 * parsed, before anything is painted (Next.js guide "Preventing flash before hydration").
 * Reading the cookie here rather than with cookies() on the server keeps pages statically
 * renderable. <html> carries suppressHydrationWarning because this script changes it. The same
 * script also marks a likely session for the home page (components/shell/session-hint.ts).
 *
 * Switching locale re-mounts the root layout in the browser, and React then warns about any
 * <script> it creates there, since such a script never runs. So a client mount renders it as
 * an inert text/plain block: the server HTML and the hydration of it keep the runnable script,
 * and after a switch ThemeSync puts the theme back instead.
 */
export function ThemeScript() {
  const mountedOnClient = useSyncExternalStore(noSubscription, () => true, () => false);

  return (
    <script
      type={mountedOnClient ? "text/plain" : undefined}
      dangerouslySetInnerHTML={{ __html: themeBootScript + ";" + sessionHintScript }}
    />
  );
}
