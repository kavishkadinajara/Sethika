/* eslint-disable @next/next/no-img-element -- an 850-byte SVG needs no image optimisation */
import { cn } from "@/lib/utils";

/** The Sethika bowl. The ivory variant on dark backgrounds; follows the theme switcher. */
export function BrandMark({ className }: { className?: string }) {
  return (
    <>
      <img src="/brand/sethika-mark-light.svg" alt="" width={40} height={40} className={cn("size-10 dark:hidden", className)} />
      <img src="/brand/sethika-mark-dark.svg" alt="" width={40} height={40} className={cn("hidden size-10 dark:block", className)} />
    </>
  );
}
