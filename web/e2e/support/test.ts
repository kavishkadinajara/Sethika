import { test as base, expect, type Page } from "@playwright/test";
import { createAccount, signIn, type Account } from "./accounts";
import { clearRateLimits } from "./redis";

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
  /** Clears the rate-limit counters before each test (see below). */
  freshLimits: void;
};

export const test = base.extend<Fixtures>({
  /*  Every sign-in, step-up and texted code from the screens comes from 127.0.0.1, so a full
      run (two projects, ~55 tests) passes the per-client limits mid-run and fails on 429s that
      have nothing to do with the screens. The global setup clears the counters once; this
      clears them before every test too. The limits are covered by the API's integration tests. */
  freshLimits: [
    async ({}, provide) => {
      await clearRateLimits();
      await provide();
    },
    { auto: true },
  ],

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

/**
 * The innermost elements that reach past the window's right edge: what makes `scrollsSideways`
 * true, named, so a failure says which element to fix. Hidden and fixed ones are left out.
 */
export async function widerThanWindow(page: Page): Promise<string[]> {
  return page.evaluate(() => {
    const edge = document.documentElement.clientWidth + 1;
    // A fixed box (the toasts' region, a sheet) sits on the window and never makes the page scroll.
    const fixed = (element: HTMLElement | null): boolean =>
      element !== null && (getComputedStyle(element).position === "fixed" || fixed(element.parentElement));
    const past = Array.from(document.body.querySelectorAll<HTMLElement>("*")).filter((element) => {
      const box = element.getBoundingClientRect();
      return box.width > 1 && box.right > edge && getComputedStyle(element).visibility !== "hidden" && !fixed(element);
    });
    return past
      .filter((element) => !past.some((other) => other !== element && element.contains(other)))
      .map((element) => `<${element.tagName.toLowerCase()} class="${element.className}"> "${(element.textContent ?? "").trim().slice(0, 60)}"`);
  });
}

/**
 * Boxes that cut their own content off at the side: a card with `overflow-hidden` hides a badge
 * that does not fit, and the page itself never scrolls, so `scrollsSideways` cannot see it.
 * Deliberate clipping (an ellipsis, the 1 px screen-reader-only copies) is left out, and so are
 * text fields: a value longer than its box scrolls inside it, which is how a field works.
 * `within` (a CSS selector) narrows the check to part of the page, such as the sidebar.
 */
export async function clipsSideways(page: Page, within?: string): Promise<string[]> {
  return page.evaluate((scope) =>
    Array.from(scope ? document.querySelectorAll(scope) : [document.body])
      .flatMap((root) => Array.from(root.querySelectorAll<HTMLElement>("*")))
      .filter((element) => {
        const style = getComputedStyle(element);
        return (
          !(element instanceof HTMLInputElement || element instanceof HTMLTextAreaElement) &&
          style.overflowX !== "visible" &&
          style.textOverflow !== "ellipsis" &&
          element.clientWidth > 1 &&
          element.scrollWidth > element.clientWidth + 1
        );
      })
      .map((element) => `<${element.tagName.toLowerCase()}> "${(element.textContent ?? "").trim().slice(0, 60)}"`),
    within,
  );
}
