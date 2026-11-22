import { mkdirSync } from "node:fs";
import path from "node:path";
import type { Browser, Locator, Page } from "@playwright/test";
import { createAccount, newPhone, signIn, type Account } from "../support/accounts";
import { field } from "../support/fields";
import { approvedInstitution, colomboDate, unlist } from "../support/institutions";
import { assign, leaderOf, requestBy, serviceTypeOf } from "../support/services";
import { clipsSideways, expect, scrollsSideways, test } from "../support/test";

/*  Phase E, 8.9: religious services, from the donor's, the institution's and the leader's side.
    Each test approves its own institution (a Buddhist temple, which gets SERVICES at approval, V12)
    and makes its own people. The rules behind each refusal are the API's integration tests; these
    prove the screens use the API, say what happened in words, and fit a 320 px phone.            */

function screenDir(): string {
  const dir = path.join(__dirname, "..", ".screens");
  mkdirSync(dir, { recursive: true });
  return dir;
}

/** A second person, in their own browser, signed in. */
async function personPage(browser: Browser, account: Account, locale: "" | "si" | "ta" = ""): Promise<Page> {
  const context = await browser.newContext();
  const page = await context.newPage();
  await signIn(page, account, locale);
  return page;
}

const openDays = (scope: Locator) => scope.locator("button[data-date]:not([aria-disabled])");
const appointmentNo = /^APT-\d{4}-\d{6}$/;

test("an owner sets up a service and a leader; a donor asks for it at home; the owner assigns; the donor sees it confirmed @phone", async ({ page, baseURL, browser }) => {
  const name = `E2E Services Vihara ${Date.now()}`;
  const institution = await approvedInstitution(baseURL!, name);
  const donor = await createAccount(baseURL!, "E2E Donor Nimal");

  try {
    // ---- the owner adds a service, after the form says what is missing
    await signIn(page, institution.owner);
    await page.goto("/services/types");
    await expect(page.getByRole("heading", { name: "Religious services", level: 1 })).toBeVisible();
    await expect(page.getByText("You offer no services yet.", { exact: false })).toBeVisible();

    await page.getByRole("button", { name: "Add a service" }).click();
    let form = page.getByRole("dialog");
    await form.getByRole("button", { name: "Add service" }).click();
    await expect(form.getByText("Please give a name of up to 100 characters.")).toBeVisible();
    await field(form, "Name").fill("Pirith");
    await field(form, "How long it takes").selectOption({ label: "3 hours" });
    await form.getByRole("radio", { name: "Here or at the donor's home" }).check();
    await form.getByRole("button", { name: "Add service" }).click();
    await expect(form).toBeHidden();
    const card = page.locator("article").filter({ hasText: "Pirith" });
    await expect(card.getByText("3 hours · 1 leader")).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);

    // ---- and a leader to send
    await page.goto("/services/leaders");
    await page.getByRole("button", { name: "Add a leader" }).click();
    form = page.getByRole("dialog");
    await field(form, "Title").fill("Ven.");
    await field(form, "Name").fill("E2E Ananda Thero");
    await field(form, "Phone (optional)").fill(newPhone());
    await form.getByRole("button", { name: "Add leader" }).click();
    await expect(form).toBeHidden();
    await expect(page.getByRole("heading", { name: "Ven. E2E Ananda Thero" })).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);

    // ---- the donor finds it on the page and asks for it at home
    const donorPage = await personPage(browser, donor);
    await donorPage.goto(`/i/${institution.slug}`);
    await expect(donorPage.getByRole("heading", { name: "Religious services" })).toBeVisible();
    await donorPage.getByRole("button", { name: "Ask for this service" }).click();
    const sheet = donorPage.getByRole("dialog");
    await expect(sheet.getByRole("heading", { name: "Pirith" })).toBeVisible();

    await sheet.getByRole("button", { name: "Send request" }).click();
    await expect(sheet.getByText("Please choose a day.")).toBeVisible();
    await openDays(sheet).first().click();
    await field(sheet, "Starting at").selectOption("10:00");
    await expect(sheet.getByText("It would end at about 13:00.")).toBeVisible();
    await sheet.getByRole("radio", { name: "At my home" }).check();
    await sheet.getByRole("button", { name: "Send request" }).click();
    await expect(sheet.getByText("Please give the address.")).toBeVisible();
    await field(sheet, "Address").fill("12 Temple Road, Galle");
    await field(sheet, "Occasion (optional)").fill("Our new house");
    await sheet.getByRole("button", { name: "Send request" }).click();
    await expect(sheet.getByText("Request sent")).toBeVisible();
    const no = (await sheet.getByText(appointmentNo).textContent())!;
    expect(await scrollsSideways(donorPage)).toBe(false);
    await sheet.getByRole("link", { name: "See my service requests" }).click();

    const mine = donorPage.locator("article").filter({ hasText: no });
    await expect(mine.getByText("Waiting for a reply")).toBeVisible();
    await expect(mine.getByText("12 Temple Road, Galle")).toBeVisible();
    await expect(mine.getByText("Our new house")).toBeVisible();

    // ---- the owner assigns the leader, which accepts the request
    await page.goto("/services/appointments");
    await page.getByRole("button", { name: "Waiting for you" }).click();
    const request = page.locator("article").filter({ hasText: no });
    await expect(request.getByText("E2E Donor Nimal")).toBeVisible();
    await expect(request.getByText("At the donor's home: 12 Temple Road, Galle")).toBeVisible();
    await request.getByRole("button", { name: "Assign leaders and accept" }).click();
    const picker = page.getByRole("dialog");
    await picker.getByRole("checkbox", { name: /E2E Ananda Thero/ }).check();
    await picker.getByRole("button", { name: "Assign 1 leader" }).click();
    await expect(page.getByText(`Leaders assigned to ${no}.`).first()).toBeVisible();
    await expect(page.getByText("No requests waiting.")).toBeVisible();

    await page.getByRole("button", { name: "Coming up" }).click();
    const approved = page.locator("article").filter({ hasText: no });
    await expect(approved.getByText("Confirmed", { exact: true })).toBeVisible();
    await expect(approved.getByText("Not answered yet")).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);

    await donorPage.reload();
    await expect(donorPage.locator("article").filter({ hasText: no }).getByText("Confirmed", { exact: true })).toBeVisible();
    await donorPage.context().close();
  } finally {
    await unlist(baseURL!, institution);
  }
});

test("a leader who signs in accepts from their schedule, then cannot come, and the appointment needs a leader again", async ({ page, baseURL, browser }) => {
  const institution = await approvedInstitution(baseURL!, `E2E Schedule Temple ${Date.now()}`);
  const type = await serviceTypeOf(baseURL!, institution);
  const leaderAccount = await createAccount(baseURL!, "E2E Leader Bodhi");
  const leader = await leaderOf(baseURL!, institution, "E2E Bodhi Thero", leaderAccount);
  const donor = await createAccount(baseURL!, "E2E Donor Kamala");
  const no = await requestBy(baseURL!, donor, type, colomboDate(5));
  await assign(baseURL!, institution, no, [leader]);

  try {
    await signIn(page, leaderAccount);
    await page.goto("/services/schedule");
    const item = page.locator("article").filter({ hasText: "E2E Donor Kamala" });
    await expect(item.getByText("Please answer")).toBeVisible();
    await item.getByRole("button", { name: "Accept" }).click();
    await expect(page.getByText(`You accepted ${no}.`).first()).toBeVisible();
    await expect(item.getByText("You accepted")).toBeVisible();

    await item.getByRole("button", { name: "I cannot come" }).click();
    const sheet = page.getByRole("dialog");
    await field(sheet, "Reason (optional)").fill("At a funeral that day");
    await sheet.getByRole("button", { name: "I cannot come" }).click();
    await expect(page.getByText(`The institution is told you cannot come to ${no}.`).first()).toBeVisible();
    await expect(page.getByText("Nothing assigned to you yet.")).toBeVisible();

    // A leader holds only their schedule: the agenda is not theirs.
    await page.goto("/services/appointments");
    await expect(page.getByText("Your role in this institution does not include this page.")).toBeVisible();

    const owner = await personPage(browser, institution.owner);
    await owner.goto("/services/appointments");
    await owner.getByRole("button", { name: "Needs leaders" }).click();
    const card = owner.locator("article").filter({ hasText: no });
    await expect(card.getByText("Needs 1 more leader")).toBeVisible();
    await expect(card.getByText(/Declined.*At a funeral that day/)).toBeVisible();
    await owner.context().close();
  } finally {
    await unlist(baseURL!, institution);
  }
});

test("a request turned down with a reason, which the donor reads; another cancelled by the donor @phone", async ({ page, baseURL, browser }) => {
  const institution = await approvedInstitution(baseURL!, `E2E Reject Kovil ${Date.now()}`);
  const type = await serviceTypeOf(baseURL!, institution, "Pooja");
  const donor = await createAccount(baseURL!, "E2E Donor Ravi");
  const rejected = await requestBy(baseURL!, donor, type, colomboDate(6));
  const kept = await requestBy(baseURL!, donor, type, colomboDate(7));

  try {
    await signIn(page, institution.owner);
    await page.goto("/services/appointments");
    await page.getByRole("button", { name: "Waiting for you" }).click();
    await page.locator("article").filter({ hasText: rejected }).getByRole("button", { name: "Reject" }).click();
    const sheet = page.getByRole("dialog");
    await sheet.getByRole("button", { name: "Reject request" }).click();
    await expect(sheet.getByText("Please give a reason.")).toBeVisible();
    await field(sheet, "Reason").fill("All our monks are away that week");
    await sheet.getByRole("button", { name: "Reject request" }).click();
    await expect(page.getByText(`${rejected} was rejected.`).first()).toBeVisible();

    const donorPage = await personPage(browser, donor);
    await donorPage.goto("/services/mine");
    await expect(donorPage.locator("article").filter({ hasText: rejected }).getByText("Not accepted: All our monks are away that week")).toBeVisible();

    await donorPage.locator("article").filter({ hasText: kept }).getByRole("button", { name: "Cancel request" }).click();
    const cancel = donorPage.getByRole("dialog");
    await cancel.getByRole("button", { name: "Cancel request" }).click();
    await expect(donorPage.getByText(`${kept} is cancelled.`).first()).toBeVisible();
    await expect(donorPage.getByText("Nothing coming up.")).toBeVisible();
    expect(await scrollsSideways(donorPage)).toBe(false);
    await donorPage.context().close();
  } finally {
    await unlist(baseURL!, institution);
  }
});

test("a leader's usual week is saved; leave over their appointment is refused; leave on another day is added and removed", async ({ page, baseURL }) => {
  const institution = await approvedInstitution(baseURL!, `E2E Leave Church ${Date.now()}`);
  const type = await serviceTypeOf(baseURL!, institution, "Blessing");
  const leader = await leaderOf(baseURL!, institution, "E2E Father Paul");
  const donor = await createAccount(baseURL!, "E2E Donor Mary");
  const date = colomboDate(8);
  const no = await requestBy(baseURL!, donor, type, date);
  await assign(baseURL!, institution, no, [leader]);

  try {
    await signIn(page, institution.owner);
    await page.goto("/services/leaders");
    const card = page.locator("article").filter({ hasText: "E2E Father Paul" });
    await expect(card.getByText("No usual week recorded")).toBeVisible();

    await card.getByRole("button", { name: "Usual week" }).click();
    let sheet = page.getByRole("dialog");
    await sheet.getByRole("button", { name: "Add a time" }).click();
    await sheet.getByRole("button", { name: "Save week" }).click();
    await expect(sheet).toBeHidden();
    await expect(card.getByText("Sun 06:00–18:00")).toBeVisible();

    // Leave over the day of the appointment the leader holds.
    await page.getByRole("button", { name: "Add leave" }).click();
    sheet = page.getByRole("dialog");
    await field(sheet, "From").fill(date);
    await field(sheet, "Until").fill(date);
    await sheet.getByRole("button", { name: "Add leave" }).click();
    await expect(sheet.getByText("The leader is assigned to a service in that time.", { exact: false })).toBeVisible();

    // Another day is free.
    const other = colomboDate(20);
    await field(sheet, "From").fill(other);
    await field(sheet, "Until").fill(other);
    await field(sheet, "Reason (optional)").fill("Retreat");
    await sheet.getByRole("button", { name: "Add leave" }).click();
    await expect(sheet).toBeHidden();
    const leave = page.locator("li").filter({ hasText: "Retreat" });
    await expect(leave.getByText("E2E Father Paul")).toBeVisible();
    await leave.getByRole("button", { name: "Remove" }).click();
    await expect(page.getByText("No leave coming up.")).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);
  } finally {
    await unlist(baseURL!, institution);
  }
});

test("the service screens in Sinhala and Tamil, nothing cut off at 320 px @phone", async ({ page, baseURL, browser }, info) => {
  const institution = await approvedInstitution(baseURL!, `E2E Languages Devalaya ${Date.now()}`, "si");
  const type = await serviceTypeOf(baseURL!, institution);
  const leaderAccount = await createAccount(baseURL!, "E2E Leader Tamil", "ta");
  const leader = await leaderOf(baseURL!, institution, "E2E Kurukkal Siva", leaderAccount);
  const donor = await createAccount(baseURL!, "E2E Donor Tamil", "ta");
  const no = await requestBy(baseURL!, donor, type, colomboDate(9));
  await assign(baseURL!, institution, no, [leader]);
  const shot = (locale: string, screen: string) => path.join(screenDir(), `${info.project.name}-${locale}-services-${screen}.png`);

  try {
    async function check(target: Page, locale: string, screen: string) {
      expect(await scrollsSideways(target), `${locale} ${screen}`).toBe(false);
      expect(await clipsSideways(target), `${locale} ${screen}`).toEqual([]);
      await target.screenshot({ path: shot(locale, screen), fullPage: true });
    }

    // The public page and the request sheet, in both languages.
    for (const locale of ["si", "ta"] as const) {
      const visitor = await (await browser.newContext()).newPage();
      await visitor.goto(`/${locale}/i/${institution.slug}`);
      await expect(visitor.locator("#services")).toBeVisible();
      await check(visitor, locale, "institution");
      await visitor.locator("#services").locator("..").getByRole("button").first().click();
      await expect(visitor.getByRole("dialog")).toBeVisible();
      await visitor.screenshot({ path: shot(locale, "sheet") });
      await visitor.context().close();
    }

    // The owner works in Sinhala.
    await signIn(page, institution.owner, "si");
    for (const screen of ["appointments", "types", "leaders"]) {
      await page.goto(`/si/services/${screen}`);
      await expect(page.locator("article").first()).toBeVisible();
      await check(page, "si", screen);
    }

    // The leader and the donor in Tamil.
    const leaderPage = await personPage(browser, leaderAccount, "ta");
    await leaderPage.goto("/ta/services/schedule");
    await expect(leaderPage.locator("article").first()).toBeVisible();
    await check(leaderPage, "ta", "schedule");
    await leaderPage.context().close();

    const donorPage = await personPage(browser, donor, "ta");
    await donorPage.goto("/ta/services/mine");
    await expect(donorPage.locator("article").first()).toBeVisible();
    await check(donorPage, "ta", "mine");
    await donorPage.context().close();

    // The request form itself, signed in: its labels and hints are the longest lines of all.
    const sinhalaDonor = await createAccount(baseURL!, "E2E Donor Sinhala", "si");
    for (const [locale, person] of [["si", sinhalaDonor], ["ta", donor]] as const) {
      const asker = await personPage(browser, person, locale);
      await asker.goto(`/${locale}/i/${institution.slug}`);
      await asker.locator("#services").locator("..").getByRole("button").first().click();
      const form = asker.getByRole("dialog");
      await openDays(form).first().click();
      await expect(form.getByRole("radio")).toHaveCount(2);
      expect(await clipsSideways(asker), `${locale} form`).toEqual([]);
      await asker.screenshot({ path: shot(locale, "form") });
      await form.getByRole("radio").last().check();
      await form.locator("textarea").first().scrollIntoViewIfNeeded();
      expect(await clipsSideways(asker), `${locale} form at home`).toEqual([]);
      await asker.screenshot({ path: shot(locale, "form-home") });
      await asker.context().close();
    }
  } finally {
    await unlist(baseURL!, institution);
  }
});
