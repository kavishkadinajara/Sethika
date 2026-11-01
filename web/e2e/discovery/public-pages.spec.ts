import type { APIRequestContext } from "@playwright/test";
import { apiAs } from "../support/accounts";
import { approvedInstitution, slotOf, unlist, type Institution } from "../support/institutions";
import { rateLimitCount } from "../support/redis";
import { sql } from "../support/sql";
import { expect, test } from "../support/test";

/*  Plan 06, G9: what a search engine is given by the server, before any script runs - the name,
    address, meals and description in the HTML, a title and snippet, the page in three languages,
    schema.org data, a 404 for a place that is not there, the sitemap and robots.txt. Raw HTTP
    (Playwright's request), so nothing here is what JavaScript drew. The last test opens the page
    in the browser: hydrating the server's HTML must raise no error, and the institution is not
    fetched a second time.                                                                        */

const site = process.env.E2E_WEB_URL ?? "http://localhost:3000";
const schemaTypes: Record<string, string> = { TEMPLE: "BuddhistTemple", KOVIL: "HinduTemple", MOSQUE: "Mosque", CHURCH: "Church" };
const description = "A temple by the lake. </script><b>not markup</b>";

let institution: Institution;
let type: string;

test.describe.configure({ mode: "serial" });

test.beforeAll(async ({ baseURL }) => {
  institution = await approvedInstitution(baseURL!, `E2E Lotus Pond ${Date.now()}`);
  await slotOf(baseURL!, institution, { name: "Noon meal" });

  // A description an institution might write, markup and all: it must reach the page as text.
  sql(`UPDATE org.Institutions SET Description = N'${description}' WHERE Slug = '$(slug)';`, { slug: institution.slug });

  const owner = await apiAs(baseURL!, institution.owner);
  try {
    const listed = await owner.put("/api/institution/listing", { data: { listed: true } });
    expect(listed.ok()).toBe(true);
    type = ((await (await owner.get(`/api/institutions/${institution.slug}`)).json()) as { type: string }).type;
  } finally {
    await owner.dispose();
  }
});

test.afterAll(async ({ baseURL }) => {
  await unlist(baseURL!, institution);
});

async function html(request: APIRequestContext, path: string, status = 200): Promise<string> {
  const response = await request.get(path);
  expect(response.status(), path).toBe(status);
  return response.text();
}

test("the server's HTML names the place, its meals and its languages, and describes it for search engines", async ({ request }) => {
  const page = await html(request, `/si/i/${institution.slug}`);
  const path = `/i/${institution.slug}`;

  expect(page).toContain(`<title>${institution.name} · Sethika</title>`);
  expect(page).toMatch(new RegExp(`<h1[^>]*>${institution.name}</h1>`));
  expect(page).toContain("Noon meal");
  expect(page).toContain(`<link rel="canonical" href="${site}/si${path}"/>`);
  for (const [language, href] of [["en", path], ["si", `/si${path}`], ["ta", `/ta${path}`], ["x-default", path]]) {
    expect(page).toContain(`<link rel="alternate" hrefLang="${language}" href="${site}${href}"/>`);
  }
  expect(page).toContain(`<meta property="og:url" content="${site}/si${path}"/>`);

  // The description is text, in the snippet and on the page: never markup.
  expect(page).toContain('<meta name="description" content="A temple by the lake. &lt;/script&gt;&lt;b&gt;not markup&lt;/b&gt;"/>');
  expect(page).not.toContain("<b>not markup</b>");

  const scripts = [...page.matchAll(/<script type="application\/ld\+json">(.*?)<\/script>/g)].map((m) => m[1]);
  expect(scripts).toHaveLength(1);
  expect(scripts[0]).not.toContain("<");
  const data = JSON.parse(scripts[0]);
  expect(data).toMatchObject({
    "@context": "https://schema.org",
    "@type": schemaTypes[type] ?? "Place",
    name: institution.name,
    description,
    url: `${site}/si${path}`,
    address: { "@type": "PostalAddress", addressCountry: "LK" },
    geo: { "@type": "GeoCoordinates" },
  });
});

test("the server reads the place on the visitor's own budget, not the web server's", async ({ request }) => {
  // The address the nearest proxy wrote last; anything earlier in the header is the client's own claim.
  const visitor = `10.${Date.now() % 250}.${Math.floor(Math.random() * 250)}.7`;
  const response = await request.get(`/i/${institution.slug}`, { headers: { "x-forwarded-for": `203.0.113.9, ${visitor}` } });
  expect(response.status()).toBe(200);

  expect(await rateLimitCount("institution-read-client", visitor)).toBe(1);
  expect(await rateLimitCount("institution-read-client", "203.0.113.9")).toBe(0);
});

test("a place that is not there is a 404, in the page's own words", async ({ request }) => {
  const page = await html(request, `/i/no-such-place-${Date.now()}`, 404);
  expect(page).toContain("We could not find this place");
  expect(page).not.toContain('type="application/ld+json"');
});

test("the sitemap lists a listed place in every language, and robots.txt keeps out of the signed-in screens", async ({ request, baseURL }) => {
  const path = `/i/${institution.slug}`;
  const sitemap = await html(request, "/sitemap.xml");
  for (const prefix of ["", "/si", "/ta"]) {
    expect(sitemap).toContain(`<loc>${site}${prefix}${path}</loc>`);
    expect(sitemap).toContain(`<loc>${site}${prefix}/find</loc>`);
  }
  expect(sitemap).toContain(`<xhtml:link rel="alternate" hreflang="ta" href="${site}/ta${path}" />`);

  const robots = await html(request, "/robots.txt");
  expect(robots).toContain("Disallow: /si/bookings");
  expect(robots).toContain(`Sitemap: ${site}/sitemap.xml`);
  expect(robots).not.toMatch(/Disallow: \/i\b/);

  // Unlisted, it leaves the sitemap; its page stays reachable by its link (C10).
  await unlist(baseURL!, institution);
  expect(await html(request, "/sitemap.xml")).not.toContain(path);
  await html(request, path);
});

test("in Sinhala and Tamil the server's page hydrates as it is, cost and all", async ({ page }) => {
  // Node knows Sinhala's currency format and Chromium does not: a cost formatted by Intl read
  // "රු. 450" from the server and "LKR 450" in the browser, and React threw the page away.
  // The problems fixture fails the test on the error React raises when hydration fails.
  for (const [locale, cost] of [["si", "රු. 450"], ["ta", "ரூ. 450"]]) {
    await page.goto(`/${locale}/i/${institution.slug}`);
    await expect(page.getByText(cost)).toBeVisible();
    await expect(page.getByRole("radio").first()).toBeChecked();
  }
});

test("in the browser the page hydrates without an error and does not fetch the place again", async ({ page }) => {
  const fetched: string[] = [];
  page.on("request", (r) => {
    if (r.url().includes("/api/institutions/")) fetched.push(new URL(r.url()).pathname);
  });

  await page.goto(`/i/${institution.slug}`);
  await expect(page.getByRole("heading", { level: 1, name: institution.name })).toBeVisible();
  await expect(page.getByRole("radio", { name: /Noon meal/ })).toBeChecked();
  await expect(page.getByRole("heading", { name: "Choose a day" })).toBeVisible();

  // The month of open days is the browser's to fetch; the institution itself came with the HTML.
  await expect.poll(() => fetched.some((p) => p.endsWith("/availability"))).toBe(true);
  expect(fetched.filter((p) => p === `/api/institutions/${institution.slug}`)).toEqual([]);
});
