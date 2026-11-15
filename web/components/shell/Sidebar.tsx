"use client";

import { useTranslations } from "next-intl";
import { BrandMark } from "@/components/auth/BrandMark";
import { Link } from "@/i18n/navigation";
import type { NavSection } from "@/lib/shell/nav";
import { cn, focusRing } from "@/lib/utils";
import { SidebarNav } from "./SidebarNav";

/**
 * The wide-screen menu (docs/DESIGN.md, the app shell): a canvas panel the height of the window,
 * the brand at the top and the labelled menu under it. Below `lg` it is hidden and MobileNav shows
 * the same list in a sheet.
 */
export function Sidebar({ sections, pathname }: { sections: NavSection[]; pathname: string }) {
  const t = useTranslations("Shell");

  return (
    <aside className="sticky top-0 hidden h-dvh w-72 shrink-0 p-3 pr-0 lg:block">
      <div className="flex h-full flex-col rounded-[1.75rem] border border-hairline bg-canvas shadow-lift">
        <Link
          href="/"
          aria-label={t("home")}
          className={cn("m-3 flex min-h-12 items-center gap-3 rounded-2xl px-2 py-1.5", focusRing)}
        >
          <BrandMark className="size-10" />
          <span className="text-xl font-semibold tracking-tight text-brand">Sethika</span>
        </Link>
        <SidebarNav
          id="sidebar"
          sections={sections}
          pathname={pathname}
          className="min-h-0 flex-1 overflow-y-auto overscroll-contain px-3 pt-2 pb-4"
        />
      </div>
    </aside>
  );
}
