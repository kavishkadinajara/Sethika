import { mkdirSync } from "node:fs";
import path from "node:path";
import { createAccount, signIn } from "../support/accounts";
import { expect, scrollsSideways, test } from "../support/test";

/*  Sinhala and Tamil words run longer than English and wrap differently, so each screen is
    opened in all three languages: no missing message (next-intl reports those as console
    errors, which the fixture fails on), nothing wider than the window, and a full-page
    screenshot of each kept under e2e/.screens for a person - or Claude - to look at.

    The account is made in the language under test: signing in lands a person in their own
    account's language (A11), so an English account would see English on /si/apply too.     */

// Outside outputDir, which Playwright empties at the start of every run.
const screens = path.join(__dirname, "..", ".screens");
const englishTitle = "Register an institution";

for (const locale of ["en", "si", "ta"] as const) {
  test(`the application screens in ${locale} @phone`, async ({ page, baseURL }, testInfo) => {
    mkdirSync(screens, { recursive: true });
    const account = await createAccount(baseURL!, "E2E Language Check", locale);
    const prefix = locale === "en" ? "" : `/${locale}`;
    await signIn(page, account, locale === "en" ? "" : locale);

    const shot = (name: string) =>
      page.screenshot({ path: path.join(screens, `${testInfo.project.name}-${locale}-${name}.png`), fullPage: true });

    await page.goto(`${prefix}/apply`);
    await expect(page.locator("html")).toHaveAttribute("lang", locale);
    await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
    if (locale !== "en") await expect(page.getByRole("heading", { name: englishTitle })).toBeHidden();
    await page.waitForLoadState("networkidle");
    expect(await scrollsSideways(page)).toBe(false);
    // Nothing cut off inside a control: the theme switcher's pills hold the longest words.
    const clipped = await page.locator("label, a, button").evaluateAll((elements) =>
      elements.filter((e) => e.scrollWidth > e.clientWidth + 1 && getComputedStyle(e).overflow !== "visible").map((e) => e.textContent),
    );
    expect(clipped).toEqual([]);
    const crowded = await page.locator("fieldset label").evaluateAll((labels) =>
      labels.filter((l) => l.scrollWidth > l.clientWidth + 1).map((l) => l.textContent),
    );
    expect(crowded, "theme labels wider than their pill").toEqual([]);
    await shot("list");

    await page.goto(`${prefix}/apply/new`);
    await expect(page.locator("select").first()).toBeVisible();
    await page.waitForLoadState("networkidle");
    expect(await scrollsSideways(page)).toBe(false);
    await shot("details");

    // Submitting nothing shows every error at once: the longest the form gets.
    await page.locator('button[type="submit"]').click();
    await expect(page.locator('[aria-invalid="true"]').first()).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);
    await shot("details-errors");
  });
}
