/*  This phone's half of push (plan F, F31, F32, F41). Framework-free and dependency-injected, like
    session-client.ts, so the order of things is unit-tested without a device.

    - Registering is always: channel, permission, token, PUT. Android shows nothing for a channel
      the phone does not have, and its permission prompt waits for a first channel (F41).
    - The choice is this phone's (F32): on registers, off removes. Nothing on the API remembers it.
      Before any choice, the first sign-in asks once, and a refusal is kept as "off", so the prompt
      never comes back by itself. A launch with the choice on never prompts.
    - The token is kept so the device can be removed without asking Expo for it again, and only
      ever travels in a body (F31).
    - Everything takes its turn: a removal behind a registration in flight cannot be overtaken by it
      and leave the phone registered after sign-out.
    - Push is in addition to texts (F5), so nothing here throws, and a failure never stops a
      sign-out. A launch that fails to register tries again at the next launch.                */

/** ExpoPushGateway.AndroidChannelId. A push naming any other channel is not shown at all. */
export const PUSH_CHANNEL_ID = "bookings";

const CHOICE_KEY = "sethika.push.choice";
const TOKEN_KEY = "sethika.push.token";

/** How long sign-out waits for the removal. A phone with no signal must still be able to sign out. */
export const FORGET_WAIT_MS = 5000;

export type PushStatus = "unsupported" | "on" | "off" | "blocked";
export type PushPermission = { granted: boolean; canAskAgain: boolean };

/** What the phone can do. native-device.ts wires the real one. */
export type PushDevice = {
  /** False in Expo Go, which has had no remote push on Android since SDK 53 (F37). */
  supported: boolean;
  platform: "Android" | "iOS";
  appVersion: string | null;
  createChannel(name: string): Promise<unknown>;
  getPermission(): Promise<PushPermission>;
  requestPermission(): Promise<PushPermission>;
  getToken(): Promise<string>;
};

export type PushMemory = {
  get(key: string): Promise<string | null>;
  set(key: string, value: string): Promise<void>;
  delete(key: string): Promise<void>;
};

export type PushClient = {
  fetch(path: string, init?: RequestInit): Promise<Response>;
  getState(): { status: string };
};

export type PushOptions = { device: PushDevice; memory: PushMemory; client: PushClient };

export function createPush({ device, memory, client }: PushOptions) {
  let queue: Promise<unknown> = Promise.resolve();

  function inTurn<T>(work: () => Promise<T>): Promise<T> {
    const turn = queue.then(work);
    queue = turn.catch(() => {});
    return turn;
  }

  function post(method: "PUT" | "POST", path: string, body: unknown) {
    return client.fetch(path, {
      method,
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });
  }

  function removeFromApi(expoPushToken: string) {
    return post("POST", "/api/me/devices/remove", { expoPushToken });
  }

  /** Rejects if the phone or the API could not do it, and then nothing is decided. */
  async function register(channelName: string, ask: boolean): Promise<"on" | "off" | "blocked"> {
    await device.createChannel(channelName);

    let permission = await device.getPermission();
    if (!permission.granted && ask) permission = await device.requestPermission();
    if (!permission.granted) return permission.canAskAgain ? "off" : "blocked";

    const token = await device.getToken();

    // The token can take seconds; the donor may have signed out in the meantime.
    if (client.getState().status !== "authenticated") throw new Error("Signed out.");

    // Kept first: a token kept but never sent costs a removal that finds nothing, while one sent
    // but never kept could not be removed at all.
    await memory.set(TOKEN_KEY, token);
    const response = await post("PUT", "/api/me/devices", {
      expoPushToken: token,
      platform: device.platform,
      appVersion: device.appVersion,
    });
    if (!response.ok) throw new Error(`Registering answered ${response.status}.`);
    return "on";
  }

  /** After sign-in and at each signed-in launch: asks if nobody has chosen, and registers if on. */
  function sync(channelName: string): Promise<void> {
    if (!device.supported) return Promise.resolve();
    return inTurn(async () => {
      try {
        const choice = await memory.get(CHOICE_KEY);
        if (choice === "off") return;
        const chosen = choice === "on";
        const outcome = await register(channelName, !chosen);
        if (!chosen) await memory.set(CHOICE_KEY, outcome === "on" ? "on" : "off");
      } catch {
        // No signal, or no push credentials in this build. The next launch tries again.
      }
    });
  }

  /** What the Profile switch shows. */
  function status(): Promise<PushStatus> {
    if (!device.supported) return Promise.resolve("unsupported");
    return inTurn(async () => {
      try {
        const permission = await device.getPermission();
        if (!permission.granted) return permission.canAskAgain ? "off" : "blocked";
        return (await memory.get(CHOICE_KEY)) === "on" ? "on" : "off";
      } catch {
        return "off";
      }
    });
  }

  /** The switch going on. "failed" leaves the choice as it was. */
  function enable(channelName: string): Promise<PushStatus | "failed"> {
    return inTurn(async () => {
      try {
        const outcome = await register(channelName, true);
        if (outcome === "on") await memory.set(CHOICE_KEY, "on");
        return outcome;
      } catch {
        return "failed";
      }
    });
  }

  /** The switch going off. "failed" stays on: a switch must not say off while pushes still come. */
  function disable(): Promise<"off" | "failed"> {
    return inTurn(async () => {
      try {
        const token = await memory.get(TOKEN_KEY);
        if (token) {
          const response = await removeFromApi(token);
          if (!response.ok) return "failed";
          await memory.delete(TOKEN_KEY);
        }
        await memory.set(CHOICE_KEY, "off");
        return "off";
      } catch {
        return "failed";
      }
    });
  }

  /**
   * Before a sign-out, while the access token still works (F31). The choice stays: it belongs to
   * the phone, not the account. Waits at most FORGET_WAIT_MS, and the sign-out goes on either way.
   */
  function forget(): Promise<void> {
    if (!device.supported) return Promise.resolve();
    const removal = inTurn(async () => {
      try {
        const token = await memory.get(TOKEN_KEY);
        if (!token) return;
        await removeFromApi(token).catch(() => {});
        await memory.delete(TOKEN_KEY);
      } catch {
        // The sign-out is what matters now.
      }
    });

    return new Promise((resolve) => {
      const timer = setTimeout(resolve, FORGET_WAIT_MS);
      void removal.then(() => {
        clearTimeout(timer);
        resolve();
      });
    });
  }

  return { sync, status, enable, disable, forget };
}

export type Push = ReturnType<typeof createPush>;
