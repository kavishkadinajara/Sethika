"use client";

import { useTranslations } from "next-intl";
import { useState } from "react";
import { BrandMark } from "@/components/auth/BrandMark";
import { MenuIcon } from "@/components/ui/icons";
import { Sheet, SheetContent, SheetTitle, SheetTrigger } from "@/components/ui/sheet";
import { Button } from "@/components/ui/button";
import type { NavSection } from "@/lib/shell/nav";
import { SidebarBody } from "./Sidebar";
import type { ShellData } from "./useShellData";

/**
 * Below `lg`: the menu button in the top bar opens the sidebar's body in a sheet from the left
 * (plan 05, R2), so a phone reaches the same screens, counts and today's table. Choosing a link,
 * switching context or opening jump-to closes it.
 */
export function MobileNav({
  sections,
  pathname,
  data,
  busy,
  onJump,
}: {
  sections: NavSection[];
  pathname: string;
  data: ShellData;
  busy: boolean;
  onJump: () => void;
}) {
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
        <div className="px-4 pt-4 pb-8">
          <SidebarBody
            sections={sections}
            pathname={pathname}
            data={data}
            busy={busy}
            onNavigate={close}
            onJump={() => {
              close();
              onJump();
            }}
          />
        </div>
      </SheetContent>
    </Sheet>
  );
}
