"use client";

import { useLocale, useTranslations } from "next-intl";
import { DropdownMenu } from "radix-ui";
import { CheckIcon, GlobeIcon } from "@/components/ui/icons";
import { Link, usePathname } from "@/i18n/navigation";
import { routing } from "@/i18n/routing";
import { savePreferredLanguage } from "@/lib/auth/api";
import { cn, focusRing } from "@/lib/utils";
import { menuContent, menuItem } from "./menu-styles";

/**
 * The language switcher folded into one top-bar control. It works like LanguageSwitcher: each
 * choice is a link to the same page under the other locale (next-intl remembers it in its
 * cookie) and, when signed in, is saved to the account beside the navigation, never before it.
 * The trigger's name says what it does in the current language and shows the current language
 * in its own script, so someone who cannot read this one still recognises theirs.
 */
export function LanguageMenu() {
  const t = useTranslations("LanguageSwitcher");
  const current = useLocale();
  const pathname = usePathname();

  return (
    <DropdownMenu.Root>
      <DropdownMenu.Trigger
        aria-label={t("label")}
        className={cn(
          "inline-flex min-h-11 min-w-11 items-center justify-center gap-2 rounded-full px-3 text-base font-medium text-fg",
          "transition-colors hover:bg-subtle hover:text-brand data-[state=open]:bg-subtle",
          focusRing,
        )}
      >
        <GlobeIcon className="size-5 shrink-0" />
        <span lang={current} className="hidden sm:inline">
          {t(`names.${current}`)}
        </span>
      </DropdownMenu.Trigger>
      <DropdownMenu.Portal>
        <DropdownMenu.Content align="end" sideOffset={8} collisionPadding={16} className={cn(menuContent, "min-w-48")}>
          <DropdownMenu.Label className="px-3 pt-1 pb-2 text-sm font-semibold text-muted">{t("label")}</DropdownMenu.Label>
          {routing.locales.map((locale) => {
            const active = locale === current;
            return (
              <DropdownMenu.Item key={locale} asChild className={cn(menuItem, active && "font-semibold text-brand")}>
                <Link
                  href={pathname}
                  locale={locale}
                  lang={locale}
                  hrefLang={locale}
                  aria-current={active ? "true" : undefined}
                  onClick={active ? undefined : () => void savePreferredLanguage(locale)}
                  // Another script's name in the platform font, so this menu never pulls a Noto file.
                  className={cn(locale !== current && locale !== routing.defaultLocale && "font-system")}
                >
                  <span className="flex-1">{t(`names.${locale}`)}</span>
                  {active && <CheckIcon className="size-5 shrink-0" />}
                </Link>
              </DropdownMenu.Item>
            );
          })}
        </DropdownMenu.Content>
      </DropdownMenu.Portal>
    </DropdownMenu.Root>
  );
}
