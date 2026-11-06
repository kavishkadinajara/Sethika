import { mkdirSync } from "node:fs";
import path from "node:path";
import type { Page } from "@playwright/test";
import { createAccount, grantPlatformRole, lastTextId, newPhone, signIn, stepUp, type Account } from "../support/accounts";
import { field } from "../support/fields";
import { approvedInstitution, unlist } from "../support/institutions";
import { grantInstitutionRole } from "../support/services";
import { expect, scrollsSideways, test } from "../support/test";

/*  Plan 09, D.1: gifts and receipts on /giving/donations, the printable receipt, My giving, and the
    platform's "bring roles up to date". Each test approves its own institution, so its roles hold
    GIVING from the templates. The rules (numbering, maker-checker, the trigger, RLS, texts) are the
    API's integration tests (DonationEndpointsTests); these prove the screens use the API, say what
    happened in words, and fit a 320 px phone.                                                     */

function shot(name: string): string {
  const dir = path.join(__dirname, "..", ".screens");
  mkdirSync(dir, { recursive: true });
  return path.join(dir, `giving-${name}.png`);
}

const year = new Date().getFullYear();

async function recordCash(page: Page, name: string, amount: string) {
  await page.getByRole("button", { name: "Record a gift" }).click();
  const form = page.getByRole("dialog");
  await field(form, "Amount (LKR)").fill(amount);
  await field(form, "Paid by").selectOption({ label: "Cash" });
  await form.getByRole("radio", { name: "Someone else" }).check();
  await field(form, "Their name").fill(name);
  await form.getByRole("button", { name: "Record the gift" }).click();
  await expect(form).toBeHidden();
}

test("staff record a cash gift, open its printable receipt, and a second person voids it behind a step-up @phone", async ({ page, baseURL, browser }, testInfo) => {
  const institution = await approvedInstitution(baseURL!, `E2E Giving Vihara ${Date.now()}`);
  const staff = await createAccount(baseURL!, "E2E Counter Staff");
  grantInstitutionRole(staff, institution, "STAFF");

  try {
    await signIn(page, staff);
    await page.goto("/giving/donations");
    await expect(page.getByRole("heading", { name: "Donations and receipts", level: 1 })).toBeVisible();
    await expect(page.getByText("No gifts recorded in this month.")).toBeVisible();

    // ---- the form says what is missing
    await page.getByRole("button", { name: "Record a gift" }).click();
    const form = page.getByRole("dialog");
    await form.getByRole("radio", { name: "Someone else" }).check();
    await form.getByRole("button", { name: "Record the gift" }).click();
    await expect(form.getByText("Enter an amount above zero, with at most two decimals.")).toBeVisible();
    await expect(form.getByText("Enter the giver's name.")).toBeVisible();
    await form.getByRole("button", { name: "Cancel" }).click();
    await expect(form).toBeHidden();

    // ---- a walk-in's cash
    await recordCash(page, "E2E Walk-in Giver", "5000");
    await expect(page.getByText(`Receipt RCP-${year}-000001 recorded.`).first()).toBeVisible();
    const card = page.getByRole("article", { name: `RCP-${year}-000001` });
    await expect(card.getByRole("heading", { name: "LKR 5,000.00" })).toBeVisible();
    await expect(card.getByText("E2E Walk-in Giver")).toBeVisible();
    await expect(page.getByTestId("giving-totals")).toHaveText("1 gift: LKR 5,000.00 in money, none in items.");
    // Staff record; they never void.
    await expect(card.getByRole("button", { name: /^Void/ })).toHaveCount(0);
    expect(await scrollsSideways(page)).toBe(false);
    await page.screenshot({ path: shot(`list-${testInfo.project.name}`), fullPage: true });

    // ---- the receipt
    await card.getByRole("link", { name: `Receipt RCP-${year}-000001` }).click();
    const receipt = page.getByRole("article", { name: `Receipt RCP-${year}-000001` });
    await expect(receipt.getByText(institution.name)).toBeVisible();
    await expect(receipt.getByText("LKR 5,000.00")).toBeVisible();
    await expect(receipt.getByText("Cash")).toBeVisible();
    await expect(receipt.getByText("It is not a tax receipt.", { exact: false })).toBeVisible();
    await expect(page.getByRole("button", { name: "Print or save as PDF" })).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);
    await page.screenshot({ path: shot(`receipt-${testInfo.project.name}`), fullPage: true });
    // Printed, the shell and the buttons are gone: only the receipt.
    await page.emulateMedia({ media: "print" });
    await expect(page.getByRole("button", { name: "Print or save as PDF" })).toBeHidden();
    await expect(page.locator("header").first()).toBeHidden();
    await page.screenshot({ path: shot(`receipt-print-${testInfo.project.name}`), fullPage: true });
    await page.emulateMedia({ media: "screen" });

    // ---- the owner, a second person, voids it with a reason behind a step-up
    const ownerPage = await (await browser.newContext()).newPage();
    await signIn(ownerPage, institution.owner);
    await ownerPage.goto("/giving/donations");
    await ownerPage.getByRole("button", { name: `Void receipt RCP-${year}-000001` }).click();
    const sheet = ownerPage.getByRole("dialog");
    await sheet.getByRole("button", { name: "Void the gift" }).click();
    await expect(sheet.getByText("Say why it is voided.")).toBeVisible();
    await field(sheet, "Why is it voided?").fill("Counted twice at the counter.");
    const before = lastTextId(institution.owner.phone);
    await sheet.getByRole("button", { name: "Void the gift" }).click();
    await stepUp(ownerPage, institution.owner, before);
    await expect(ownerPage.getByText(`Receipt RCP-${year}-000001 voided.`).first()).toBeVisible();
    const voided = ownerPage.getByRole("article", { name: `RCP-${year}-000001` });
    await expect(voided.getByText("VOID", { exact: true })).toBeVisible();
    await expect(ownerPage.getByTestId("giving-totals")).toContainText("No gifts kept");
    await expect(ownerPage.getByTestId("giving-totals")).toContainText("1 voided, not counted");

    // The filter shows voided gifts alone.
    await ownerPage.getByRole("button", { name: "Not voided", exact: true }).click();
    await expect(ownerPage.getByText("No gift this month matches what you chose.")).toBeVisible();

    // The receipt says VOID, why and by whom.
    await page.reload();
    await expect(receipt.getByRole("status")).toContainText("VOID");
    await expect(receipt.getByText("Reason: Counted twice at the counter.")).toBeVisible();
    await ownerPage.context().close();
  } finally {
    await unlist(baseURL!, institution);
  }
});

test("the recorder cannot void their own gift; a donor book gift reaches the donor's My giving", async ({ page, baseURL, browser }) => {
  const institution = await approvedInstitution(baseURL!, `E2E Giving Kovil ${Date.now()}`);
  const donor: Account = await createAccount(baseURL!, "E2E Giving Donor");

  try {
    await signIn(page, institution.owner);

    // The donor is in the donor book: their phone is their account's, so the row is linked.
    await page.goto("/bookings/sponsors");
    await page.getByRole("button", { name: "Add a donor" }).click();
    let form = page.getByRole("dialog");
    await field(form, "Full name").fill("E2E Kamala Giver");
    await field(form, "Phone").fill(donor.phone);
    await form.getByRole("button", { name: "Add donor" }).click();
    await expect(form).toBeHidden();

    // ---- items from the donor book
    await page.goto("/giving/donations");
    await page.getByRole("button", { name: "Record a gift" }).click();
    form = page.getByRole("dialog");
    await form.getByRole("radio", { name: "Items" }).check();
    await field(form, "What was given").fill("Exercise books");
    await field(form, "How many (optional)").fill("50");
    await field(form, "Find them by name or phone").fill("kamala");
    await form.getByRole("radio", { name: /E2E Kamala Giver/ }).check();
    await expect(form.getByText("Chosen: E2E Kamala Giver")).toBeVisible();
    await form.getByRole("button", { name: "Record the gift" }).click();
    await expect(form).toBeHidden();

    const card = page.getByRole("article", { name: `RCP-${year}-000001` });
    await expect(card.getByRole("heading", { name: "Exercise books (50)" })).toBeVisible();
    await expect(card.getByText("donor book")).toBeVisible();
    // The owner recorded it, so the owner may not void it: maker-checker.
    await expect(card.getByRole("button", { name: /^Void/ })).toHaveCount(0);

    // ---- the donor sees it in My giving, and opens its receipt
    const donorPage = await (await browser.newContext()).newPage();
    await signIn(donorPage, donor);
    await donorPage.goto("/giving/mine");
    await expect(donorPage.getByRole("heading", { name: "My giving", level: 1 })).toBeVisible();
    const mine = donorPage.getByRole("article", { name: `RCP-${year}-000001` });
    await expect(mine.getByRole("heading", { name: "Exercise books (50)" })).toBeVisible();
    await expect(mine.getByRole("link", { name: institution.name })).toBeVisible();
    await mine.getByRole("link", { name: `Receipt RCP-${year}-000001` }).click();
    const receipt = donorPage.getByRole("article", { name: `Receipt RCP-${year}-000001` });
    await expect(receipt.getByText("E2E Kamala Giver")).toBeVisible();
    await expect(receipt.getByText("Exercise books")).toBeVisible();
    await expect(donorPage.getByRole("link", { name: "Back to my giving" })).toBeVisible();
    await donorPage.context().close();
  } finally {
    await unlist(baseURL!, institution);
  }
});

test("a platform admin brings an institution's roles up to their templates, behind a step-up", async ({ page, baseURL }) => {
  const institution = await approvedInstitution(baseURL!, `E2E Giving Sync ${Date.now()}`);
  const admin = await createAccount(baseURL!, "E2E Sync Admin");
  grantPlatformRole(admin, "PLATFORM_ADMIN");

  try {
    await signIn(page, admin);
    await page.goto(`/admin/institutions?q=${encodeURIComponent(institution.name)}`);
    const before = lastTextId(admin.phone);
    await page.getByRole("button", { name: `Bring ${institution.name}'s roles up to date` }).click();
    await stepUp(page, admin, before);
    // Approved today, it holds everything already.
    await expect(page.getByText(`${institution.name} already holds everything its templates give. Nothing was added.`).first()).toBeVisible();
  } finally {
    await unlist(baseURL!, institution);
  }
});

// A walk-in with a phone: the screen keeps the phone off texts; the receipt keeps it on paper only.
test("an unknown phone number on a walk-in is refused in words", async ({ page, baseURL }) => {
  const institution = await approvedInstitution(baseURL!, `E2E Giving Phone ${Date.now()}`);
  try {
    await signIn(page, institution.owner);
    await page.goto("/giving/donations");
    await page.getByRole("button", { name: "Record a gift" }).click();
    const form = page.getByRole("dialog");
    await field(form, "Amount (LKR)").fill("100");
    await form.getByRole("radio", { name: "Someone else" }).check();
    await field(form, "Their name").fill("E2E Walk-in");
    await field(form, "Their phone (optional)").fill("12");
    await form.getByRole("button", { name: "Record the gift" }).click();
    await expect(form.getByText("Enter a Sri Lankan phone number, or leave it blank.")).toBeVisible();
    await field(form, "Their phone (optional)").fill(newPhone());
    await form.getByRole("button", { name: "Record the gift" }).click();
    await expect(form).toBeHidden();
  } finally {
    await unlist(baseURL!, institution);
  }
});

for (const [locale, heading, record] of [
  ["si", "පරිත්‍යාග සහ රිසිට්පත්", "පරිත්‍යාගයක් සටහන් කරන්න"],
  ["ta", "நன்கொடைகளும் பற்றுச்சீட்டுகளும்", "நன்கொடையைப் பதிவு செய்"],
] as const) {
  test(`the donations screen and its record sheet in ${locale} fit the phone @phone`, async ({ page, baseURL }, testInfo) => {
    const institution = await approvedInstitution(baseURL!, `E2E Giving ${locale} ${Date.now()}`, locale);
    try {
      await signIn(page, institution.owner, locale);
      await page.goto(`/${locale}/giving/donations`);
      await expect(page.getByRole("heading", { name: heading, level: 1 })).toBeVisible();
      await expect(page.getByTestId("giving-totals")).toBeVisible();
      expect(await scrollsSideways(page)).toBe(false);
      await page.screenshot({ path: shot(`list-${locale}-${testInfo.project.name}`), fullPage: true });
      await page.getByRole("button", { name: record, exact: true }).click();
      await expect(page.getByRole("dialog")).toBeVisible();
      expect(await scrollsSideways(page)).toBe(false);
      await page.screenshot({ path: shot(`sheet-${locale}-${testInfo.project.name}`), fullPage: true });
    } finally {
      await unlist(baseURL!, institution);
    }
  });
}
