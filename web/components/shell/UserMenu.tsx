"use client";

import { useTranslations } from "next-intl";
import { DropdownMenu } from "radix-ui";
import { useSyncExternalStore, type ReactNode } from "react";
import {
  applyThemePreference,
  readThemePreference,
  subscribeTheme,
  themePreferences,
  type ThemePreference,
} from "@/components/theme/theme";
import {
  AutoThemeIcon,
  CheckIcon,
  ChevronDownIcon,
  LockIcon,
  LogOutIcon,
  MoonIcon,
  SunIcon,
  SwapIcon,
} from "@/components/ui/icons";
import { Link, useRouter } from "@/i18n/navigation";
import { session } from "@/lib/auth";
import { useSession } from "@/lib/auth/useSession";
import { cn, focusRing } from "@/lib/utils";
import { menuContent, menuItem, menuSeparator } from "./menu-styles";
import { initials, useProfile } from "./useProfile";

const themeIcons: Record<ThemePreference, ReactNode> = {
  system: <AutoThemeIcon className="size-5 shrink-0 text-muted" />,
  light: <SunIcon className="size-5 shrink-0 text-muted" />,
  dark: <MoonIcon className="size-5 shrink-0 text-muted" />,
};

/**
 * The account menu: the person's name, their password, switching institution, the theme, and
 * signing out (here or everywhere). Signing out lands on the home page, which is the public one
 * once the session has gone.
 */
export function UserMenu() {
  const t = useTranslations("Shell");
  const tTheme = useTranslations("ThemeSwitcher");
  const state = useSession();
  const profile = useProfile();
  const router = useRouter();
  const preference = useSyncExternalStore(subscribeTheme, readThemePreference, () => "system");

  if (state.status !== "authenticated") return null;

  const name = profile?.displayName || profile?.fullName || "";

  async function signOut(everywhere: boolean) {
    await (everywhere ? session.logoutAll() : session.logout());
    router.replace("/");
  }

  return (
    <DropdownMenu.Root>
      <DropdownMenu.Trigger
        aria-label={t("account.menu")}
        className={cn(
          "inline-flex min-h-11 items-center gap-2 rounded-full p-1 pr-2 text-fg transition-colors",
          "hover:bg-subtle data-[state=open]:bg-subtle",
          focusRing,
        )}
      >
        <span
          aria-hidden
          className="grid size-9 shrink-0 place-items-center rounded-full bg-estate text-sm font-semibold text-ivory ring-2 ring-accent/70"
        >
          {initials(name) || "·"}
        </span>
        <span className="hidden max-w-44 truncate text-base font-medium md:inline">{name}</span>
        <ChevronDownIcon className="size-4 shrink-0 text-muted" />
      </DropdownMenu.Trigger>
      <DropdownMenu.Portal>
        <DropdownMenu.Content align="end" sideOffset={8} collisionPadding={16} className={cn(menuContent, "w-72")}>
          {name && (
            <DropdownMenu.Label className="px-3 pt-2 pb-3">
              <span className="block text-sm text-muted">{t("account.menu")}</span>
              <span className="block truncate text-base font-semibold text-fg">{name}</span>
            </DropdownMenu.Label>
          )}

          <DropdownMenu.Item asChild className={menuItem}>
            <Link href="/account/password">
              <LockIcon className="size-5 shrink-0 text-muted" />
              {t("account.changePassword")}
            </Link>
          </DropdownMenu.Item>
          {state.institutions.length > 1 && (
            <DropdownMenu.Item asChild className={menuItem}>
              <Link href="/select-institution">
                <SwapIcon className="size-5 shrink-0 text-muted" />
                {t("context.switch")}
              </Link>
            </DropdownMenu.Item>
          )}

          <DropdownMenu.Separator className={menuSeparator} />
          <DropdownMenu.Label className="px-3 pt-1 pb-1 text-sm font-semibold text-muted">{t("account.theme")}</DropdownMenu.Label>
          <DropdownMenu.RadioGroup value={preference} onValueChange={(value) => applyThemePreference(value as ThemePreference)}>
            {themePreferences.map((option) => (
              <DropdownMenu.RadioItem key={option} value={option} className={menuItem} onSelect={(event) => event.preventDefault()}>
                {themeIcons[option]}
                <span className="flex-1">{tTheme(option)}</span>
                <DropdownMenu.ItemIndicator>
                  <CheckIcon className="size-5 text-brand" />
                </DropdownMenu.ItemIndicator>
              </DropdownMenu.RadioItem>
            ))}
          </DropdownMenu.RadioGroup>

          <DropdownMenu.Separator className={menuSeparator} />
          <DropdownMenu.Item className={menuItem} onSelect={() => void signOut(false)}>
            <LogOutIcon className="size-5 shrink-0 text-muted" />
            {t("account.signOut")}
          </DropdownMenu.Item>
          <DropdownMenu.Item className={cn(menuItem, "text-danger data-[highlighted]:text-danger")} onSelect={() => void signOut(true)}>
            <LogOutIcon className="size-5 shrink-0" />
            {t("account.signOutEverywhere")}
          </DropdownMenu.Item>
        </DropdownMenu.Content>
      </DropdownMenu.Portal>
    </DropdownMenu.Root>
  );
}
