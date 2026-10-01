import { cva, type VariantProps } from "class-variance-authority";
import { Slot } from "radix-ui";
import type { ComponentProps } from "react";
import { cn } from "@/lib/utils";
import { Spinner } from "./spinner";

/*  Pill-shaped, 44px minimum touch target at every size. Height is a minimum, never fixed,
    and labels wrap: Sinhala and Tamil labels run longer than English and must not clip.  */
export const buttonVariants = cva(
  [
    "relative inline-flex max-w-full min-h-11 items-center justify-center gap-2 rounded-full",
    "px-5 py-2 text-center text-base font-medium leading-snug select-none",
    "transition-[background-color,border-color,color,transform] duration-150",
    "focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-estate",
    "active:translate-y-px motion-reduce:active:translate-y-0",
    "disabled:cursor-not-allowed disabled:opacity-55 aria-busy:cursor-progress",
    "aria-disabled:pointer-events-none aria-disabled:opacity-55",
    "[&_svg]:shrink-0",
  ],
  {
    variants: {
      variant: {
        primary:
          "bg-estate text-ivory shadow-[inset_0_1px_0_rgb(255_255_255/0.14),0_1px_2px_rgb(14_42_33/0.3)] hover:bg-estate-strong",
        accent:
          "bg-gold text-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.3),0_1px_2px_rgb(14_42_33/0.2)] hover:bg-gold-strong",
        outline: "border-[1.5px] border-estate bg-transparent text-estate hover:bg-estate/8",
        ghost: "bg-transparent text-estate hover:bg-estate/8",
        danger: "bg-danger text-white hover:bg-danger-strong",
      },
      size: {
        md: "",
        lg: "min-h-13 px-7 text-lg",
        icon: "size-11 shrink-0 p-0",
      },
    },
    defaultVariants: { variant: "primary", size: "md" },
  },
);

type ButtonProps = ComponentProps<"button"> &
  VariantProps<typeof buttonVariants> & {
    /** Render the child (a link, say) with button styling instead of a <button>. */
    asChild?: boolean;
    /** Disables the button, marks it aria-busy and shows a spinner before the label. */
    loading?: boolean;
  };

export function Button({
  className,
  variant,
  size,
  asChild = false,
  loading = false,
  disabled,
  children,
  type,
  ...props
}: ButtonProps) {
  const classes = cn(buttonVariants({ variant, size }), className);

  if (asChild) {
    return (
      <Slot.Root className={classes} {...props}>
        {children}
      </Slot.Root>
    );
  }

  return (
    <button
      type={type ?? "button"}
      className={classes}
      disabled={disabled || loading}
      aria-busy={loading || undefined}
      {...props}
    >
      {loading && <Spinner size="sm" decorative />}
      {children}
    </button>
  );
}
