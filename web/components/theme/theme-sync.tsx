"use client";

import { usePathname } from "next/navigation";
import { useLayoutEffect } from "react";
import { syncThemeFromCookie } from "./theme";

/**
 * Keeps a saved light/dark choice on <html> across client-side navigations. ThemeScript
 * covers full page loads; this covers everything after hydration. Renders nothing.
 */
export function ThemeSync() {
  const pathname = usePathname();

  useLayoutEffect(() => {
    syncThemeFromCookie();
  }, [pathname]);

  return null;
}
