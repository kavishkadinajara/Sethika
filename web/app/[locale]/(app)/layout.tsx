import { useTranslations } from "next-intl";
import { BrandMark } from "@/components/auth/BrandMark";
import { LanguageSwitcher } from "@/components/ui/language-switcher";
import { ThemeSwitcher } from "@/components/ui/theme-switcher";
import { Link } from "@/i18n/navigation";
import { usePageLocale } from "@/i18n/static-locale";
import { focusRing } from "@/lib/utils";

/**
 * The frame of the signed-in screens that are more than a sign-in form: wider than the auth
 * frame, for forms with many fields, and still a single column on a phone.
 */
export default function AppLayout({ children, params }: LayoutProps<"/[locale]">) {
  usePageLocale(params);
  const t = useTranslations();

  return (
    <div className="flex min-h-dvh flex-col">
      <header className="mx-auto flex w-full max-w-2xl flex-wrap items-center justify-between gap-3 px-4 pt-4 sm:pt-8">
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

      <main className="mx-auto w-full max-w-2xl flex-1 px-4 py-6 sm:py-10">{children}</main>

      <footer className="mx-auto w-full max-w-2xl px-4 pb-6">
        <ThemeSwitcher />
      </footer>
    </div>
  );
}
