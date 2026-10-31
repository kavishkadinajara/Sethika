"use client";

import { useTranslations } from "next-intl";
import { Accordion } from "radix-ui";
import { ChevronDownIcon } from "@/components/ui/icons";
import { Link } from "@/i18n/navigation";
import { isActive, type NavSection } from "@/lib/shell/nav";
import { cn, focusRing } from "@/lib/utils";
import { navIcons } from "./nav-icons";

/**
 * One section's modules as an accordion, each opening onto its screens (plan 04, W1). Every group
 * starts open: there are few, and a closed group hides the link a person came for. The link to
 * the page they are on is marked with aria-current and the gold rim.
 */
export function NavGroups({
  section,
  pathname,
  onNavigate,
}: {
  section: NavSection;
  pathname: string;
  onNavigate?: () => void;
}) {
  const t = useTranslations("Shell");

  return (
    <Accordion.Root type="multiple" defaultValue={section.groups.map((group) => group.key)} className="grid gap-2">
      {section.groups.map((group) => (
        <Accordion.Item key={group.key} value={group.key} className="rounded-2xl border border-hairline bg-canvas/60">
          <Accordion.Header asChild>
            <h3>
              <Accordion.Trigger
                className={cn(
                  "group flex min-h-12 w-full items-center justify-between gap-3 rounded-2xl px-4 py-2 text-left",
                  "text-base font-semibold leading-snug text-fg transition-colors hover:bg-subtle",
                  focusRing,
                )}
              >
                {t(`groups.${group.key}`)}
                <ChevronDownIcon className="size-4.5 shrink-0 text-muted transition-transform duration-200 group-data-[state=open]:rotate-180 motion-reduce:transition-none" />
              </Accordion.Trigger>
            </h3>
          </Accordion.Header>
          <Accordion.Content className="px-2 pb-2">
            <ul className="grid gap-1">
              {group.items.map((item) => {
                const Icon = navIcons[item.icon];
                const active = isActive(item.href, pathname);
                return (
                  <li key={item.key}>
                    <Link
                      href={item.href}
                      onClick={onNavigate}
                      aria-current={active ? "page" : undefined}
                      className={cn(
                        "relative flex min-h-11 items-center gap-3 rounded-xl px-3 py-2 text-base leading-snug transition-colors",
                        focusRing,
                        active
                          ? "bg-primary/10 font-semibold text-brand before:absolute before:inset-y-2.5 before:left-0 before:w-[3px] before:rounded-r-full before:bg-accent"
                          : "text-fg hover:bg-subtle hover:text-brand",
                      )}
                    >
                      <Icon className={cn("size-5 shrink-0", active ? "text-brand" : "text-muted")} />
                      <span>{t(`items.${item.key}`)}</span>
                    </Link>
                  </li>
                );
              })}
            </ul>
          </Accordion.Content>
        </Accordion.Item>
      ))}
    </Accordion.Root>
  );
}
