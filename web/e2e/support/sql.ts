import { execFileSync } from "node:child_process";

/*  The browser tests reach the database the API is using - the local development database, by
    default - with sqlcmd, which the SQL Server setup already installs (docs/LOCAL_SETUP.md), so
    there is no SQL client package in the web app for the sake of its tests.

    Every batch runs as the platform: row-level security would otherwise hide other users' rows
    and a cleanup would silently delete nothing. Values travel as sqlcmd variables, $(name), and
    are checked against a narrow pattern first, because sqlcmd substitutes them as text.        */

const server = process.env.E2E_SQL_SERVER ?? "localhost";
const database = process.env.E2E_SQL_DATABASE ?? "Sethika";
const safeValue = /^[\w+\-.@ ]{1,100}$/;

/** Runs T-SQL and returns the non-empty output lines, trimmed. */
export function sql(query: string, variables: Record<string, string> = {}): string[] {
  // -I: QUOTED_IDENTIFIER on, which writes to tables with filtered indexes need.
  const args = ["-S", server, "-d", database, "-E", "-C", "-I", "-b", "-h", "-1", "-W", "-w", "8000"];
  for (const [name, value] of Object.entries(variables)) {
    if (!safeValue.test(value)) throw new Error(`Refusing to pass ${name}=${JSON.stringify(value)} to sqlcmd.`);
    args.push("-v", `${name}=${value}`);
  }
  args.push("-Q", `SET NOCOUNT ON; EXEC sys.sp_set_session_context @key = N'IsPlatform', @value = 1; ${query}`);

  let output: string;
  try {
    output = execFileSync("sqlcmd", args, { encoding: "utf8" });
  } catch (error) {
    // sqlcmd prints SQL Server's message on stdout; without it the error says only "failed".
    const { stdout, stderr } = error as { stdout?: string; stderr?: string };
    throw new Error(`sqlcmd failed: ${`${stdout ?? ""}${stderr ?? ""}`.trim()}\n${query}`);
  }
  return output
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean);
}
