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
import { initials } from "./useProfile";
import { menuContent, menuItem, menuSeparator } from "./menu-styles";

/**
 * Who the session acts as, at the top of the sidebar (plan 05, R2; plan 04, W3): the institution's
 * initials in a bowl-shaped tile with the gold rim, and its name (or the platform team, or a donor). For
 * anyone who belongs to an institution it is also the switch: between their institutions, and back
 * to their own account (P1). Switching rotates the session and lands on the dashboard, because
 * the page the user was on may not exist in the other context. Folded, only the tile shows.
 */
export function ContextCard({ folded = false, onSwitched }: { folded?: boolean; onSwitched?: () => void }) {
  const t = useTranslations("Shell");
  const tErrors = useTranslations("Errors");
  const state = useSession();
  const { isPlatform } = usePermissions();
  const router = useRouter();
  const toast = useToast();
  const [switching, setSwitching] = useState<string | null>(null);

  if (state.status !== "authenticated") return null;

  const active = state.institutions.find((i) => i.slug === state.activeInstitution) ?? null;
  const Icon = isPlatform ? ShieldIcon : GivingIcon;
  const name = active ? active.name : isPlatform ? t("context.platform") : t("context.donor");

  const tile = (
    <span
      aria-hidden
      className={cn(
        "grid size-11 shrink-0 place-items-center rounded-[0.6rem_0.6rem_1.35rem_1.35rem] text-base font-semibold",
        "bg-primary text-on-primary ring-2 ring-accent ring-offset-2 ring-offset-surface",
      )}
    >
      {active ? initials(active.name) : <Icon className="size-5" />}
    </span>
  );
  const text = !folded && (
    <span className="grid min-w-0 flex-1">
      <span className="text-xs leading-tight text-muted">{t("context.label")}</span>
      <span className="truncate text-base leading-snug font-semibold text-fg">{name}</span>
    </span>
  );
  const card = cn(
    "flex w-full items-center gap-3 rounded-2xl text-left",
    folded ? "justify-center p-1" : "border border-hairline bg-canvas p-2.5 pr-3",
  );

  if (state.institutions.length === 0) {
    return (
      <div className={card} aria-label={folded ? `${t("context.label")}: ${name}` : undefined} role={folded ? "img" : undefined}>
        {tile}
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
      <DropdownMenu.Trigger
        className={cn(card, "transition-colors hover:border-line", focusRing)}
        aria-label={`${t("context.switch")}: ${name}`}
      >
        {tile}
        {text}
        {!folded &&
          (switching !== null ? <Spinner size="sm" decorative /> : <ChevronDownIcon className="size-4 shrink-0 text-muted" />)}
      </DropdownMenu.Trigger>
      <DropdownMenu.Portal>
        <DropdownMenu.Content
          side={folded ? "right" : "bottom"}
          align="start"
          sideOffset={8}
          collisionPadding={16}
          className={menuContent}
        >
          <DropdownMenu.Label className="px-3 pt-1 pb-2 text-sm font-semibold text-muted">{t("context.switch")}</DropdownMenu.Label>
          <DropdownMenu.Item
            disabled={switching !== null}
            onSelect={() => void choose(null)}
            className={cn(menuItem, active === null && "font-semibold text-brand")}
          >
            <Icon className="size-5 shrink-0 text-muted" />
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
