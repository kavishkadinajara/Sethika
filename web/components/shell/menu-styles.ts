import { cn } from "@/lib/utils";

/** The shell's dropdown menus (account, language, institution): a bowl that settles in. */
export const menuContent = cn(
  "z-50 min-w-64 max-w-[calc(100vw-2rem)] overflow-y-auto rounded-bowl border border-hairline bg-surface p-2 text-fg shadow-lift",
  "max-h-[var(--radix-dropdown-menu-content-available-height)]",
  "data-[state=open]:animate-menu-in motion-reduce:animate-none focus-visible:outline-none",
);

/** One row: a 44 px target, the highlight following the pointer and the keyboard alike. */
export const menuItem = cn(
  "relative flex min-h-11 cursor-pointer items-center gap-3 rounded-xl px-3 py-2 text-base leading-snug text-fg outline-none select-none",
  "data-[highlighted]:bg-subtle data-[highlighted]:text-brand",
  "data-[disabled]:cursor-not-allowed data-[disabled]:opacity-55",
);

export const menuSeparator = "my-1.5 h-px bg-hairline";
