"use client";

import { useTranslations } from "next-intl";
import { DropdownMenu } from "radix-ui";
import { useState } from "react";
import { useToast } from "@/components/ui/toast";
import { CheckIcon, ChevronDownIcon, GivingIcon, InstitutionIcon, ShieldIcon } from "@/components/ui/icons";
import { Spinner } from "@/components/ui/spinner";
import { useRouter } from "@/i18n/navigation";
import { session } from "@/lib/auth";
import { useSession } from "@/lib/auth/useSession";
import { usePermissions } from "@/lib/permissions/usePermissions";
import { cn, focusRing } from "@/lib/utils";
import { menuContent, menuItem, menuSeparator } from "./menu-styles";

/**
 * Who the session is acting as (plan 04, W3): the institution's name, or the platform team, or a
 * donor. For anyone who belongs to an institution it is also the switch: between their
 * institutions, and back to their own account (P1), where a donor's screens hold. Switching
 * rotates the session (as /select-institution does) and lands on the dashboard, because the page
 * the user was on may not exist in the other context.
 */
export function ContextChip({ className, onSwitched }: { className?: string; onSwitched?: () => void }) {
  const t = useTranslations("Shell");
  const tErrors = useTranslations("Errors");
  const state = useSession();
  const { isPlatform } = usePermissions();
  const router = useRouter();
  const toast = useToast();
  const [switching, setSwitching] = useState<string | null>(null);

  if (state.status !== "authenticated") return null;

  const active = state.institutions.find((i) => i.slug === state.activeInstitution) ?? null;
  const Icon = active ? InstitutionIcon : isPlatform ? ShieldIcon : GivingIcon;
  const name = active ? active.name : isPlatform ? t("context.platform") : t("context.donor");

  const chip = cn(
    "inline-flex min-h-11 max-w-full items-center gap-2 rounded-full border border-hairline bg-surface py-1.5 pr-3 pl-1.5",
    "text-left text-base leading-snug text-fg",
  );
  const badge = (
    <span className="grid size-8 shrink-0 place-items-center rounded-full bg-primary/12 text-brand">
      <Icon className="size-4.5" />
    </span>
  );
  const text = (
    <span className="grid min-w-0">
      <span className="text-xs leading-tight text-muted">{t("context.label")}</span>
      <span className="truncate font-semibold">{name}</span>
    </span>
  );

  if (state.institutions.length === 0) {
    return (
      <div className={cn(chip, className)}>
        {badge}
        {text}
      </div>
    );
  }

  /** An institution's slug, or null for the person's own account. */
  async function choose(slug: string | null) {
    if (state.status !== "authenticated" || slug === state.activeInstitution) return;
    setSwitching(slug ?? "");
    const result = slug === null ? await session.switchToPersonal() : await session.switchInstitution(slug);
    setSwitching(null);
    if (result.ok) {
      onSwitched?.();
      router.push("/");
      return;
    }
    if (result.reason === "signed_out") {
      router.replace("/login");
      return;
    }
    toast({
      variant: "error",
      title: result.reason === "not_available" ? tErrors("institution_not_available") : tErrors("network"),
    });
  }

  return (
    <DropdownMenu.Root>
      <DropdownMenu.Trigger className={cn(chip, "hover:border-line", focusRing, className)} aria-label={`${t("context.switch")}: ${name}`}>
        {badge}
        {text}
        {switching ? <Spinner size="sm" decorative /> : <ChevronDownIcon className="size-4 shrink-0 text-muted" />}
      </DropdownMenu.Trigger>
      <DropdownMenu.Portal>
        <DropdownMenu.Content align="start" sideOffset={8} collisionPadding={16} className={menuContent}>
          <DropdownMenu.Label className="px-3 pt-1 pb-2 text-sm font-semibold text-muted">{t("context.switch")}</DropdownMenu.Label>
          <DropdownMenu.Item
            disabled={switching !== null}
            onSelect={() => void choose(null)}
            className={cn(menuItem, active === null && "font-semibold text-brand")}
          >
            {isPlatform ? <ShieldIcon className="size-5 shrink-0 text-muted" /> : <GivingIcon className="size-5 shrink-0 text-muted" />}
            <span className="min-w-0 flex-1">{t("context.personal")}</span>
            {active === null && <CheckIcon className="size-5 shrink-0 text-brand" />}
          </DropdownMenu.Item>
          <DropdownMenu.Separator className={menuSeparator} />
          {state.institutions.map((institution) => {
            const current = institution.slug === state.activeInstitution;
            return (
              <DropdownMenu.Item
                key={institution.slug}
                disabled={switching !== null}
                onSelect={() => void choose(institution.slug)}
                className={cn(menuItem, current && "font-semibold text-brand")}
              >
                <InstitutionIcon className="size-5 shrink-0 text-muted" />
                <span className="min-w-0 flex-1">{institution.name}</span>
                {current && <CheckIcon className="size-5 shrink-0 text-brand" />}
              </DropdownMenu.Item>
            );
          })}
        </DropdownMenu.Content>
      </DropdownMenu.Portal>
    </DropdownMenu.Root>
  );
}
