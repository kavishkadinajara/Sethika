"use client";

import { useEffect, useRef, type ReactNode } from "react";

/**
 * A screen or step heading that takes focus when it appears (`focusOnMount`), so a screen-reader
 * user moving to the next step hears where they are instead of being left on a vanished button.
 */
export function AuthHeading({
  title,
  description,
  focusOnMount = false,
}: {
  title: ReactNode;
  description?: ReactNode;
  focusOnMount?: boolean;
}) {
  const ref = useRef<HTMLHeadingElement>(null);

  useEffect(() => {
    if (focusOnMount) ref.current?.focus();
  }, [focusOnMount]);

  return (
    <div className="mb-6 grid gap-1.5">
      <h1 ref={ref} tabIndex={-1} className="text-2xl font-semibold leading-snug text-brand outline-none">
        {title}
      </h1>
      {description && <p className="text-muted">{description}</p>}
    </div>
  );
}
