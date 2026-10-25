import type { ComponentType, SVGProps } from "react";
import {
  BowlIcon,
  CalendarIcon,
  DashboardIcon,
  FileTextIcon,
  GivingIcon,
  InboxIcon,
  InstitutionIcon,
  KeyIcon,
  SearchIcon,
  ShieldIcon,
  TicketIcon,
  UsersIcon,
} from "@/components/ui/icons";
import type { NavIcon } from "@/lib/shell/nav";

/** Draws the menu's icon names (lib/shell/nav.ts). */
export const navIcons: Record<NavIcon, ComponentType<SVGProps<SVGSVGElement>>> = {
  dashboard: DashboardIcon,
  giving: GivingIcon,
  institution: InstitutionIcon,
  platform: ShieldIcon,
  search: SearchIcon,
  ticket: TicketIcon,
  file: FileTextIcon,
  calendar: CalendarIcon,
  bowl: BowlIcon,
  inbox: InboxIcon,
  key: KeyIcon,
  users: UsersIcon,
};
