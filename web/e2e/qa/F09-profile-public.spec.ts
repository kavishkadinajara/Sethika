import si from "../../messages/si.json";
import { signIn } from "../support/accounts";
import { sql } from "../support/sql";
import { expect, scrollsSideways, test } from "../support/test";
import { anonymous } from "./support/api";
import {
  colomboDate,
  internalIdKeys,
  isListed,
  LISTING,
  makeSlot,
  SLOTS,
  Sessions,
  slotName,
  slotsOf,
} from "./support/booking";
import { account, answer, evidence, tenant } from "./support/world";

/*  F09 - Institution profile and public page /i/[slug] (P2). Cases: docs/qa/cases/F09-profile-public.md.
    What an institution can change about its public page after approval is its meals and its
    listing: editing the profile itself (name, address, description) was deliberately not built
    (DECISIONS, onboarding "what was cut"), so that part of QA-F09-02 is N/A (OBS-10). Every case
    puts T1's listing back as it found it and switches off the meals it made.                   */

const t1 = () => tenant("T1");
const t4 = () => tenant("T4");

/** T1's private columns, which the reviewer saw and the public must not (plan 03, E23). */
function privateOf(slug: string) {
  const [row] = sql(
    "SELECT CONCAT(Id, '|', InstitutionCode, '|', Phone, '|', OfficialRegistrationNo) FROM org.Institutions WHERE Slug = '$(s)';",
    { s: slug },
  );
  const [id, code, phone, registrationNo] = row.split("|");
  return { id, code, phone, registrationNo };
}

test("QA-F09-01 a visitor reads T1's public page in English and Sinhala: what was published, nothing the reviewer was told @phone", async ({ page }, testInfo) => {
  const sessions = new Sessions();
  const slots: string[] = [];
  try {
    const owner = await sessions.of("O1");
    const lunch = await makeSlot(owner, { name: slotName("F09 Lunch"), nameSi: "QA දිවා දානය" });
    slots.push(lunch.publicId);
    const hidden = privateOf(t1().slug!);

    for (const locale of ["", "si"] as const) {
      await page.goto(`${locale ? `/${locale}` : ""}/i/${t1().slug}`);
      await expect(page.getByRole("heading", { name: t1().name, level: 1 })).toBeVisible();
      await expect(page.getByText("11 Temple Road").first()).toBeVisible();
      // The meal in the page's language: its Sinhala name on /si, the English one otherwise.
      await expect(page.getByRole("radio", { name: new RegExp(locale ? "QA දිවා දානය" : lunch.name) })).toBeVisible();
      const html = await page.content();
      for (const secret of [hidden.code, hidden.phone, hidden.phone.slice(3), hidden.registrationNo]) expect(html, `${locale} page carries ${secret}`).not.toContain(secret);
      expect(await scrollsSideways(page), `${locale} page`).toBe(false);
      await evidence(page, "QA-F09-01", testInfo, locale || "en");
    }

    // The API the page reads: what E23 lists, no internal id, no phone, e-mail or registration.
    const visitor = await anonymous();
    try {
      const r = await answer(await visitor.get(`/api/institutions/${t1().slug}`));
      expect(r.status).toBe(200);
      expect(internalIdKeys(r.body)).toEqual([]);
      const text = JSON.stringify(r.body);
      for (const secret of [hidden.code, hidden.phone, hidden.registrationNo]) expect(text).not.toContain(secret);
      for (const key of ["phone", "email", "officialRegistrationNo", "institutionCode", "status"]) expect(r.body, key).not.toHaveProperty(key);
      const month = colomboDate(2).slice(0, 7);
      const availability = await answer(await visitor.get(`/api/institutions/${t1().slug}/availability?month=${month}`));
      expect(availability.status).toBe(200);
      expect(internalIdKeys(availability.body)).toEqual([]);
    } finally {
      await visitor.dispose();
    }
  } finally {
    const owner = await sessions.of("O1");
    for (const slot of slots) await owner.post(`${SLOTS}/${slot}/deactivate`);
    await sessions.dispose();
  }
});

test("QA-F09-02 what O1 changes on the meals shows on the public page at once; the profile itself has no edit (N/A)", async ({ page, browser }) => {
  const sessions = new Sessions();
  const slots: string[] = [];
  try {
    const owner = await sessions.of("O1");
    const name = slotName("F09 Evening");
    const meal = await makeSlot(owner, { name, nameSi: "QA රාත්‍රී දානය" });
    slots.push(meal.publicId);

    await page.goto(`/si/i/${t1().slug}`);
    await expect(page.getByRole("radio", { name: /QA රාත්‍රී දානය/ })).toBeVisible();

    // Renamed (with the version just read): the next look shows the new names.
    const read = (await slotsOf(owner)).find((s) => s.publicId === meal.publicId)!;
    const renamed = `${name} renamed`;
    const put = await answer(
      await owner.put(`${SLOTS}/${meal.publicId}`, {
        data: {
          name: renamed, nameSi: "QA සවස දානය", nameTa: null, startTime: "18:00", endTime: "19:30", daysOfWeekMask: 127,
          bookingMode: "Exclusive", maxSponsors: null, minHeadcount: 1, maxHeadcount: 60, estimatedCostPerHead: 450,
          minAdvanceDays: 1, maxAdvanceDays: 120, requiresApproval: true, allowedFulfilment: ["DonorBrings"], sortOrder: null,
          version: read.version,
        },
      }),
    );
    expect(put.status).toBe(200);
    await page.reload();
    await expect(page.getByRole("radio", { name: /QA සවස දානය/ })).toBeVisible();
    await expect(page.getByRole("radio", { name: /QA රාත්‍රී දානය/ })).toHaveCount(0);
    // In English, in a browser of its own (the first page remembers its Sinhala choice).
    const english = await (await browser.newContext()).newPage();
    await english.goto(`/i/${t1().slug}`);
    await expect(english.getByRole("radio", { name: new RegExp(renamed) })).toBeVisible();

    // Switched off: gone from the page and from its API.
    expect((await owner.post(`${SLOTS}/${meal.publicId}/deactivate`)).status()).toBe(200);
    await english.reload();
    await expect(english.getByRole("heading", { name: t1().name, level: 1 })).toBeVisible();
    await expect(english.getByRole("radio", { name: new RegExp(renamed) })).toHaveCount(0);
    await english.context().close();
    const visitor = await anonymous();
    try {
      const body = (await (await visitor.get(`/api/institutions/${t1().slug}`)).json()) as { slots: { publicId: string }[] };
      expect(body.slots.map((s) => s.publicId)).not.toContain(meal.publicId);
    } finally {
      await visitor.dispose();
    }

    // The profile: the catalogue has INSTITUTION.PROFILE.EDIT, but no route or screen uses it (OBS-10).
    const profile = await owner.put("/api/institution/profile", { data: { description: "QA" } });
    expect(profile.status()).toBe(404);
  } finally {
    const owner = await sessions.of("O1");
    for (const slot of slots) await owner.post(`${SLOTS}/${slot}/deactivate`);
    await sessions.dispose();
  }
});

test("QA-F09-03 unlisted: reachable by its link only; listed: in the list, the search and the sitemap; a listing needs a meal", async ({ page, baseURL }) => {
  const sessions = new Sessions();
  const slots: string[] = [];
  const wasListed = isListed("T1");
  const listed = async () => {
    const visitor = await anonymous();
    try {
      const list = (await (await visitor.get("/api/institutions/")).json()) as { slug: string }[];
      const search = (await (await visitor.get(`/api/search/?date=${colomboDate(3)}&district=KDY`)).json()) as { results: { slug: string }[] };
      const sitemap = await (await fetch(`${baseURL}/sitemap.xml`)).text();
      return {
        list: list.some((i) => i.slug === t1().slug),
        search: search.results.some((i) => i.slug === t1().slug),
        sitemap: sitemap.includes(`/i/${t1().slug}`),
      };
    } finally {
      await visitor.dispose();
    }
  };
  try {
    const owner = await sessions.of("O1");
    // T1 has no meal switched on after setup: a listing is refused until it has one (C10).
    if ((await slotsOf(owner)).every((s) => !s.isActive)) {
      expect(await answer(await owner.put(LISTING, { data: { listed: true } }))).toMatchObject({ status: 409, body: { code: "no_active_slot" } });
    }
    expect(await answer(await owner.put(LISTING, { data: {} }))).toMatchObject({ status: 400, body: { code: "invalid_details", fields: ["listed"] } });

    const meal = await makeSlot(owner, { name: slotName("F09 Listed lunch") });
    slots.push(meal.publicId);
    expect((await owner.put(LISTING, { data: { listed: false } })).status()).toBe(204);
    expect(await listed()).toEqual({ list: false, search: false, sitemap: false });
    // ...and still open to anyone with its link (plan 03: "absent from the list but reachable by slug").
    await page.goto(`/i/${t1().slug}`);
    await expect(page.getByRole("heading", { name: t1().name, level: 1 })).toBeVisible();

    // O1 lists T1 on the slots screen, in Sinhala.
    await signIn(page, account("O1"), "si");
    await page.goto("/si/bookings/slots");
    const toggle = page.getByRole("switch");
    await expect(toggle).toHaveAttribute("aria-checked", "false");
    await expect(page.getByText(si.Slots.listing.unlistedHint).first()).toBeVisible();
    await toggle.click();
    await expect(toggle).toHaveAttribute("aria-checked", "true");
    await expect(page.getByText(si.Slots.listing.listedHint).first()).toBeVisible();
    await expect.poll(listed, { timeout: 20_000 }).toEqual({ list: true, search: true, sitemap: true });

    // Unlisted again: gone from all three (the geo index follows through the outbox).
    await toggle.click();
    await expect(toggle).toHaveAttribute("aria-checked", "false");
    await expect.poll(listed, { timeout: 20_000 }).toEqual({ list: false, search: false, sitemap: false });
  } finally {
    const owner = await sessions.of("O1");
    if (wasListed) await owner.put(LISTING, { data: { listed: true } });
    for (const slot of slots) await owner.post(`${SLOTS}/${slot}/deactivate`);
    if (!wasListed) await owner.put(LISTING, { data: { listed: false } });
    await sessions.dispose();
  }
});

test("QA-F09-04 a suspended institution (T4) is not public, even marked listed, and takes no booking", async ({ page }) => {
  const sessions = new Sessions();
  const slug = t4().slug!;
  const setStatus = (status: string) =>
    sql("UPDATE org.Institutions SET Status = '$(st)', PermissionsVersion = PermissionsVersion + 1 WHERE Slug = '$(s)';", { st: status, s: slug });
  const setListed = (on: string) => sql("UPDATE org.Institutions SET IsPubliclyListed = $(on) WHERE Slug = '$(s)';", { on, s: slug });

  // A meal T4 offered before it was suspended: let back for the moment (no product path, OBS-02).
  setStatus("Active");
  let meal = "";
  try {
    const owner = await sessions.of("O4");
    meal = (await makeSlot(owner, { name: slotName("F09 T4 lunch") })).publicId;
    expect((await owner.put(LISTING, { data: { listed: true } })).status()).toBe(204);
  } finally {
    setStatus("Suspended");
    await sessions.dispose();
  }
  try {
    expect(sql("SELECT CAST(IsPubliclyListed AS int) FROM org.Institutions WHERE Slug = '$(s)';", { s: slug })[0]).toBe("1");
    const visitor = await anonymous();
    try {
      expect(await answer(await visitor.get(`/api/institutions/${slug}`))).toMatchObject({ status: 404, body: { code: "institution_not_found" } });
      expect(await answer(await visitor.get(`/api/institutions/${slug}/availability?month=${colomboDate(2).slice(0, 7)}`))).toMatchObject({
        status: 404,
        body: { code: "institution_not_found" },
      });
      const list = (await (await visitor.get("/api/institutions/")).json()) as { slug: string }[];
      expect(list.map((i) => i.slug)).not.toContain(slug);
      const search = (await (await visitor.get(`/api/search/?date=${colomboDate(3)}&district=GLL`)).json()) as { results: { slug: string }[] };
      expect(search.results.map((i) => i.slug)).not.toContain(slug);
    } finally {
      await visitor.dispose();
    }
    const response = await page.goto(`/i/${slug}`);
    expect(response?.status()).toBe(404);

    // A donor holding the meal's id cannot book it; O4 cannot change the listing or the meals.
    const d1 = await sessions.of("D1");
    expect(await answer(await d1.post("/api/bookings", { data: { mealSlot: meal, date: colomboDate(5), headcount: 10, fulfilment: "DonorBrings" } }))).toMatchObject({
      status: 409,
      body: { code: "institution_not_active" },
    });
    const o4 = await sessions.of("O4");
    const listing = await o4.put(LISTING, { data: { listed: false } });
    const slots = await o4.get(SLOTS);
    expect([listing.status(), slots.status()]).toEqual([403, 403]);
  } finally {
    setListed("0");
    sql("UPDATE booking.MealSlots SET IsActive = 0 WHERE PublicId = '$(p)';", { p: meal });
    await sessions.dispose();
  }
});

test("QA-F09-05 the public pages are addressed by slug, never by an internal id", async ({ page }) => {
  const hidden = privateOf(t1().slug!);
  for (const wrong of [hidden.id, hidden.code, t1().slug!.toUpperCase().replaceAll("-", "_")]) {
    const response = await page.goto(`/i/${encodeURIComponent(wrong)}`);
    expect(response?.status(), wrong).toBe(404);
  }
  const visitor = await anonymous();
  try {
    expect((await visitor.get(`/api/institutions/${hidden.id}`)).status()).toBe(404);
    expect((await visitor.get(`/api/institutions/${"x".repeat(151)}`)).status()).toBe(404);
    const list = (await (await visitor.get("/api/institutions/")).json()) as unknown[];
    expect(internalIdKeys(list)).toEqual([]);
    const search = await (await visitor.get(`/api/search/?date=${colomboDate(3)}`)).json();
    expect(internalIdKeys(search)).toEqual([]);
  } finally {
    await visitor.dispose();
  }
  // Every link on the page goes by slug or public id: no "/i/<number>" or "?slot=<number>".
  await page.goto(`/i/${t1().slug}`);
  const hrefs = await page.locator("a[href]").evaluateAll((links) => links.map((a) => a.getAttribute("href") ?? ""));
  expect(hrefs.filter((h) => /\/i\/\d+(\b|$)|[?&](slot|institution|id)=\d+(\b|$)/.test(h))).toEqual([]);
});
