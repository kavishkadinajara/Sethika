import type { ReactNode } from "react";
import { AlertCircleIcon } from "@/components/ui/icons";
import { cn } from "@/lib/utils";

/**
 * A form-level error (not tied to one field). Always mounted as a live region, so a message
 * that appears after submit is announced; empty, it takes no space in the layout.
 */
export function FormError({ children, className }: { children?: ReactNode; className?: string }) {
  const shown = children != null && children !== false && children !== "";
  return (
    // Empty, it is taken out of the flow (absolute) rather than hidden: it must stay in the
    // accessibility tree to announce what arrives, but should not add a gap to the form's grid.
    <div aria-live="polite" className={shown ? className : "absolute"}>
      {shown && (
        <div
          className={cn(
            "flex items-start gap-2.5 rounded-cup border border-danger/40 bg-danger-soft px-4 py-3",
            "text-sm leading-normal text-fg",
          )}
        >
          <AlertCircleIcon className="mt-0.5 size-4.5 shrink-0 text-danger" />
          <div className="min-w-0">{children}</div>
        </div>
      )}
    </div>
  );
}
