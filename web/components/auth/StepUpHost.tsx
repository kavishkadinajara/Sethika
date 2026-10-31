"use client";

import { lazy, Suspense, useEffect, useState } from "react";
import { session } from "@/lib/auth";

// The dialog (Radix Dialog and the form) is fetched only when an action needs a step-up.
const StepUpDialog = lazy(() => import("./StepUpDialog"));

/**
 * Answers lib/auth's step-up hook: when an API call comes back 403 stepup_required, the fetch
 * wrapper calls the handler registered here, which opens the dialog and resolves true once the
 * code is verified (the wrapper then retries the call) or false if the donor cancels.
 * Mounted once, in the root layout.
 */
export function StepUpHost() {
  const [pending, setPending] = useState<((verified: boolean) => void) | null>(null);

  useEffect(
    () =>
      session.setStepUpHandler(
        () => new Promise<boolean>((resolve) => setPending(() => resolve)),
      ),
    [],
  );

  if (!pending) return null;

  return (
    <Suspense fallback={null}>
      <StepUpDialog
        onDone={(verified) => {
          pending(verified);
          setPending(null);
        }}
      />
    </Suspense>
  );
}
