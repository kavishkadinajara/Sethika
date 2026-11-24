import * as Notifications from "expo-notifications";
import { router } from "expo-router";
import { createContext, useContext, useEffect, useMemo, type ReactNode } from "react";

import { useSession, useSessionClient } from "@/auth/session-provider";
import { useI18n } from "@/i18n/i18n-provider";

import { nativeDevice, nativeMemory } from "./native-device";
import { createPush, type Push } from "./push";

const PushContext = createContext<Push | null>(null);

/** One per app, so the turns in push.ts are the only turns. */
export function PushProvider({ children }: { children: ReactNode }) {
  const client = useSessionClient();
  const push = useMemo(() => createPush({ device: nativeDevice, memory: nativeMemory, client }), [client]);

  return (
    <PushContext.Provider value={push}>
      <Registration />
      {nativeDevice.supported && <Taps />}
      {children}
    </PushContext.Provider>
  );
}

export function usePush(): Push {
  const push = useContext(PushContext);
  if (!push) throw new Error("usePush needs a PushProvider.");
  return push;
}

/** The channel's name is what Android's settings list, so a new language renames it. */
export function useChannelName(): string {
  return useI18n().t("App.profile.push.channel");
}

/** Runs when a session starts and at each signed-in launch. A session rotating changes nothing here. */
function Registration() {
  const push = usePush();
  const channelName = useChannelName();
  const signedIn = useSession().status === "authenticated";

  useEffect(() => {
    if (signedIn) void push.sync(channelName);
  }, [signedIn, push, channelName]);

  return null;
}

/** Every push is about a booking (F27), and its data is `{ code }`: ExpoPushGateway. */
function opensBookings(response: Notifications.NotificationResponse): boolean {
  const code = response.notification.request.content.data?.code;
  return (
    response.actionIdentifier === Notifications.DEFAULT_ACTION_IDENTIFIER &&
    typeof code === "string" &&
    code.startsWith("BOOKING_")
  );
}

/**
 * A tap opens My bookings, whether the app was running or the tap started it: the hook answers
 * with the tap that launched the app too. Cleared, or the same tap would open it again the next
 * time this mounts. A signed-out donor gets the screen's own sign-in prompt.
 */
function Taps() {
  const response = Notifications.useLastNotificationResponse();

  useEffect(() => {
    if (!response) return;
    if (opensBookings(response)) router.navigate("/bookings");
    Notifications.clearLastNotificationResponseAsync().catch(() => {});
  }, [response]);

  return null;
}
