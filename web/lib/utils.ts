import { clsx, type ClassValue } from "clsx";
import { twMerge } from "tailwind-merge";

/** Joins class names and lets a later Tailwind class override an earlier one (shadcn/ui's `cn`). */
export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}

/**
 * Signature 2 - the offering glow (docs/DESIGN.md): an Estate Green outline, which carries
 * the contrast (8.4:1 on ivory), inside a soft Offering Gold halo, which carries the brand.
 * Every focusable primitive uses this one ring.
 */
export const focusRing =
  "focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand focus-visible:ring-6 focus-visible:ring-accent/40";
