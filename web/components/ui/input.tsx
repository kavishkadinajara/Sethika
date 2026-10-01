import type { ComponentProps } from "react";
import { cn, focusRing } from "@/lib/utils";

/*  16px text so iOS does not zoom on focus; min-height rather than height so tall
    Sinhala and Tamil glyphs are never clipped. Invalid state follows aria-invalid, which
    Field sets.                                                                         */
export const inputClasses = cn(
  // A cup (docs/DESIGN.md): the shape and the faint inner shadow say "put something in here".
  "block w-full min-h-12 rounded-cup border-[1.5px] border-line bg-surface px-4 py-2.5 shadow-well",
  "text-base leading-normal text-ink placeholder:text-muted",
  "transition-[border-color,box-shadow] duration-150 hover:border-ink/60",
  "focus-visible:border-estate",
  focusRing,
  "disabled:cursor-not-allowed disabled:border-hairline disabled:bg-subtle disabled:text-muted disabled:shadow-none",
  "aria-invalid:border-danger aria-invalid:bg-danger-soft/40 aria-invalid:focus-visible:outline-danger",
);

export function Input({ className, type = "text", ...props }: ComponentProps<"input">) {
  return <input type={type} className={cn(inputClasses, className)} {...props} />;
}
