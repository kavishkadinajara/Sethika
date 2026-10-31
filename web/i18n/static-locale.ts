import { hasLocale } from "next-intl";
import { setRequestLocale } from "next-intl/server";
import { notFound } from "next/navigation";
import { use } from "react";
import { routing } from "./routing";

/**
 * For a page under app/[locale]: checks the locale and hands it to next-intl directly, which
 * lets the page be prerendered (next-intl otherwise reads it from the request, making the page
 * dynamic). Call it first in every page.
 */
export function usePageLocale(params: Promise<{ locale: string }>) {
  const { locale } = use(params);
  if (!hasLocale(routing.locales, locale)) notFound();
  setRequestLocale(locale);
  return locale;
}
