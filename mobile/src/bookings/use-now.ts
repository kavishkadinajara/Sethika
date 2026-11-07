import { useEffect, useState } from "react";
import { AppState } from "react-native";

/**
 * The time, kept fresh for a screen that says what today is: every 30 seconds, as on the web, and
 * at once when the phone comes back to the app, because timers stop while it is in the background.
 */
export function useNow(): Date {
  const [now, setNow] = useState(() => new Date());

  useEffect(() => {
    const tick = () => setNow(new Date());
    const timer = setInterval(tick, 30_000);
    const subscription = AppState.addEventListener("change", (state) => {
      if (state === "active") tick();
    });
    return () => {
      clearInterval(timer);
      subscription.remove();
    };
  }, []);

  return now;
}
