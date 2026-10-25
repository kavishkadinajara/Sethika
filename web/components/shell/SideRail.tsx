"use client";

import { useTranslations } from "next-intl";
import { Popover } from "radix-ui";
import { useState, type ComponentProps, type ReactNode } from "react";
import { Link } from "@/i18n/navigation";
import { activeSectionKey, type NavSection, type SectionKey } from "@/lib/shell/nav";
import { cn } from "@/lib/utils";
import { navIcons } from "./nav-icons";
import { NavGroups } from "./NavGroups";

/**
 * The desktop menu (plan 04, W1): a floating rail of section icons, shaped like the bowl, with a
 * flyout that opens beside it on the section's modules and screens. Radix Popover gives the
 * flyout its focus handling: it takes focus, Escape or a click outside closes it, and focus goes
 * back to the rail. Below `lg` the rail is hidden and MobileNav shows the same tree.
 */
export function SideRail({ sections, pathname }: { sections: NavSection[]; pathname: string }) {
  const t = useTranslations("Shell");
  // The flyout is open for one section on one page; moving to another page (a link in it, or
  // the browser's back button) closes it without an effect.
  const [open, setOpen] = useState<{ key: SectionKey; pathname: string } | null>(null);
  const openKey = open?.pathname === pathname ? open.key : null;
  const current = activeSectionKey(sections, pathname);

  return (
    <nav
      aria-label={t("mainMenu")}
      className={cn(
        "sticky top-20 ml-4 hidden h-[calc(100dvh-6rem)] w-[4.5rem] shrink-0 flex-col items-center gap-2 py-4 lg:flex",
        // A brand panel (DESIGN.md): Estate Green in both themes, a tonal gradient of its own green.
        "rounded-bowl bg-[linear-gradient(180deg,#1f7356_0%,var(--color-estate)_38%,#0f3d2e_100%)] shadow-lift",
      )}
    >
      <ul className="flex flex-col items-center gap-2">
        {sections.map((section) => {
          const Icon = navIcons[section.icon];
          const label = t(`sections.${section.key}`);
          const here = current === section.key;

          if (section.href !== undefined) {
            return (
              <li key={section.key}>
                <RailControl href={section.href} label={label} here={here}>
                  <Icon className="size-[1.375rem]" />
                </RailControl>
              </li>
            );
          }

          return (
            <li key={section.key}>
              <Popover.Root
                open={openKey === section.key}
                onOpenChange={(next) => setOpen(next ? { key: section.key, pathname } : null)}
              >
                <Popover.Trigger asChild>
                  <RailControl label={label} here={here}>
                    <Icon className="size-[1.375rem]" />
                  </RailControl>
                </Popover.Trigger>
                <Popover.Portal>
                  <Popover.Content
                    side="right"
                    align="start"
                    sideOffset={14}
                    collisionPadding={16}
                    aria-label={label}
                    className={cn(
                      "z-40 w-80 overflow-y-auto overscroll-contain rounded-bowl border border-hairline bg-surface p-3 text-fg shadow-lift",
                      "max-h-[var(--radix-popover-content-available-height)]",
                      "data-[state=open]:animate-flyout-in motion-reduce:animate-none focus-visible:outline-none",
                    )}
                  >
                    <p className="px-2 pt-1 pb-3 text-sm font-semibold tracking-wide text-muted uppercase">{label}</p>
                    <NavGroups section={section} pathname={pathname} onNavigate={() => setOpen(null)} />
                  </Popover.Content>
                </Popover.Portal>
              </Popover.Root>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}

type RailControlProps = Omit<ComponentProps<"button">, "children"> & {
  label: string;
  here: boolean;
  /** A section that is one page (the dashboard) is a link; the others open the flyout. */
  href?: string;
  children: ReactNode;
};

/**
 * One rail icon: a 48 px target with its name as the accessible name and as a tooltip on hover
 * or keyboard focus. The section the user is in carries the gold rim on the rail's edge. As the
 * flyout's trigger it is a button and takes Radix's props (aria-expanded, data-state, the ref).
 */
function RailControl({ label, here, href, children, className, ...buttonProps }: RailControlProps) {
  const classes = cn(
    "group relative grid size-12 place-items-center rounded-2xl text-ivory/75 transition-colors",
    "hover:bg-white/12 hover:text-ivory data-[state=open]:bg-white/16 data-[state=open]:text-ivory",
    // The offering glow, drawn in ivory and gold so it reads on the green.
    "focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ivory focus-visible:ring-6 focus-visible:ring-accent/50",
    here &&
      "bg-white/14 text-ivory before:absolute before:inset-y-3 before:-left-3 before:w-[3px] before:rounded-r-full before:bg-accent",
    className,
  );
  const tooltip = (
    <span
      aria-hidden
      className={cn(
        "pointer-events-none absolute top-1/2 left-full z-50 ml-4 -translate-y-1/2 rounded-full bg-ink px-3 py-1.5",
        "text-sm font-medium whitespace-nowrap text-ivory opacity-0 shadow-lift transition-opacity duration-150",
        "group-hover:opacity-100 group-focus-visible:opacity-100 group-data-[state=open]:hidden",
      )}
    >
      {label}
    </span>
  );

  if (href !== undefined) {
    return (
      <Link href={href} aria-label={label} aria-current={here ? "page" : undefined} className={classes}>
        {children}
        {tooltip}
      </Link>
    );
  }

  return (
    <button type="button" {...buttonProps} aria-label={label} className={classes}>
      {children}
      {tooltip}
    </button>
  );
}
