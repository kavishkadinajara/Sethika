"use client";

import { useTranslations } from "next-intl";
import { useState } from "react";
import { BrandMark } from "@/components/auth/BrandMark";
import { MenuIcon } from "@/components/ui/icons";
import { Sheet, SheetContent, SheetTitle, SheetTrigger } from "@/components/ui/sheet";
import { Button } from "@/components/ui/button";
import type { NavSection } from "@/lib/shell/nav";
import { ContextChip } from "./ContextChip";
import { SidebarNav } from "./SidebarNav";

/**
 * Below `lg`: the menu button in the top bar opens the sidebar's list in a sheet from the left,
 * so a phone reaches the same screens, with the same glide pill. Choosing a link closes it.
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
        <SidebarNav id="sheet" sections={sections} pathname={pathname} onNavigate={close} className="px-4 pt-5 pb-8" />
      </SheetContent>
    </Sheet>
  );
}
