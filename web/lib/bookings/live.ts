import { HubConnectionBuilder, LogLevel, type HubConnection, type ILogger } from "@microsoft/signalr";
import { firstOfMonth, lastOfMonth } from "./calendar";

/*  Live slot updates (plan 07, H9-H11). The API's hub tells every calendar watching an institution
    that some of its days changed; the calendar fetches its month again. A hint, never the data: a
    lost one costs freshness, not correctness, because the API still refuses a full day.

    The hub is under /api, so the one proxy rule (A3) carries it and the browser keeps talking to
    one origin. Where the proxy cannot carry a WebSocket, SignalR falls back to Server-Sent Events or
    long polling by itself.                                                                       */

export const HUB_PATH = "/api/hubs/availability";

/** What the hub sends: the institution, and the days that changed (both null: every day). */
export type AvailabilityChanged = { slug: string; from: string | null; to: string | null };

/** Whether a change of `from`..`to` touches `month` ("yyyy-MM"): no range touches every month. */
export function touchesMonth(change: Pick<AvailabilityChanged, "from" | "to">, month: string): boolean {
  if (change.from === null || change.to === null) return true;
  return change.from <= lastOfMonth(month) && change.to >= firstOfMonth(month);
}

/**
 * SignalR's messages as warnings, never errors. Live updates are a hint (H9): a connection that fails,
 * or is stopped while it negotiates - a page left at once, or React mounting an effect twice in
 * development - leaves the calendar working as before. SignalR logs those as errors, which the
 * development overlay then shows over the page.
 */
export const liveLogger: ILogger = {
  log(level, message) {
    if (level >= LogLevel.Warning && level !== LogLevel.None) console.warn(`[live] ${message}`);
  },
};

/**
 * Watches `slug` until the returned function is called. `onChange` hears each change, and a change
 * of every day after a reconnect: anything may have changed while the connection was down.
 */
export function watchAvailability(slug: string, onChange: (change: AvailabilityChanged) => void): () => void {
  const connection: HubConnection = new HubConnectionBuilder()
    .withUrl(HUB_PATH)
    .withAutomaticReconnect()
    .configureLogging(liveLogger)
    .build();

  let stopped = false;
  connection.on("AvailabilityChanged", (change: AvailabilityChanged) => {
    if (change.slug === slug) onChange(change);
  });
  connection.onreconnected(() => {
    void connection.invoke("Watch", slug).catch(() => undefined);
    onChange({ slug, from: null, to: null });
  });

  void connection
    .start()
    .then(() => (stopped ? undefined : connection.invoke("Watch", slug)))
    .catch(() => undefined); // No live updates; the calendar still works as before.

  return () => {
    stopped = true;
    void connection.stop();
  };
}
