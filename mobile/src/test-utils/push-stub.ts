import type { PushDevice, PushMemory, PushPermission } from "@/notifications/push";

/*  The phone as push.ts sees it. `events` keeps the order things happened in, across the device,
    the memory and (when a test adds to it) the API, because the order is half of what push promises. */

export const TOKEN = "ExponentPushToken[phone-1]";
export const CHOICE_KEY = "sethika.push.choice";
export const TOKEN_KEY = "sethika.push.token";

const GRANTED: PushPermission = { granted: true, canAskAgain: true };
const UNDECIDED: PushPermission = { granted: false, canAskAgain: true };

export function createPushStub() {
  const events: string[] = [];
  const stored = new Map<string, string>();

  const phone = {
    /** What getPermission answers. */
    permission: UNDECIDED,
    /** What the system prompt ends with. */
    answer: GRANTED,
    channel: (): Promise<unknown> => Promise.resolve({ id: "bookings" }),
    token: (): Promise<string> => Promise.resolve(TOKEN),
  };

  const device: PushDevice = {
    supported: true,
    platform: "Android",
    appVersion: "0.1.0",
    createChannel: (name) => {
      events.push(`channel:${name}`);
      return phone.channel();
    },
    getPermission: async () => {
      events.push("permission");
      return phone.permission;
    },
    requestPermission: async () => {
      events.push("prompt");
      phone.permission = phone.answer;
      return phone.answer;
    },
    getToken: () => {
      events.push("token");
      return phone.token();
    },
  };

  const memory: PushMemory = {
    get: async (key) => stored.get(key) ?? null,
    set: async (key, value) => {
      events.push(`kept:${key}`);
      stored.set(key, value);
    },
    delete: async (key) => {
      events.push(`dropped:${key}`);
      stored.delete(key);
    },
  };

  return { events, stored, phone, device, memory };
}

export const permissions = { GRANTED, UNDECIDED, REFUSED: UNDECIDED, BLOCKED: { granted: false, canAskAgain: false } };
