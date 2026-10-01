"use client";

import { useLocale, useTranslations } from "next-intl";
import { Link, usePathname } from "@/i18n/navigation";
import { routing } from "@/i18n/routing";
import { cn, focusRing } from "@/lib/utils";

/**
 * en / සිංහල / தமிழ், each a link to the same page under the other locale prefix.
 * next-intl's Link updates the NEXT_LOCALE cookie on click, so the choice sticks for
 * unprefixed URLs too. Each name is written in its own script and tagged with its
 * language, so a screen reader pronounces it correctly.
 */
export function LanguageSwitcher({ className }: { className?: string }) {
  const t = useTranslations("LanguageSwitcher");
  const current = useLocale();
  const pathname = usePathname();

  return (
    <nav aria-label={t("label")} className={className}>
      <ul className="inline-flex max-w-full flex-wrap gap-1 rounded-full bg-subtle p-1">
        {routing.locales.map((locale) => {
          const active = locale === current;
          return (
            <li key={locale}>
              <Link
                href={pathname}
                locale={locale}
                lang={locale}
                hrefLang={locale}
                aria-current={active ? "true" : undefined}
                className={cn(
                  "inline-flex min-h-11 min-w-11 items-center justify-center rounded-full px-4 py-1.5",
                  "text-base leading-snug font-medium transition-colors",
                  focusRing,
                  // Another script's name is drawn in the platform font, so this switcher never
                  // pulls a 100 KB+ Noto file onto a page that does not otherwise need it.
                  locale !== current && locale !== routing.defaultLocale && "font-system",
                  active
                    ? "bg-primary text-on-primary shadow-[0_1px_2px_rgb(14_42_33/0.3)]"
                    : "text-fg hover:bg-surface hover:text-brand",
                )}
              >
                {t(`names.${locale}`)}
              </Link>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}
