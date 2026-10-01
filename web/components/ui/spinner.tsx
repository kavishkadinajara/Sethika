import { useTranslations } from "next-intl";
import { cn } from "@/lib/utils";

const sizes = { sm: "size-4", md: "size-6", lg: "size-9" } as const;

type SpinnerProps = {
  size?: keyof typeof sizes;
  /** Overrides the default "Loading" announcement. */
  label?: string;
  /** Inside a control that already says it is busy (aria-busy), stay silent. */
  decorative?: boolean;
  className?: string;
};

/** Colour follows `currentColor`, so it sits on any surface. */
export function Spinner({ size = "md", label, decorative = false, className }: SpinnerProps) {
  const t = useTranslations("Common");

  const svg = (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      aria-hidden
      focusable={false}
      // Under reduced motion it still turns, slowly: a frozen spinner would read as "stuck".
      className={cn(
        "animate-spin motion-reduce:animate-[spin_2.4s_linear_infinite]",
        sizes[size],
        className,
      )}
    >
      <circle cx="12" cy="12" r="9.5" stroke="currentColor" strokeOpacity="0.2" strokeWidth="3" />
      <path d="M21.5 12A9.5 9.5 0 0 0 12 2.5" stroke="currentColor" strokeWidth="3" strokeLinecap="round" />
    </svg>
  );

  if (decorative) return svg;

  return (
    <span role="status" className="inline-flex items-center">
      {svg}
      <span className="sr-only">{label ?? t("loading")}</span>
    </span>
  );
}
