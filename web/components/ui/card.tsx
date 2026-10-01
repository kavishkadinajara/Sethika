import type { ComponentProps } from "react";
import { cn } from "@/lib/utils";

type CardProps = ComponentProps<"div"> & {
  /** A thin Offering Gold rule along the top edge, for the card that matters most on a screen. */
  accent?: boolean;
};

export function Card({ className, accent = false, ...props }: CardProps) {
  return (
    <div
      className={cn(
        "relative flex min-w-0 flex-col overflow-hidden rounded-2xl border border-hairline bg-surface text-ink shadow-card",
        accent && "before:absolute before:inset-x-0 before:top-0 before:h-1 before:bg-gold",
        className,
      )}
      {...props}
    />
  );
}

export function CardHeader({ className, ...props }: ComponentProps<"div">) {
  return <div className={cn("grid gap-1.5 px-5 pt-5 sm:px-6 sm:pt-6", className)} {...props} />;
}

type CardTitleProps = ComponentProps<"h2"> & { as?: "h2" | "h3" | "h4" };

/** A real heading, so screen-reader users can jump between cards. Pick the level that fits the page. */
export function CardTitle({ className, as: Heading = "h3", ...props }: CardTitleProps) {
  return (
    <Heading
      className={cn("text-xl font-semibold leading-snug text-estate", className)}
      {...props}
    />
  );
}

export function CardDescription({ className, ...props }: ComponentProps<"p">) {
  return <p className={cn("text-sm leading-normal text-muted", className)} {...props} />;
}

export function CardContent({ className, ...props }: ComponentProps<"div">) {
  return <div className={cn("px-5 py-4 sm:px-6", className)} {...props} />;
}

export function CardFooter({ className, ...props }: ComponentProps<"div">) {
  return (
    <div
      className={cn(
        "mt-auto flex flex-wrap items-center gap-3 border-t border-hairline px-5 py-4 sm:px-6",
        className,
      )}
      {...props}
    />
  );
}
