"use client";

import { useCallback, useEffect, useState } from "react";

/** Seconds left until a moment, ticking once a second. 0 when there is nothing to wait for. */
export function useCountdown() {
  const [until, setUntil] = useState<number | null>(null);
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    if (until === null) return;
    const id = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(id);
  }, [until]);

  const remaining = until === null ? 0 : Math.max(0, Math.ceil((until - now) / 1000));

  const start = useCallback((seconds: number) => {
    const start = Date.now();
    setNow(start);
    setUntil(start + seconds * 1000);
  }, []);

  return { remaining, start };
}

/** 75 -> "1:15". Digits read the same in every language Sethika serves. */
export function formatWait(seconds: number) {
  const minutes = Math.floor(seconds / 60);
  return `${minutes}:${String(seconds % 60).padStart(2, "0")}`;
}
