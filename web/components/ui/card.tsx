import type { ComponentProps } from "react";
import { cn } from "@/lib/utils";

type CardProps = ComponentProps<"div"> & {
  /** The gold rim mark along the top edge, for the one card that matters most on a screen. */
  accent?: boolean;
};

export function Card({ className, accent = false, ...props }: CardProps) {
  return (
    <div
      className={cn(
        // A bowl (docs/DESIGN.md): gentle top corners, deeper bottom ones, warm green-tinted lift.
        "relative flex min-w-0 flex-col rounded-bowl border border-hairline bg-surface text-fg shadow-lift",
        accent &&
          "before:absolute before:-top-px before:left-5 before:h-[3px] before:w-12 before:rounded-b-full before:bg-accent sm:before:left-6",
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
      className={cn("text-xl font-semibold leading-snug text-brand", className)}
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
