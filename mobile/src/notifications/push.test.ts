import appConfig from "../../app.json";

import { CHOICE_KEY, createPushStub, permissions, TOKEN, TOKEN_KEY } from "@/test-utils/push-stub";

import { createPush, FORGET_WAIT_MS, PUSH_CHANNEL_ID } from "./push";

/*  The order and the rules of push on a phone, against a scripted phone and the API's contract:
      PUT /api/me/devices           204 | 400
      POST /api/me/devices/remove   204, the token in the body                                  */

declare const __dirname: string;

const CHANNEL = "Bookings";
const PUT = "PUT /api/me/devices";
const REMOVE = "POST /api/me/devices/remove";

function harness() {
  const stub = createPushStub();
  const requests: { line: string; body: unknown }[] = [];
  const api = {
    status: "authenticated",
    answer: (): Promise<Response> => Promise.resolve(new Response(null, { status: 204 })),
  };

  const client = {
    getState: () => ({ status: api.status }),
    fetch: (path: string, init?: RequestInit) => {
      const line = `${init?.method} ${path}`;
      stub.events.push(line);
      requests.push({ line, body: JSON.parse(String(init?.body)) });
      return api.answer();
    },
  };

  return { ...stub, api, requests, push: createPush({ device: stub.device, memory: stub.memory, client }) };
}

const refused = () => Promise.resolve(new Response(null, { status: 500 }));
const noSignal = () => Promise.reject(new Error("Network request failed"));

describe("the channel's name, which Android shows nothing without (F41)", () => {
  const { readFileSync } = jest.requireActual<{ readFileSync: (path: string, encoding: "utf8") => string }>("fs");

  it("is the one the API pushes to", () => {
    const gateway = readFileSync(`${__dirname}/../../../src/Sethika.Infrastructure/Notifications/ExpoPushGateway.cs`, "utf8");

    expect(gateway).toContain(`AndroidChannelId = "${PUSH_CHANNEL_ID}"`);
  });

  it("is the build's default channel", () => {
    const plugin = appConfig.expo.plugins.find((entry) => Array.isArray(entry) && entry[0] === "expo-notifications");

    expect((plugin?.[1] as unknown as { defaultChannel: string }).defaultChannel).toBe(PUSH_CHANNEL_ID);
  });
});

describe("after sign-in, when nobody has chosen yet", () => {
  it("makes the channel first, then asks, then takes the token, then registers", async () => {
    const { push, events, stored, requests } = harness();

    await push.sync(CHANNEL);

    expect(events).toEqual([`channel:${CHANNEL}`, "permission", "prompt", "token", `kept:${TOKEN_KEY}`, PUT, `kept:${CHOICE_KEY}`]);
    expect(requests[0]?.body).toEqual({ expoPushToken: TOKEN, platform: "Android", appVersion: "0.1.0" });
    expect(stored.get(CHOICE_KEY)).toBe("on");
    expect(stored.get(TOKEN_KEY)).toBe(TOKEN);
  });

  it("does not prompt when the phone already allows notifications", async () => {
    const { push, events, phone, stored } = harness();
    phone.permission = permissions.GRANTED;

    await push.sync(CHANNEL);

    expect(events).not.toContain("prompt");
    expect(events).toContain(PUT);
    expect(stored.get(CHOICE_KEY)).toBe("on");
  });

  it("never asks for a token if the channel could not be made", async () => {
    const { push, events, phone, stored } = harness();
    phone.channel = () => Promise.reject(new Error("no channel"));

    await push.sync(CHANNEL);

    expect(events).toEqual([`channel:${CHANNEL}`]);
    expect(stored.has(CHOICE_KEY)).toBe(false);
  });

  it("takes a refusal as off, and never prompts again by itself", async () => {
    const { push, events, phone, stored } = harness();
    phone.answer = permissions.REFUSED;

    await push.sync(CHANNEL);
    expect(stored.get(CHOICE_KEY)).toBe("off");
    expect(events).not.toContain("token");
    expect(events).not.toContain(PUT);

    events.length = 0;
    await push.sync(CHANNEL);
    expect(events).toEqual([]);
  });

  it("leaves the choice open when the token cannot be had, and tries again next launch", async () => {
    const { push, events, phone, stored } = harness();
    phone.token = () => Promise.reject(new Error("ERR_NOTIFICATIONS_NO_EXPERIENCE_ID"));

    await push.sync(CHANNEL);
    expect(events).not.toContain(PUT);
    expect(stored.has(CHOICE_KEY)).toBe(false);

    phone.token = () => Promise.resolve(TOKEN);
    await push.sync(CHANNEL);
    expect(events).toContain(PUT);
    expect(stored.get(CHOICE_KEY)).toBe("on");
  });

  it.each([
    ["refuses", refused],
    ["cannot be reached", noSignal],
  ])("leaves the choice open when the API %s", async (_, answer) => {
    const { push, api, stored } = harness();
    api.answer = answer;

    await expect(push.sync(CHANNEL)).resolves.toBeUndefined();

    expect(stored.has(CHOICE_KEY)).toBe(false);
  });

  it("registers nothing if the donor signed out while the token was coming", async () => {
    const { push, api, events, phone, stored } = harness();
    phone.token = async () => {
      api.status = "anonymous";
      return TOKEN;
    };

    await push.sync(CHANNEL);

    expect(events).not.toContain(PUT);
    expect(stored.has(TOKEN_KEY)).toBe(false);
  });
});

describe("at a later launch", () => {
  it("registers again with the choice on, and never prompts", async () => {
    const { push, events, phone, stored } = harness();
    stored.set(CHOICE_KEY, "on");
    phone.permission = permissions.GRANTED;

    await push.sync(CHANNEL);

    expect(events).toContain(PUT);
    expect(events).not.toContain("prompt");
  });

  it("keeps the choice and stays quiet if the phone's settings took the permission away", async () => {
    const { push, events, phone, stored } = harness();
    stored.set(CHOICE_KEY, "on");
    phone.permission = permissions.UNDECIDED;

    await push.sync(CHANNEL);

    expect(events).not.toContain("prompt");
    expect(events).not.toContain(PUT);
    expect(stored.get(CHOICE_KEY)).toBe("on");
  });

  it("does nothing at all with the choice off", async () => {
    const { push, events, stored } = harness();
    stored.set(CHOICE_KEY, "off");

    await push.sync(CHANNEL);

    expect(events).toEqual([]);
  });
});

describe("where push cannot work (Expo Go)", () => {
  it("touches nothing", async () => {
    const { push, device, events, stored } = harness();
    device.supported = false;
    stored.set(TOKEN_KEY, TOKEN);

    await push.sync(CHANNEL);
    await push.forget();

    expect(events).toEqual([]);
    await expect(push.status()).resolves.toBe("unsupported");
  });
});

describe("the switch's state", () => {
  it.each([
    ["on", permissions.GRANTED, "on", "on"],
    ["off", permissions.GRANTED, "off", "off"],
    ["nobody chose", permissions.GRANTED, undefined, "off"],
    ["on, but refused and askable", permissions.REFUSED, "on", "off"],
    ["on, but blocked", permissions.BLOCKED, "on", "blocked"],
    ["off and blocked", permissions.BLOCKED, "off", "blocked"],
  ])("%s", async (_, permission, choice, expected) => {
    const { push, phone, stored } = harness();
    phone.permission = permission;
    if (choice) stored.set(CHOICE_KEY, choice);

    await expect(push.status()).resolves.toBe(expected);
  });

  it("waits for a registration in flight, so it never reads the choice half made", async () => {
    const { push, phone } = harness();
    phone.permission = permissions.GRANTED;

    void push.sync(CHANNEL);

    await expect(push.status()).resolves.toBe("on");
  });

  it("reads as off if the phone cannot say", async () => {
    const { push, device } = harness();
    device.getPermission = () => Promise.reject(new Error("no answer"));

    await expect(push.status()).resolves.toBe("off");
  });
});

describe("turning it on", () => {
  it("makes the channel first, asks even after an earlier no, and remembers the yes", async () => {
    const { push, events, stored } = harness();
    stored.set(CHOICE_KEY, "off");

    await expect(push.enable(CHANNEL)).resolves.toBe("on");

    expect(events.slice(0, 4)).toEqual([`channel:${CHANNEL}`, "permission", "prompt", "token"]);
    expect(events).toContain(PUT);
    expect(stored.get(CHOICE_KEY)).toBe("on");
  });

  it.each([
    ["off", permissions.REFUSED],
    ["blocked", permissions.BLOCKED],
  ])("answers %s when the phone says no, and keeps the choice off", async (expected, answer) => {
    const { push, events, phone, stored } = harness();
    stored.set(CHOICE_KEY, "off");
    phone.answer = answer;

    await expect(push.enable(CHANNEL)).resolves.toBe(expected);

    expect(events).not.toContain(PUT);
    expect(stored.get(CHOICE_KEY)).toBe("off");
  });

  it.each([
    ["refuses", refused],
    ["cannot be reached", noSignal],
  ])("fails and keeps the choice off when the API %s", async (_, answer) => {
    const { push, api, stored } = harness();
    stored.set(CHOICE_KEY, "off");
    api.answer = answer;

    await expect(push.enable(CHANNEL)).resolves.toBe("failed");

    expect(stored.get(CHOICE_KEY)).toBe("off");
  });
});

describe("turning it off", () => {
  it("removes this phone by a token in the body, then remembers the no", async () => {
    const { push, requests, stored } = harness();
    stored.set(CHOICE_KEY, "on");
    stored.set(TOKEN_KEY, TOKEN);

    await expect(push.disable()).resolves.toBe("off");

    expect(requests).toEqual([{ line: REMOVE, body: { expoPushToken: TOKEN } }]);
    expect(stored.get(CHOICE_KEY)).toBe("off");
    expect(stored.has(TOKEN_KEY)).toBe(false);
  });

  it.each([
    ["refuses", refused],
    ["cannot be reached", noSignal],
  ])("stays on when the API %s: pushes would still come", async (_, answer) => {
    const { push, api, stored } = harness();
    stored.set(CHOICE_KEY, "on");
    stored.set(TOKEN_KEY, TOKEN);
    api.answer = answer;

    await expect(push.disable()).resolves.toBe("failed");

    expect(stored.get(CHOICE_KEY)).toBe("on");
    expect(stored.get(TOKEN_KEY)).toBe(TOKEN);
  });

  it("needs no call when this phone was never registered", async () => {
    const { push, requests, stored } = harness();

    await expect(push.disable()).resolves.toBe("off");

    expect(requests).toEqual([]);
    expect(stored.get(CHOICE_KEY)).toBe("off");
  });
});

describe("before a sign-out", () => {
  it("removes this phone, drops the token, and keeps the phone's choice", async () => {
    const { push, requests, stored } = harness();
    stored.set(CHOICE_KEY, "on");
    stored.set(TOKEN_KEY, TOKEN);

    await push.forget();

    expect(requests).toEqual([{ line: REMOVE, body: { expoPushToken: TOKEN } }]);
    expect(stored.has(TOKEN_KEY)).toBe(false);
    expect(stored.get(CHOICE_KEY)).toBe("on");
  });

  it("needs no call when this phone was not registered", async () => {
    const { push, requests } = harness();

    await push.forget();

    expect(requests).toEqual([]);
  });

  it("still ends, and drops the token, with no signal", async () => {
    const { push, api, stored } = harness();
    stored.set(TOKEN_KEY, TOKEN);
    api.answer = noSignal;

    await expect(push.forget()).resolves.toBeUndefined();

    expect(stored.has(TOKEN_KEY)).toBe(false);
  });

  it("still ends if the phone's store fails", async () => {
    const { push, memory } = harness();
    memory.get = () => Promise.reject(new Error("store"));

    await expect(push.forget()).resolves.toBeUndefined();
  });

  it("waits for a registration in flight, so it cannot be overtaken and leave the phone registered", async () => {
    const { push, api, events, phone, stored } = harness();
    phone.permission = permissions.GRANTED;
    stored.set(CHOICE_KEY, "on");
    let finishPut = (_: Response) => {};
    api.answer = () => new Promise((resolve) => (finishPut = resolve));

    void push.sync(CHANNEL);
    const forgotten = push.forget();
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(events).toContain(PUT);
    expect(events).not.toContain(REMOVE);

    api.answer = () => Promise.resolve(new Response(null, { status: 204 }));
    finishPut(new Response(null, { status: 204 }));
    await forgotten;

    expect(events.indexOf(PUT)).toBeLessThan(events.indexOf(REMOVE));
  });

  describe("on a call that never answers", () => {
    beforeEach(() => {
      jest.useFakeTimers();
    });

    afterEach(() => {
      jest.useRealTimers();
    });

    it("lets the sign-out go on after the wait, and not before", async () => {
      const { push, api, stored } = harness();
      stored.set(TOKEN_KEY, TOKEN);
      api.answer = () => new Promise(() => {});
      let done = false;

      void push.forget().then(() => (done = true));

      await jest.advanceTimersByTimeAsync(FORGET_WAIT_MS - 1);
      expect(done).toBe(false);
      await jest.advanceTimersByTimeAsync(1);
      expect(done).toBe(true);
    });
  });
});
