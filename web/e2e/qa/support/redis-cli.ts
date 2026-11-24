import { execFileSync } from "node:child_process";

/*  Raw Redis commands on the QA db, for the cases that need a clock: a code's TTL is how the API
    measures its lifetime and the resend cooldown, so shortening a TTL is how a case "waits five
    minutes" without waiting. redis-cli comes with the Windows Redis service.               */

const cli = process.env.QA_REDIS_CLI ?? "C:\\Program Files\\Redis\\redis-cli.exe";

export function redis(...args: string[]): string {
  return execFileSync(cli, ["-h", process.env.E2E_REDIS_HOST!, "-p", process.env.E2E_REDIS_PORT!, "-n", process.env.E2E_REDIS_DB!, ...args], { encoding: "utf8" }).trim();
}

/** +94XXXXXXXXX for a local 07X number. */
export const e164 = (local: string) => `+94${local.replace(/\D/g, "").slice(-9)}`;
