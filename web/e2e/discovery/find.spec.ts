import type { APIRequestContext, Page } from "@playwright/test";
import { approvedInstitution, colomboDate, slotOf, unlist, type Institution } from "../support/institutions";
import { expect, scrollsSideways, test, widerThanWindow } from "../support/test";
import { apiAs } from "../support/accounts";

/*  Plan 06, B.4: finding a place to give. A donor searches near them, sees the place in the list
    and on the map, picks a day on the month and books from the list; a filter that finds nothing
    says so in words, and the back button undoes it; the map searches where it was moved; location
    refused says what to do instead; the old directory's address and the menu lead here; Sinhala
    and Tamil, on a 320 px phone without a sideways scroll.

    E2E institutions are approved at the same place (Galle's coordinates, the first district in the
    lookups), so every search here asserts that the test's own place is among the results, never
    that it is the only one.                                                                     */

const here = { latitude: 6.0329, longitude: 80.2168 };
const near = "6.033%2C80.217";

let institution: Institution;

test.describe.configure({ mode: "serial" });

test.beforeAll(async ({ baseURL }) => {
  test.setTimeout(180_000);
  institution = await approvedInstitution(baseURL!, `E2E Find Vihara ${Date.now()}`);
  await slotOf(baseURL!, institution, { name: "Lunch" });

  const owner = await apiAs(baseURL!, institution.owner);
  try {
    expect((await owner.put("/api/institution/listing", { data: { listed: true } })).ok()).toBe(true);
  } finally {
    await owner.dispose();
  }
});

test.afterAll(async ({ baseURL }) => {
  await unlist(baseURL!, institution);
});

/**
 * The geo index learns of a listing from the outbox, which the API's job runs every minute (G5):
 * until then a search near a point does not find it yet. Wait for it, through the real index.
 */
async function findableNearby(request: APIRequestContext) {
  await expect
    .poll(
      async () => {
        const response = await request.get(`/api/search?date=${colomboDate(1)}&near=${near}&radiusKm=5`);
        const body = (await response.json()) as { results: { slug: string }[] };
        return body.results.some((r) => r.slug === institution.slug);
      },
      { timeout: 120_000, intervals: [2_000] },
    )
    .toBe(true);
}

const card = (page: Page) => page.locator("article").filter({ has: page.getByRole("link", { name: institution.name, exact: true }) });

test.describe("near me", () => {
  test.use({ geolocation: here, permissions: ["geolocation"] });

  test("a donor searches near them, sees the place in the list and on the map, picks a day and books it @phone", async ({ applicant, request }, testInfo) => {
    const { page } = applicant;
    const phone = testInfo.project.name === "phone";
    await findableNearby(request);

    await page.goto("/find");
    await expect(page.getByRole("heading", { name: "Find a place to give", level: 1 })).toBeVisible();
    await page.getByRole("button", { name: "Near me" }).click();
    await expect(page).toHaveURL(new RegExp(`near=${near}`));
    await expect(page.getByRole("button", { name: "Near me" })).toHaveAttribute("aria-pressed", "true");

    // In the list, with its open meal and how far it is.
    await expect(card(page)).toBeVisible();
    await expect(card(page).getByText(/km away/)).toBeVisible();
    await expect(card(page).getByText("Lunch")).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);

    // On the map, under the same number as its card. Switching the phone's tab is not a step the
    // back button undoes.
    const steps = await page.evaluate(() => history.length);
    const number = (await card(page).locator("span[aria-hidden]").first().textContent())!.trim();
    if (phone) await page.getByRole("tab", { name: "Map" }).click();
    await expect(page.locator(`.leaflet-marker-icon[title="${number}. ${institution.name}"]`)).toBeVisible();
    expect(await widerThanWindow(page)).toEqual([]);
    if (phone) await page.getByRole("tab", { name: "List" }).click();
    expect(await page.evaluate(() => history.length)).toBe(steps);

    // The month says how many are open each day; choosing another day searches it.
    const later = colomboDate(3);
    await page.locator(`button[data-date="${later}"]`).click();
    await expect(page).toHaveURL(new RegExp(`date=${later}`));
    await expect(card(page)).toBeVisible();

    // Book: the place's page opens on that meal and day, with the form already open.
    await card(page).getByRole("link", { name: `Book Lunch at ${institution.name}` }).click();
    await expect(page).toHaveURL(new RegExp(`/i/${institution.slug}\\?slot=.+&date=${later}`));
    const sheet = page.getByRole("dialog");
    await expect(sheet.getByRole("heading", { name: "Sponsor Lunch" })).toBeVisible();
    await sheet.getByRole("button", { name: "Send request" }).click();
    await expect(sheet.getByText("Request sent")).toBeVisible();
  });

  test("a time of day with nothing open says so in words, and back undoes it", async ({ page }) => {
    await page.goto(`/find?near=${near}&radius=5`);
    await expect(card(page)).toBeVisible();

    await page.getByRole("button", { name: "Evening" }).click();
    await expect(page).toHaveURL(/time=evening/);
    await expect(page.getByText(/No place has a meal you can sponsor on .+ here\./)).toBeVisible();
    await expect(card(page)).toHaveCount(0);

    await page.goBack();
    await expect(page).not.toHaveURL(/time=evening/);
    await expect(page.getByRole("button", { name: "Any time" })).toHaveAttribute("aria-pressed", "true");
    await expect(card(page)).toBeVisible();
  });

  test("moving the map offers to search there, and does", async ({ page }) => {
    await page.goto(`/find?near=${near}&radius=5`);
    const map = page.getByRole("region", { name: "Map of the places found" });
    await expect(page.locator(".leaflet-marker-icon").first()).toBeVisible();
    await expect(page.getByRole("button", { name: "Search this area" })).toBeHidden();

    // The mouse reaches only what is in the window, and the search's own place sits at the centre:
    // grab the map in view, beside the marker rather than on it.
    await map.scrollIntoViewIfNeeded();
    const box = (await map.boundingBox())!;
    const from = { x: box.x + box.width * 0.3, y: box.y + box.height * 0.7 };
    await page.mouse.move(from.x, from.y);
    await page.mouse.down();
    await page.mouse.move(from.x - 120, from.y - 60, { steps: 8 });
    await page.mouse.up();

    await page.getByRole("button", { name: "Search this area" }).click();
    await expect(page).toHaveURL(/near=\d+\.\d{1,3}%2C\d+\.\d{1,3}/);
    await expect(page).not.toHaveURL(new RegExp(`near=${near}`));
    await expect(page.getByRole("button", { name: "Search this area" })).toBeHidden();
  });
});

test.describe("location refused", () => {
  test.use({ permissions: [] });

  test("says what to do instead, and a district still finds the place", async ({ page }) => {
    await page.goto("/find");
    await page.getByRole("button", { name: "Near me" }).click();
    // In the page, not Next's route announcer (also an alert, empty until a navigation).
    await expect(page.getByRole("main").getByRole("alert")).toHaveText("Location is turned off for Sethika. Allow it in your browser, or choose a district.");
    await expect(page).not.toHaveURL(/near=/);

    await page.getByRole("button", { name: "In a district" }).click();
    await page.getByLabel("District").selectOption("AMP");
    await expect(page).toHaveURL(/district=AMP/);
    await expect(card(page)).toBeVisible();
    await expect(card(page).getByText(/km away/)).toHaveCount(0);

    // A district with no place listed in it.
    await page.getByLabel("District").selectOption("MLT");
    await expect(page.getByText(/No place has a meal you can sponsor on .+ here\./)).toBeVisible();
  });
});

test("two taps before the page has redrawn both count", async ({ page }) => {
  // A slow phone draws the first tap after the second has landed: the second must start from the
  // address the first left, not the one the page last drew. Both in one task, so React draws neither.
  // Day 3 is the day the first test books, and its only place.
  const later = colomboDate(5);
  await page.goto("/find?district=AMP");
  await expect(card(page)).toBeVisible();
  await expect(page.locator(`button[data-date="${later}"]`)).toBeEnabled();

  await page.evaluate((date) => {
    const midday = Array.from(document.querySelectorAll("button")).find((b) => b.textContent === "Midday")!;
    midday.click();
    document.querySelector<HTMLButtonElement>(`button[data-date="${date}"]`)!.click();
  }, later);
  await expect(page).toHaveURL(new RegExp(`date=${later}&time=midday&district=AMP`));
  await expect(page.getByRole("button", { name: "Midday" })).toHaveAttribute("aria-pressed", "true");
});

test("the old directory and the menu lead to the search", async ({ applicant, request }) => {
  const { page } = applicant;
  // Straight to the search in the same language: one permanent redirect, not two hops through the
  // language cookie.
  for (const [from, to] of [["/institutions", "/find"], ["/si/institutions", "/si/find"], ["/ta/institutions", "/ta/find"]]) {
    const response = await request.get(from, { maxRedirects: 0 });
    expect(response.status(), from).toBe(308);
    expect(new URL(response.headers().location!, "http://x").pathname, from).toBe(to);
  }
  await page.goto("/si/institutions");
  await expect(page).toHaveURL(/\/si\/find$/);

  // Every way the donor's home offers to find a place goes to the search.
  await page.goto("/");
  const links = page.getByRole("link", { name: "Find a place to give" });
  await expect(links.first()).toBeVisible();
  expect(await links.count()).toBeGreaterThan(1);
  for (const link of await links.all()) await expect(link).toHaveAttribute("href", "/find");
});

for (const [locale, title, evening] of [
  ["si", "දීමට ස්ථානයක් සොයන්න", "සවස"],
  ["ta", "வழங்க ஓர் இடத்தைக் கண்டறியுங்கள்", "மாலை"],
] as const) {
  test(`in ${locale}, the search reads in its language and fits a phone @phone`, async ({ page }) => {
    await page.goto(`/${locale}/find?district=AMP`);
    await expect(page.getByRole("heading", { name: title, level: 1 })).toBeVisible();
    await expect(page.getByRole("button", { name: evening })).toBeVisible();
    await expect(card(page)).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);
    expect(await widerThanWindow(page)).toEqual([]);
  });
}
