import { mkdirSync } from "node:fs";
import path from "node:path";
import { createAccount, newPhone, signIn } from "../support/accounts";
import { field } from "../support/fields";
import { approvedInstitution, unlist } from "../support/institutions";
import { grantInstitutionRole } from "../support/services";
import { expect, scrollsSideways, test } from "../support/test";

/*  Plan 08, K.1: the institution's donor book on /bookings/sponsors. Each test approves its own
    institution, so its owner holds BOOKINGS.SPONSORS from the template. The rules behind each refusal
    are the API's integration tests (DonorBookEndpointsTests); these prove the screen uses the API,
    says what happened in words, and fits a 320 px phone.                                           */

function shot(name: string): string {
  const dir = path.join(__dirname, "..", ".screens");
  mkdirSync(dir, { recursive: true });
  return path.join(dir, `sponsors-${name}.png`);
}

/** 0779 123456 as the card shows it: 077 912 3456. */
const shown = (phone: string) => `${phone.slice(0, 3)} ${phone.slice(3, 6)} ${phone.slice(6)}`;

test("an owner adds regular donors, is told what is missing or taken, finds one and edits them @phone", async ({ page, baseURL }, testInfo) => {
  const institution = await approvedInstitution(baseURL!, `E2E Donor Book Vihara ${Date.now()}`);
  const appUser = await createAccount(baseURL!, "E2E App Donor");
  const phone = newPhone();

  try {
    await signIn(page, institution.owner);
    await page.goto("/bookings/sponsors");
    await expect(page.getByRole("heading", { name: "Regular donors", level: 1 })).toBeVisible();
    await expect(page.getByText("No one in your donor book yet.", { exact: false })).toBeVisible();

    // ---- the form says what is missing
    await page.getByRole("button", { name: "Add a donor" }).click();
    let form = page.getByRole("dialog");
    await form.getByRole("button", { name: "Add donor" }).click();
    await expect(form.getByText("Enter their name (up to 150 letters).")).toBeVisible();
    await expect(form.getByText("Enter a phone number, for example 077 123 4567 or 011 234 5678.")).toBeVisible();

    // ---- a person who does not use the app, who agreed to texts in Tamil
    await field(form, "Full name").fill("E2E Sunil Perera");
    await field(form, "Phone").fill(phone);
    await field(form, "Language of texts").selectOption({ label: "Tamil" });
    await form.getByRole("switch", { name: /agreed to receive texts/ }).click();
    await field(form, "Notes").fill("Brings rice and two curries");
    await form.getByRole("button", { name: "Add donor" }).click();
    await expect(form).toBeHidden();

    const sunil = page.locator("article").filter({ hasText: "E2E Sunil Perera" });
    await expect(sunil.getByText(`${shown(phone)} · Tamil`)).toBeVisible();
    await expect(sunil.getByText(/^Agreed to texts on /)).toBeVisible();
    await expect(sunil.getByText("No regular days yet")).toBeVisible();
    await expect(sunil.getByText("Brings rice and two curries")).toBeVisible();
    await expect(sunil.getByText("Uses Sethika")).toHaveCount(0);

    // ---- the same number again is refused beside the phone
    await page.getByRole("button", { name: "Add a donor" }).click();
    form = page.getByRole("dialog");
    await field(form, "Full name").fill("E2E Someone Else");
    await field(form, "Phone").fill(phone);
    await form.getByRole("button", { name: "Add donor" }).click();
    await expect(form.getByText("Someone in your donor book already has this number.")).toBeVisible();

    // ---- a person whose phone is a Sethika account's is linked; no consent, so no texts but the app's
    await field(form, "Full name").fill("E2E Kamala App");
    await field(form, "Phone").fill(appUser.phone);
    await form.getByRole("button", { name: "Add donor" }).click();
    await expect(form).toBeHidden();
    const kamala = page.locator("article").filter({ hasText: "E2E Kamala App" });
    await expect(kamala.getByText("Uses Sethika")).toBeVisible();
    await expect(kamala.getByText("Gets texts in the app")).toBeVisible();
    await expect(page.getByRole("heading", { name: "Donor book · 2 people" })).toBeVisible();

    // ---- searching by part of a phone, then by name
    const search = page.getByRole("searchbox", { name: "Search" });
    await search.fill(phone.slice(-5));
    await expect(page.locator("article")).toHaveCount(1);
    await expect(sunil).toBeVisible();
    await search.fill("kamala");
    await expect(page.locator("article")).toHaveCount(1);
    await search.fill("nobody here");
    await expect(page.getByText("No one in the book matches “nobody here”.")).toBeVisible();
    await search.fill("");
    await expect(page.locator("article")).toHaveCount(2);
    expect(await scrollsSideways(page)).toBe(false);
    await page.screenshot({ path: shot(`book-${testInfo.project.name}`), fullPage: true });

    // ---- editing: the form opens with what is stored; unticking the consent removes the date
    await page.getByRole("button", { name: "Edit E2E Sunil Perera" }).click();
    form = page.getByRole("dialog");
    await expect(field(form, "Phone")).toHaveValue(shown(phone));
    await expect(form.getByRole("switch", { name: /agreed to receive texts/ })).toHaveAttribute("aria-checked", "true");
    await page.screenshot({ path: shot(`form-${testInfo.project.name}`) });
    await field(form, "Full name").fill("E2E Sunil Perera Jr");
    await form.getByRole("switch", { name: /agreed to receive texts/ }).click();
    await form.getByRole("button", { name: "Save" }).click();
    await expect(form).toBeHidden();
    const edited = page.locator("article").filter({ hasText: "E2E Sunil Perera Jr" });
    await expect(edited.getByText("No texts: tell them yourself")).toBeVisible();

    // ---- a landline is kept for calling, but cannot agree to texts (QA OBS-05)
    await page.getByRole("button", { name: "Add a donor" }).click();
    form = page.getByRole("dialog");
    await field(form, "Full name").fill("E2E Landline Aunty");
    await field(form, "Phone").fill(`0112${String(Date.now()).slice(-6)}`);
    await form.getByRole("switch", { name: /agreed to receive texts/ }).click();
    await form.getByRole("button", { name: "Add donor" }).click();
    await expect(form.getByText("A landline cannot receive texts. Untick this, or enter a mobile number.")).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);
    await form.getByRole("switch", { name: /agreed to receive texts/ }).click();
    await form.getByRole("button", { name: "Add donor" }).click();
    await expect(form).toBeHidden();
    await expect(page.locator("article").filter({ hasText: "E2E Landline Aunty" }).getByText("No texts: tell them yourself")).toBeVisible();
  } finally {
    await unlist(baseURL!, institution);
  }
});

test("staff see the donor book in the menu, and neither add nor edit", async ({ page, baseURL, browser }) => {
  const institution = await approvedInstitution(baseURL!, `E2E Donor Book Staff ${Date.now()}`);
  const staff = await createAccount(baseURL!, "E2E Staff Member");
  grantInstitutionRole(staff, institution, "STAFF");

  try {
    // The owner adds one person, so staff have someone to look at.
    const ownerPage = await (await browser.newContext()).newPage();
    await signIn(ownerPage, institution.owner);
    await ownerPage.goto("/bookings/sponsors");
    await ownerPage.getByRole("button", { name: "Add a donor" }).click();
    const form = ownerPage.getByRole("dialog");
    await field(form, "Full name").fill("E2E Nimal Silva");
    await field(form, "Phone").fill(newPhone());
    await form.getByRole("button", { name: "Add donor" }).click();
    await expect(form).toBeHidden();

    await signIn(page, staff);
    await page.goto("/bookings/calendar");
    await page.getByRole("link", { name: "Regular donors" }).first().click();
    await expect(page).toHaveURL(/\/bookings\/sponsors$/);
    await expect(page.getByRole("heading", { name: "E2E Nimal Silva" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Add a donor" })).toHaveCount(0);
    await expect(page.getByRole("button", { name: /^Edit/ })).toHaveCount(0);
  } finally {
    await unlist(baseURL!, institution);
  }
});

test("the donor book speaks Sinhala to a Sinhala-speaking owner @phone", async ({ page, baseURL }, testInfo) => {
  const institution = await approvedInstitution(baseURL!, `E2E Donor Book Kovil ${Date.now()}`, "si");

  try {
    await signIn(page, institution.owner, "si");
    await page.goto("/si/bookings/sponsors");
    await expect(page.getByRole("heading", { name: "නිත්‍ය දායකයින්", level: 1 })).toBeVisible();
    await page.getByRole("button", { name: "දායකයෙකු එක් කරන්න" }).click();
    const form = page.getByRole("dialog");
    await form.getByRole("button", { name: "දායකයා එක් කරන්න" }).click();
    await expect(form.getByText("ඔවුන්ගේ නම ඇතුළත් කරන්න (අකුරු 150 දක්වා).")).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);
    await page.screenshot({ path: shot(`si-${testInfo.project.name}`) });
  } finally {
    await unlist(baseURL!, institution);
  }
});
