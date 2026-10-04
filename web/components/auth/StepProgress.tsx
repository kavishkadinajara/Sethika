import { cn } from "@/lib/utils";

/** "Step 2 of 4" and a bar per step, for the multi-step auth flows. */
export function StepProgress({ current, total, label }: { current: number; total: number; label: string }) {
  return (
    <div className="mb-6 grid gap-2">
      <p className="text-sm font-medium text-muted">{label}</p>
      <div aria-hidden className="grid gap-1.5" style={{ gridTemplateColumns: `repeat(${total}, minmax(0, 1fr))` }}>
        {Array.from({ length: total }, (_, index) => (
          <span key={index} className={cn("h-1.5 rounded-full", index < current ? "bg-primary" : "bg-subtle")} />
        ))}
      </div>
    </div>
  );
}
