"use client";

import { useTranslations } from "next-intl";
import { Link } from "@/i18n/navigation";
import { activeHref, type NavSection } from "@/lib/shell/nav";
import { cn, focusRing } from "@/lib/utils";
import { navIcons } from "./nav-icons";
import { useGlide } from "./useGlide";

/**
 * The menu as a labelled list (docs/DESIGN.md, the app shell): the dashboard, then each section's
 * screens under its name. The page the user is on sits in the glide pill, which moves to the new
 * page with a stretch (useGlide). The same list is the sidebar on wide screens and the menu sheet
 * on phones. Hiding a link is UX only: every endpoint keeps its own [HasPermission].
 */
export function SidebarNav({
  sections,
  pathname,
  onNavigate,
  className,
  id,
}: {
  sections: NavSection[];
  pathname: string;
  onNavigate?: () => void;
  className?: string;
  /** Which menu this is ("sidebar", "sheet"): each remembers its own pill between pages. */
  id: string;
}) {
  const t = useTranslations("Shell");
  const active = activeHref(sections, pathname);
  const track = useGlide<HTMLDivElement>(id, active);

  return (
    <nav aria-label={t("mainMenu")} className={className}>
      <div ref={track} className="glide relative">
        <Entries sections={sections} active={active} onNavigate={onNavigate} />
        {/* The pill: the same list in the active colours, clipped to the band between the
            glide's two edges. Decoration only; the links underneath are the real menu. */}
        <div aria-hidden className="glide-pill-y pointer-events-none absolute inset-0 bg-primary">
          <Entries sections={sections} active={active} pill />
          <span className="glide-rim-y absolute left-0 w-[3px] rounded-r-full bg-accent" />
        </div>
      </div>
    </nav>
  );
}

function Entries({
  sections,
  active,
  pill = false,
  onNavigate,
}: {
  sections: NavSection[];
  active: string | null;
  pill?: boolean;
  onNavigate?: () => void;
}) {
  const t = useTranslations("Shell");

  return (
    <div className="grid gap-5">
      {sections.map((section) => {
        if (section.href !== undefined) {
          return (
            <Entry
              key={section.key}
              href={section.href}
              icon={navIcons[section.icon]}
              label={t(`sections.${section.key}`)}
              here={active === section.href}
              pill={pill}
              onNavigate={onNavigate}
            />
          );
        }
        const label = t(`sections.${section.key}`);
        return (
          <section key={section.key} aria-label={pill ? undefined : label} className="grid gap-1">
            <p
              className={cn(
                "px-3 pb-1 text-xs font-semibold tracking-[0.12em] uppercase",
                pill ? "text-transparent" : "text-muted",
              )}
            >
              {label}
            </p>
            <ul className="grid gap-1">
              {section.groups.flatMap((group) =>
                group.items.map((item) => (
                  <li key={item.key}>
                    <Entry
                      href={item.href}
                      icon={navIcons[item.icon]}
                      label={t(`items.${item.key}`)}
                      here={active === item.href}
                      pill={pill}
                      onNavigate={onNavigate}
                    />
                  </li>
                )),
              )}
            </ul>
          </section>
        );
      })}
    </div>
  );
}

function Entry({
  href,
  icon: Icon,
  label,
  here,
  pill,
  onNavigate,
}: {
  href: string;
  icon: (typeof navIcons)[keyof typeof navIcons];
  label: string;
  here: boolean;
  pill: boolean;
  onNavigate?: () => void;
}) {
  // Both layers share every size, weight and gap, so a label wraps the same way in each and the
  // pill's copy lies exactly over the link.
  const shape = cn(
    "flex min-h-11 items-center gap-3 rounded-2xl px-3 py-2 text-base leading-snug",
    here && "font-semibold",
  );

  if (pill) {
    return (
      <span className={cn(shape, "text-on-primary")}>
        <Icon className="size-5 shrink-0" />
        <span>{label}</span>
      </span>
    );
  }

  return (
    <Link
      href={href}
      onClick={onNavigate}
      aria-current={here ? "page" : undefined}
      data-glide-active={here ? "" : undefined}
      className={cn(shape, "transition-colors", focusRing, here ? "text-brand" : "text-fg hover:bg-subtle hover:text-brand")}
    >
      <Icon className={cn("size-5 shrink-0", here ? "text-brand" : "text-muted")} />
      <span>{label}</span>
    </Link>
  );
}
