import type { Messages } from "next-intl";

/**
 * The slice of the catalogue a NextIntlClientProvider hands to the browser. Server
 * components read every message on the server; only namespaces that client components
 * call useTranslations() with need to travel, so each page stays light as the catalogue
 * grows.
 */
export function pickMessages<K extends keyof Messages>(messages: Messages, namespaces: readonly K[]) {
  return Object.fromEntries(namespaces.map((namespace) => [namespace, messages[namespace]])) as Pick<
    Messages,
    K
  >;
}

/** Namespaces the shared client components (components/ui, permissions) use on every page. */
export const sharedClientNamespaces = [
  "Common",
  "Toast",
  "LanguageSwitcher",
  "ThemeSwitcher",
  "PermissionDebug",
] as const;
