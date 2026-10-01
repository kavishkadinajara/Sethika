import type { ComponentProps } from "react";
import { cn } from "@/lib/utils";

/*  16px text so iOS does not zoom on focus; min-height rather than height so tall
    Sinhala and Tamil glyphs are never clipped. Invalid state follows aria-invalid, which
    Field sets.                                                                         */
export const inputClasses = cn(
  "block w-full min-h-11 rounded-xl border-[1.5px] border-line bg-surface px-4 py-2",
  "text-base leading-normal text-ink placeholder:text-muted",
  "transition-colors hover:border-ink/60",
  "focus-visible:border-estate focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-estate",
  "disabled:cursor-not-allowed disabled:border-hairline disabled:bg-subtle disabled:text-muted",
  "aria-invalid:border-danger aria-invalid:bg-danger-soft/40 aria-invalid:focus-visible:outline-danger",
);

export function Input({ className, type = "text", ...props }: ComponentProps<"input">) {
  return <input type={type} className={cn(inputClasses, className)} {...props} />;
}
