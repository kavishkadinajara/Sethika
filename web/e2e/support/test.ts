import { test as base, expect, type Page } from "@playwright/test";
import { createAccount, signIn, type Account } from "./accounts";

/*  Every test also fails on what a person would not notice but a release must not have: an
    uncaught exception, an error in the console (React, next-intl's missing messages), or a 5xx
    from the API. The browser logs each 4xx fetch as "Failed to load resource"; those are the
    API refusing something on purpose - validation, a stale write - and the test asserts what
    the screen does with them, so they are not counted.                                      */

type Fixtures = {
  /** A new donor account, signed in, on a page that has not been navigated yet. */
  applicant: { page: Page; account: Account };
  /** Problems collected while the test ran; checked automatically at the end. */
  problems: string[];
};

export const test = base.extend<Fixtures>({
  problems: [
    async ({ page }, provide) => {
      const problems: string[] = [];
      page.on("pageerror", (error) => problems.push(`page error: ${error.message}`));
      page.on("console", (message) => {
        if (message.type() !== "error") return;
        if (message.text().startsWith("Failed to load resource")) return;
        problems.push(`console: ${message.text()}`);
      });
      page.on("response", (response) => {
        if (response.status() >= 500) problems.push(`${response.status()} from ${response.request().method()} ${response.url()}`);
      });
      await provide(problems);
      expect(problems, "browser errors and 5xx responses").toEqual([]);
    },
    { auto: true },
  ],

  applicant: async ({ page, baseURL }, provide) => {
    const account = await createAccount(baseURL!);
    await signIn(page, account);
    await provide({ page, account });
  },
});

export { expect };

/** A small file the API accepts as a PDF: it checks the header, not the whole document. */
export const pdf = (name = "certificate.pdf") => ({
  name,
  mimeType: "application/pdf",
  buffer: Buffer.from("%PDF-1.4\n% Sethika browser test\n%%EOF\n"),
});

/** True when the page is wider than the window: something forces a sideways scroll. */
export async function scrollsSideways(page: Page): Promise<boolean> {
  return page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1);
}
