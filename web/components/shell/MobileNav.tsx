"use client";

import { useTranslations } from "next-intl";
import { useState } from "react";
import { BrandMark } from "@/components/auth/BrandMark";
import { MenuIcon } from "@/components/ui/icons";
import { Sheet, SheetContent, SheetTitle, SheetTrigger } from "@/components/ui/sheet";
import { Button } from "@/components/ui/button";
import { Link } from "@/i18n/navigation";
import { isActive, type NavSection } from "@/lib/shell/nav";
import { cn, focusRing } from "@/lib/utils";
import { ContextChip } from "./ContextChip";
import { navIcons } from "./nav-icons";
import { NavGroups } from "./NavGroups";

/**
 * Below `lg`: the menu button in the top bar opens the whole tree in a sheet from the left, every
 * section with its modules, so a phone reaches the same screens as the rail. Choosing a link
 * closes it.
 */
export function MobileNav({ sections, pathname }: { sections: NavSection[]; pathname: string }) {
  const t = useTranslations("Shell");
  const [open, setOpen] = useState(false);
  const close = () => setOpen(false);

  return (
    <Sheet open={open} onOpenChange={setOpen}>
      <SheetTrigger asChild>
        <Button variant="ghost" size="icon" className="-ml-2 text-fg lg:hidden" aria-label={t("openMenu")}>
          <MenuIcon className="size-6" />
        </Button>
      </SheetTrigger>
      <SheetContent side="left" className="max-w-sm" aria-describedby={undefined}>
        <div className="flex items-center gap-2 px-5 pt-5 pr-16">
          <BrandMark className="size-9" />
          <SheetTitle className="text-xl">{t("mainMenu")}</SheetTitle>
        </div>
        <div className="px-5 pt-4">
          <ContextChip onSwitched={close} />
        </div>
        <nav aria-label={t("mainMenu")} className="grid gap-6 px-4 pt-5 pb-8">
          {sections.map((section) => {
            const Icon = navIcons[section.icon];
            const label = t(`sections.${section.key}`);

            if (section.href !== undefined) {
              const here = isActive(section.href, pathname);
              return (
                <Link
                  key={section.key}
                  href={section.href}
                  onClick={close}
                  aria-current={here ? "page" : undefined}
                  className={cn(
                    "flex min-h-12 items-center gap-3 rounded-2xl px-4 py-2 text-base font-semibold",
                    focusRing,
                    here ? "bg-estate text-ivory" : "bg-subtle text-fg hover:text-brand",
                  )}
                >
                  <Icon className="size-5.5 shrink-0" />
                  {label}
                </Link>
              );
            }

            return (
              <section key={section.key} aria-label={label} className="grid gap-2">
                <p className="flex items-center gap-2 px-2 text-sm font-semibold tracking-wide text-muted uppercase">
                  <Icon className="size-4.5 shrink-0" />
                  {label}
                </p>
                <NavGroups section={section} pathname={pathname} onNavigate={close} />
              </section>
            );
          })}
        </nav>
      </SheetContent>
    </Sheet>
  );
}
