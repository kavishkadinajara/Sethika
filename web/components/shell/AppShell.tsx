"use client";

import { useTranslations } from "next-intl";
import { useEffect, useMemo, useState, type ReactNode } from "react";
import { SupportBanner } from "@/components/admin/SupportBanner";
import { BrandMark } from "@/components/auth/BrandMark";
import { Button } from "@/components/ui/button";
import { ThemeSwitcher } from "@/components/ui/theme-switcher";
import { Link, usePathname } from "@/i18n/navigation";
import { useSession } from "@/lib/auth/useSession";
import { usePermissions } from "@/lib/permissions/usePermissions";
import { jumpTargets, visibleNav, type NavSection } from "@/lib/shell/nav";
import { cn, focusRing } from "@/lib/utils";
import { JumpPalette } from "./JumpPalette";
import { LanguageMenu } from "./LanguageMenu";
import { MobileNav } from "./MobileNav";
import { Sidebar } from "./Sidebar";
import { UserMenu } from "./UserMenu";
import { useShellData } from "./useShellData";

/*  The page takes the whole width beside the sidebar, on a wide screen too (the user, 2026-10-25: a
    centred column left both sides empty). Lists add columns as the screen grows; a form or a
    paragraph keeps its own readable width inside the page (max-w-3xl, max-w-prose). One cap for
    every page today; the names stay so a layout can say what it holds.                       */
export const shellWidths = {
  narrow: "max-w-[110rem]",
  wide: "max-w-[110rem]",
  full: "max-w-[110rem]",
} as const;

const noSections: NavSection[] = [];

/**
 * The frame of every screen that is not a sign-in form (plan 04, W1; plan 05, R2). Signed in: a
 * top bar (menu button on phones, brand, language, account) over the living sidebar and the page.
 * Signed out: the same bar with the public links and no sidebar, so public pages look like the
 * same product. The menu is the RBAC catalogue's shape, filtered by what the user may view (W2);
 * hiding a link is UX only.
 */
export function AppShell({ width, children }: { width: keyof typeof shellWidths; children: ReactNode }) {
  const t = useTranslations("Shell");
  const state = useSession();
  const tShell = useTranslations("Shell");
  const { can, isLoading } = usePermissions();
  const pathname = usePathname();
  const signedIn = state.status === "authenticated";
  const fresh = useMemo(() => (signedIn ? visibleNav(can) : []), [signedIn, can]);

  // While permissions load every gate is closed, so the fresh menu holds only the public links.
  // After a switch, keep showing the last menu (marked busy) instead of a sidebar that blinks out
  // (P1); at the start of a session there is none to keep, and no menu beats half of one.
  const [kept, setKept] = useState<NavSection[]>(fresh);
  if (!isLoading && kept !== fresh) setKept(fresh);
  const busy = signedIn && isLoading && kept.length > 0;
  const sections = busy ? kept : signedIn && isLoading ? noSections : fresh;

  const contextKey = state.status === "authenticated" ? `in:${state.activeInstitution ?? ""}` : state.status;
  const data = useShellData({
    enabled: signedIn,
    withTable: signedIn && state.activeInstitution !== null && can("BOOKINGS.CALENDAR.VIEW"),
    contextKey,
  });

  const [jumping, setJumping] = useState(false);
  const targets = useMemo(
    () => jumpTargets(sections, (kind, key) => tShell(`${kind}.${key}` as Parameters<typeof tShell>[0])),
    [sections, tShell],
  );

  useEffect(() => {
    if (!signedIn) return;
    const onKey = (event: KeyboardEvent) => {
      if ((event.ctrlKey || event.metaKey) && !event.altKey && event.key.toLowerCase() === "k") {
        event.preventDefault();
        setJumping(true);
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [signedIn]);

  return (
    <div className="flex min-h-dvh flex-col">
      <a
        href="#main"
        className={cn(
          // Above the page and out of sight until focused; never shrunk to 1 px like sr-only, so it
          // appears whole when a keyboard user lands on it.
          "fixed top-3 left-3 z-50 -translate-y-24 rounded-full bg-primary px-5 py-3 font-medium text-on-primary focus:translate-y-0",
          focusRing,
        )}
      >
        {t("skipToContent")}
      </a>

      <header className="sticky top-0 z-30 border-b border-hairline bg-canvas">
        <div className="flex min-h-16 items-center gap-2 px-4 sm:gap-3 lg:px-6">
          {signedIn && (
            <MobileNav sections={sections} pathname={pathname} data={data} busy={busy} onJump={() => setJumping(true)} />
          )}
          <Link
            href="/"
            aria-label={t("home")}
            className={cn("inline-flex min-h-11 shrink-0 items-center gap-2 rounded-full pr-2", focusRing)}
          >
            <BrandMark className="size-9" />
            <span className="hidden text-xl font-semibold tracking-tight text-brand sm:inline">
              Sethika
            </span>
          </Link>

          <div className="ml-auto flex items-center gap-1 sm:gap-2">
            <LanguageMenu />
            {signedIn && <UserMenu />}
            {(state.status === "anonymous" || state.status === "unavailable") && <PublicLinks />}
          </div>
        </div>
        <SupportBanner />
      </header>

      <div className="flex flex-1 items-start">
        {signedIn && (
          <Sidebar sections={sections} pathname={pathname} data={data} busy={busy} onJump={() => setJumping(true)} />
        )}
        <div className="flex min-w-0 flex-1 flex-col self-stretch">
          <main id="main" tabIndex={-1} className="flex-1 px-4 py-6 focus:outline-none sm:px-6 sm:py-8 lg:px-10">
            <div className={cn("w-full", shellWidths[width])}>{children}</div>
          </main>
          {!signedIn && (
            <footer className="w-full px-4 pb-6 sm:px-6 lg:px-10">
              <ThemeSwitcher />
            </footer>
          )}
        </div>
      </div>

      {signedIn && <JumpPalette open={jumping} onOpenChange={setJumping} targets={targets} />}
    </div>
  );
}

/** Signed out: finding a place is open to everyone; signing in and up are the way further in. */
function PublicLinks() {
  const t = useTranslations("Shell.public");

  return (
    <>
      <Button asChild variant="ghost" className="hidden lg:inline-flex">
        <Link href="/institutions">{t("find")}</Link>
      </Button>
      <Button asChild variant="outline">
        <Link href="/login">{t("signIn")}</Link>
      </Button>
      <Button asChild className="hidden md:inline-flex">
        <Link href="/signup">{t("signUp")}</Link>
      </Button>
    </>
  );
}
