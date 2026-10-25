import { useId } from "react";
import { cn } from "@/lib/utils";

type BowlProps = {
  /** How much of the bowl is filled, 0 to 1: the share of the day's places already taken. */
  fill: number;
  className?: string;
};

/**
 * Sethika's mark as a gauge (docs/DESIGN.md, the bowl): an empty bowl is a day waiting for a
 * sponsor, and each sponsor fills it a little, until it is full. Decorative: the day's words say
 * the same thing for screen readers, so it is hidden from them.
 */
export function Bowl({ fill, className }: BowlProps) {
  const clip = useId();
  const level = Math.min(1, Math.max(0, fill));
  // The bowl's body runs from the rim (y 5) to its lowest point (y 16).
  const height = 11 * level;

  return (
    <svg viewBox="0 0 28 19" aria-hidden className={cn("h-[1.1rem] w-7", className)} fill="none">
      <defs>
        <clipPath id={clip}>
          <path d="M2 5h24a12 11 0 0 1-24 0z" />
        </clipPath>
      </defs>
      {height > 0 && (
        <rect x="0" y={16 - height} width="28" height={height} clipPath={`url(#${clip})`} className="fill-current opacity-80" />
      )}
      <path d="M2 5h24a12 11 0 0 1-24 0z" stroke="currentColor" strokeWidth="1.6" strokeLinejoin="round" />
      <path d="M10.5 18h7" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" />
    </svg>
  );
}
