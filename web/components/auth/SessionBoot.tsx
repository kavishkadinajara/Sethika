"use client";

import { useLocale } from "next-intl";
import { useEffect, useRef } from "react";
import { usePathname, useRouter } from "@/i18n/navigation";
import { routing } from "@/i18n/routing";
import { session } from "@/lib/auth";
import { accountLocaleToAdopt } from "@/lib/auth/account-locale";
import { loadAccountLanguage } from "@/lib/auth/api";

/**
 * Silent refresh at app boot (A2): the access token died with the last page, so ask for a new
 * one from the refresh cookie before anything that depends on identity renders. Renders nothing.
 *
 * Once signed in, the page moves to the account's language if it differs (A11), so a language
 * chosen on another device wins over this browser's cookie. Only at boot: a sign-in lands in the
 * account's language itself (useLandAfterSignIn), and a switch made here is saved to the account.
 */
export function SessionBoot() {
  const locale = useLocale();
  const pathname = usePathname();
  const router = useRouter();

  // Read when the boot finishes, not when it began: the visitor may have moved on meanwhile.
  const latest = useRef({ locale, pathname, router });
  useEffect(() => {
    latest.current = { locale, pathname, router };
  });

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      await session.start();
      if (cancelled || session.getState().status !== "authenticated") return;

      const language = await loadAccountLanguage();
      const now = latest.current;
      const target = accountLocaleToAdopt(language, now.locale, routing.locales);
      if (!cancelled && target) now.router.replace(`${now.pathname}${window.location.search}`, { locale: target });
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  return null;
}
