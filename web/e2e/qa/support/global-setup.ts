import type { FullConfig } from "@playwright/test";
import { warmUp } from "../../support/global-setup";
import { clearRateLimits } from "../../support/redis";
import { sql } from "../../support/sql";

/*  QA entry criteria (QA-PLAN section 9), checked before every QA run, not trusted from memory:
    the helpers are on Sethika_QA, the web app's /api proxy reaches an API that writes there, and
    no text in this database ever went through a real gateway (the console sender's ids start
    with "console-"). Then every route is opened once, as the e2e global setup does.          */
export default async function globalSetup(config: FullConfig) {
  const baseURL = config.projects.find((p) => p.name === "qa")!.use.baseURL!;

  const [database] = sql("SELECT DB_NAME();");
  if (database !== "Sethika_QA") throw new Error(`QA helpers are on ${database}, not Sethika_QA.`);

  // A sentinel number of its own each run: one number asked twice within a minute meets the cooldown.
  const sentinel = `077099${String(Math.floor(Math.random() * 10_000)).padStart(4, "0")}`;
  const [before] = sql("SELECT ISNULL(MAX(Id), 0) FROM notify.Notifications;");
  const response = await fetch(`${baseURL}/api/auth/otp/request`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ phoneNumber: sentinel, purpose: "signup", language: "en" }),
  });
  if (!response.ok) throw new Error(`Sentinel OTP through ${baseURL}: ${response.status} ${await response.text()}`);
  let arrived = "0";
  for (let i = 0; i < 20 && arrived === "0"; i++) {
    await new Promise((r) => setTimeout(r, 250));
    [arrived] = sql(`SELECT COUNT(*) FROM notify.Notifications WHERE Id > ${Number(before)} AND RIGHT(Recipient, 9) = '$(last9)';`, { last9: sentinel.slice(1) });
  }
  if (arrived === "0") throw new Error(`The web app at ${baseURL} proxies to an API that is not on Sethika_QA.`);

  const [real] = sql("SELECT COUNT(*) FROM notify.Notifications WHERE Channel = 'SMS' AND ProviderMessageId IS NOT NULL AND ProviderMessageId NOT LIKE 'console-%';");
  if (real !== "0") throw new Error(`${real} texts in Sethika_QA went through a real SMS gateway: set Sms:Provider = Console.`);

  await warmUp(baseURL);
  await clearRateLimits();
}
