import net from "node:net";

/*  Signups, sign-ins and new applications are rate limited per client and per account, and
    every browser test comes from 127.0.0.1. Run after run, the suite would start failing on
    429s for reasons that have nothing to do with the screens - so the setup clears the
    development Redis database's rate-limit counters (rl:*) first. The limits themselves are
    covered by the API's integration tests.

    Two commands are all it needs, so this speaks Redis's protocol directly rather than adding a
    client package to the web app.                                                           */

const host = process.env.E2E_REDIS_HOST ?? "127.0.0.1";
const port = Number(process.env.E2E_REDIS_PORT ?? 6379);
const db = process.env.E2E_REDIS_DB ?? "3";

const clearScript =
  "local k = redis.call('KEYS', ARGV[1]) " +
  "for i = 1, #k, 500 do redis.call('DEL', unpack(k, i, math.min(i + 499, #k))) end " +
  "return #k";

function command(...parts: string[]): string {
  return `*${parts.length}\r\n` + parts.map((p) => `$${Buffer.byteLength(p)}\r\n${p}\r\n`).join("");
}

/** Deletes every rate-limit counter in the development database and returns how many there were. */
export function clearRateLimits(): Promise<number> {
  return new Promise((resolve, reject) => {
    const socket = net.createConnection({ host, port });
    let received = "";
    socket.setTimeout(5000, () => socket.destroy(new Error(`Redis at ${host}:${port} did not answer.`)));
    socket.on("error", reject);
    socket.on("connect", () => socket.write(command("SELECT", db) + command("EVAL", clearScript, "0", "rl:*")));
    socket.on("data", (chunk) => {
      received += chunk.toString("utf8");
      const lines = received.split("\r\n");
      if (lines.length < 3) return;
      socket.end();
      if (lines[0] !== "+OK") return reject(new Error(`SELECT ${db}: ${lines[0]}`));
      if (!lines[1].startsWith(":")) return reject(new Error(`Clearing rate limits: ${lines[1]}`));
      resolve(Number(lines[1].slice(1)));
    });
  });
}
