import { useTranslations } from "next-intl";
import type { ReactNode } from "react";
import { BrandMark } from "@/components/auth/BrandMark";
import { LanguageSwitcher } from "@/components/ui/language-switcher";
import { ThemeSwitcher } from "@/components/ui/theme-switcher";
import { Link } from "@/i18n/navigation";
import { cn, focusRing } from "@/lib/utils";

const widths = {
  /** Forms with many fields: one readable column. */
  narrow: "max-w-2xl",
  /** A month calendar beside a day's details. */
  wide: "max-w-6xl",
} as const;

/**
 * The frame of the signed-in screens that are more than a sign-in form, and of the public booking
 * pages: the brand home link, the language, the theme. Still a single column on a phone.
 */
export function AppFrame({ width, children }: { width: keyof typeof widths; children: ReactNode }) {
  const t = useTranslations();
  const max = widths[width];

  return (
    <div className="flex min-h-dvh flex-col">
      <header className={cn("mx-auto flex w-full flex-wrap items-center justify-between gap-3 px-4 pt-4 sm:pt-8", max)}>
        <Link
          href="/"
          className={`inline-flex min-h-11 items-center gap-2 rounded-full pr-2 ${focusRing}`}
          aria-label={t("AuthShell.home")}
        >
          <BrandMark />
          <span className="text-xl font-semibold tracking-tight text-brand">{t("Brand.name")}</span>
        </Link>
        <LanguageSwitcher />
      </header>

      <main className={cn("mx-auto w-full flex-1 px-4 py-6 sm:py-10", max)}>{children}</main>

      <footer className={cn("mx-auto w-full px-4 pb-6", max)}>
        <ThemeSwitcher />
      </footer>
    </div>
  );
}
