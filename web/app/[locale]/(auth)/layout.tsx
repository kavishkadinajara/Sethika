import { useTranslations } from "next-intl";
import { BrandMark } from "@/components/auth/BrandMark";
import { LanguageSwitcher } from "@/components/ui/language-switcher";
import { ThemeSwitcher } from "@/components/ui/theme-switcher";
import { Link } from "@/i18n/navigation";
import { usePageLocale } from "@/i18n/static-locale";
import { focusRing } from "@/lib/utils";

/**
 * The frame of every sign-in screen. Phones first: on a small screen the form is the page,
 * with no card around it; from `sm` up it sits in a bowl-shaped card. The language switcher is
 * at the top, because a donor who cannot read the first screen cannot find it anywhere else.
 */
export default function AuthLayout({ children, params }: LayoutProps<"/[locale]">) {
  usePageLocale(params);
  const t = useTranslations();

  return (
    <div className="flex min-h-dvh flex-col">
      <header className="mx-auto flex w-full max-w-md flex-wrap items-center justify-between gap-3 px-4 pt-4 sm:pt-8">
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

      <main className="mx-auto w-full max-w-md flex-1 px-4 py-6 sm:py-10">
        <div className="sm:rounded-bowl sm:border sm:border-hairline sm:bg-surface sm:p-8 sm:shadow-lift">
          {children}
        </div>
      </main>

      <footer className="mx-auto w-full max-w-md px-4 pb-6">
        <ThemeSwitcher />
      </footer>
    </div>
  );
}
