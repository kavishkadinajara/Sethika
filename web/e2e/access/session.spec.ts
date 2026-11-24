import { spawn } from "node:child_process";
import { sql } from "../support/sql";
import { expect, test } from "../support/test";

/*  A19, hardening 2026-11-08: leaving a page while its session refresh is in flight. The server
    rotates the refresh cookie; a fetch cancelled with the page never stores the successor, so
    the next page presents the spent token - refused 409 inside the ten-second grace, and after
    it taken for a replay that revokes the whole session. The rotation is sent with keepalive
    so it finishes and its cookie lands.

    A local refresh takes ~100 ms, less than the next page takes to replace the old one, so the
    race is made certain the way the API's tests make theirs: an update lock on the session's
    live token holds the rotation at its claim for three seconds, long enough to leave the page.
    Asserted in the database, where the outcome is not a matter of timing.                    */

const refreshPath = "/api/auth/session/refresh";
const server = process.env.E2E_SQL_SERVER ?? "localhost";
const database = process.env.E2E_SQL_DATABASE ?? "Sethika";

/** The live token of <last9>'s newest session family: the browser's (sign-up made another). */
const newestFamily = (alias: string) =>
  `${alias}.FamilyId = (SELECT TOP 1 n.FamilyId FROM auth.RefreshTokens n WHERE n.UserId = u.Id ORDER BY n.Id DESC)`;

/** Holds an update lock on the browser session's live token for <seconds>; resolves once it is held. */
async function holdLiveToken(last9: string, seconds: number): Promise<{ released: Promise<void> }> {
  const query = `SET NOCOUNT ON; EXEC sys.sp_set_session_context @key = N'IsPlatform', @value = 1;
    BEGIN TRAN;
    SELECT t.Id FROM auth.RefreshTokens t WITH (UPDLOCK, ROWLOCK) JOIN auth.Users u ON u.Id = t.UserId
    WHERE RIGHT(u.PhoneNumber, 9) = '$(last9)' AND t.RevokedAt IS NULL AND ${newestFamily("t")};
    WAITFOR DELAY '00:00:0${seconds}';
    COMMIT;`;
  const child = spawn("sqlcmd", ["-S", server, "-d", database, "-E", "-C", "-I", "-b", "-h", "-1", "-W", "-v", `last9=${last9}`, "-Q", query]);
  const released = new Promise<void>((resolve, reject) =>
    child.on("exit", (code) => (code === 0 ? resolve() : reject(new Error(`lock holder exited ${code}`)))),
  );
  // sqlcmd's output is buffered until it exits, so ask the server instead: an update lock on a key
  // of auth.RefreshTokens, held by another session.
  for (let waited = 0; waited < 5_000; waited += 100) {
    const [held] = sql(
      `SELECT COUNT(*) FROM sys.dm_tran_locks
       WHERE resource_database_id = DB_ID() AND resource_type = 'KEY' AND request_mode = 'U'
         AND resource_associated_entity_id IN (SELECT hobt_id FROM sys.partitions WHERE object_id = OBJECT_ID('auth.RefreshTokens'));`,
    );
    if (Number(held) > 0) return { released };
    await new Promise((r) => setTimeout(r, 100));
  }
  throw new Error("The update lock was never seen.");
}

test("leaving a page during its session refresh keeps the person signed in", async ({ applicant: { page, account } }) => {
  const last9 = account.phone.replace(/\D/g, "").slice(-9);
  const tokens = (where: string) =>
    Number(
      sql(
        `SELECT COUNT(*) FROM auth.RefreshTokens t JOIN auth.Users u ON u.Id = t.UserId
         WHERE RIGHT(u.PhoneNumber, 9) = '$(last9)' AND ${where};`,
        { last9 },
      )[0],
    );

  // A settled page first, so the browser's session exists and holds one live token.
  await page.goto("/");
  await page.waitForLoadState("networkidle");

  // Each load starts with a refresh (the access token lives in memory only). With the rotation held
  // at its claim, leave the page while that request waits, then let it go. The refresh left behind
  // must still be carried out: here the dev proxy passes a cancelled request's abort on and the API
  // rolls the claim back, but a proxy that does not (production's, for all we know) lets the server
  // rotate and drops the cookie on the floor - the A19 sign-out. So each round is two rotations: the
  // left page's, then the next page's, which meets it as an ordinary superseded race and retries.
  const rotated = () => tokens(`${newestFamily("t")} AND t.RevokedReason = 'Rotated'`);
  for (let i = 0; i < 2; i++) {
    const before = rotated();
    const lock = await holdLiveToken(last9, 4);
    const sent = page.waitForRequest((r) => r.url().endsWith(refreshPath) && r.method() === "POST");
    await page.goto("/", { waitUntil: "commit" });
    await sent;
    await page.goto("/", { waitUntil: "commit" });
    await lock.released;
    await page.waitForLoadState("networkidle");
    await expect.poll(rotated).toBe(before + 2);
  }

  // Outlive the grace: a spent token sent now would be a replay, and revoke the family.
  await page.waitForTimeout(11_000);

  const answered = page.waitForResponse((r) => r.url().endsWith(refreshPath));
  await page.goto("/");
  expect((await answered).status()).toBe(200);

  expect(tokens("t.RevokedReason = 'ReuseDetected'")).toBe(0);
  expect(tokens(`${newestFamily("t")} AND t.RevokedReason = 'Rotated'`)).toBeGreaterThan(0);
  expect(tokens(`${newestFamily("t")} AND t.RevokedAt IS NULL`)).toBe(1);
});
