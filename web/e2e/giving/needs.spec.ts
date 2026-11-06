import { mkdirSync } from "node:fs";
import path from "node:path";
import type { Page } from "@playwright/test";
import { createAccount, signIn } from "../support/accounts";
import { field } from "../support/fields";
import { approvedInstitution, listed, unlist } from "../support/institutions";
import { grantInstitutionRole } from "../support/services";
import { expect, scrollsSideways, test } from "../support/test";

/*  Plan 09, D.2: needs on /giving/needs, the public board on /needs and on /i/[slug], pledging, receiving
    into a receipt, and pledges in My giving. The rules (the counted inventory and its forced race, the
    platform scope, RLS) are the API's integration tests (NeedEndpointsTests); these prove the screens use
    the API, say what happened in words, and fit a 320 px phone.                                       */

function shot(name: string): string {
  const dir = path.join(__dirname, "..", ".screens");
  mkdirSync(dir, { recursive: true });
  return path.join(dir, `needs-${name}.png`);
}

const year = new Date().getFullYear();

/** Adds a draft and opens it onto the board. */
async function openNeed(page: Page, title: string, quantity: string) {
  await page.goto("/giving/needs");
  await page.getByRole("button", { name: "Add a need" }).click();
  const form = page.getByRole("dialog");
  await field(form, "What is needed").fill(title);
  await field(form, "How many").fill(quantity);
  await field(form, "Category").selectOption({ label: "Stationery" });
  await form.getByRole("button", { name: "Add the need" }).click();
  await expect(form).toBeHidden();
  await page.getByRole("button", { name: `Open ${title} on the board` }).click();
  await expect(page.getByRole("article", { name: title }).getByText("Open", { exact: true })).toBeVisible();
}

async function pledgeFrom(page: Page, title: string, quantity: string) {
  await page.getByRole("button", { name: `Pledge to ${title}` }).click();
  const sheet = page.getByRole("dialog");
  await field(sheet, "How many (Pieces)").fill(quantity);
  await sheet.getByRole("button", { name: "Pledge", exact: true }).click();
}

test("a need goes from draft to fulfilled: opened, pledged, delivered short, pledged again, delivered @phone", async ({ page, baseURL, browser }, testInfo) => {
  const stamp = Date.now();
  test.setTimeout(240_000);
  const institution = await approvedInstitution(baseURL!, `E2E Needs Vihara ${stamp}`);
  await listed(baseURL!, institution);
  const title = `Exercise books ${stamp}`;
  const donor = await createAccount(baseURL!, "E2E Pledging Donor");

  try {
    await signIn(page, institution.owner);
    await page.goto("/giving/needs");
    await expect(page.getByRole("heading", { name: "Needs", level: 1 })).toBeVisible();
    await expect(page.getByText("No needs yet.")).toBeVisible();

    // ---- the form says what is missing
    await page.getByRole("button", { name: "Add a need" }).click();
    const form = page.getByRole("dialog");
    await form.getByRole("button", { name: "Add the need" }).click();
    await expect(form.getByText("Say what is needed (up to 150 characters).")).toBeVisible();
    await form.getByRole("button", { name: "Cancel" }).click();
    await expect(form).toBeHidden();

    await openNeed(page, title, "10");
    expect(await scrollsSideways(page)).toBe(false);
    await page.screenshot({ path: shot(`list-${testInfo.project.name}`), fullPage: true });

    // ---- anonymous: on the board and on its page; pledging asks for a sign-in
    const visitor = await browser.newContext();
    const anonymous = await visitor.newPage();
    await anonymous.goto("/needs");
    await expect(anonymous.getByRole("heading", { name: "Needs board", level: 1 })).toBeVisible();
    const onBoard = anonymous.getByRole("article", { name: title });
    await expect(onBoard.getByRole("link", { name: institution.name })).toBeVisible();
    await expect(onBoard.getByTestId("need-progress")).toHaveText("0 received, 0 spoken for, of 10 Pieces");
    expect(await scrollsSideways(anonymous)).toBe(false);
    await anonymous.screenshot({ path: shot(`board-${testInfo.project.name}`), fullPage: true });
    await anonymous.getByRole("button", { name: `Pledge to ${title}` }).click();
    await expect(anonymous.getByRole("dialog").getByRole("link", { name: "Sign in" })).toHaveAttribute("href", /need%3D/);

    await anonymous.goto(`/i/${institution.slug}`);
    await expect(anonymous.getByRole("heading", { name: "What we need" })).toBeVisible();
    await expect(anonymous.getByRole("article", { name: title })).toBeVisible();
    await visitor.close();

    // ---- a donor pledges more than is there, then what they can
    const donorContext = await browser.newContext();
    const donorPage = await donorContext.newPage();
    await signIn(donorPage, donor);
    await donorPage.goto("/giving/mine");
    await expect(donorPage.getByText("You have not pledged to any need yet.")).toBeVisible();
    await donorPage.goto("/needs");
    await pledgeFrom(donorPage, title, "11");
    const sheet = donorPage.getByRole("dialog");
    await expect(sheet.getByText("There is not that much left of this need", { exact: false })).toBeVisible();
    await field(sheet, "How many (Pieces)").fill("6");
    await sheet.getByRole("button", { name: "Pledge", exact: true }).click();
    await expect(donorPage.getByText("Your pledge is recorded. Thank you.").first()).toBeVisible();
    await expect(donorPage.getByRole("article", { name: title }).getByTestId("need-progress")).toHaveText("0 received, 6 spoken for, of 10 Pieces");

    await donorPage.goto("/giving/mine");
    const pledged = donorPage.getByRole("article", { name: title });
    await expect(pledged.getByText("Waiting for delivery")).toBeVisible();
    await expect(pledged.getByText("Promised 6 Pieces")).toBeVisible();
    expect(await scrollsSideways(donorPage)).toBe(false);
    await donorPage.screenshot({ path: shot(`mine-${testInfo.project.name}`), fullPage: true });

    // ---- a need with an open pledge cannot close: the button is gone, and the pledge is in its sheet
    await page.reload();
    const need = page.getByRole("article", { name: title });
    await expect(need.getByRole("button", { name: `Close ${title}` })).toHaveCount(0);

    await page.getByRole("button", { name: `Pledges on ${title}` }).click();
    const pledges = page.getByRole("dialog", { name: `Pledges on ${title}` });
    await expect(pledges.getByRole("article", { name: "E2E Pledging Donor" }).getByText("Promised 6 Pieces")).toBeVisible();
    await page.screenshot({ path: shot(`pledges-${testInfo.project.name}`), fullPage: true });

    // ---- 4 of the 6 came: a receipt, and the 2 go back to the need
    await pledges.getByRole("button", { name: "Mark E2E Pledging Donor's pledge delivered" }).click();
    const receive = page.getByRole("dialog", { name: "What E2E Pledging Donor brought" });
    await field(receive, "How many came (Pieces)").fill("7");
    await receive.getByRole("button", { name: "Record the delivery" }).click();
    await expect(receive.getByText("Enter a number above zero, at most 6.")).toBeVisible();
    await field(receive, "How many came (Pieces)").fill("4");
    await receive.getByRole("button", { name: "Record the delivery" }).click();
    await expect(page.getByText(`Delivered. Receipt RCP-${year}-000001 recorded.`).first()).toBeVisible();
    await expect(pledges.getByText("4 Pieces came")).toBeVisible();
    await pledges.getByRole("button", { name: "Close", exact: true }).last().click(); // the footer's; the first is the sheet's own X
    await expect(need.getByTestId("need-progress")).toHaveText("4 received, 4 spoken for, of 10 Pieces");

    // ---- the donor reads the receipt from My giving
    await donorPage.reload();
    const delivered = donorPage.getByRole("article", { name: title });
    await expect(delivered.getByText("4 Pieces received")).toBeVisible();
    await delivered.getByRole("link", { name: `Receipt RCP-${year}-000001` }).click();
    const receipt = donorPage.getByRole("article", { name: `Receipt RCP-${year}-000001` });
    await expect(receipt.getByText(title)).toBeVisible();
    await expect(receipt.getByText("E2E Pledging Donor")).toBeVisible();

    // ---- the rest arrives at the counter, without a pledge, and fulfils it
    await page.goto("/giving/donations");
    await page.getByRole("button", { name: "Record a gift" }).click();
    const record = page.getByRole("dialog");
    await record.getByRole("radio", { name: "Items" }).check();
    await field(record, "What was given").fill("The last exercise books");
    await field(record, "For a need (optional)").selectOption({ label: title });
    await record.getByRole("radio", { name: "Someone else" }).check();
    await field(record, "Their name").fill("E2E Walk-in Giver");
    await record.getByRole("button", { name: "Record the gift" }).click();
    await expect(record.getByText("Enter a number above zero.")).toBeVisible();
    await field(record, "How many (optional)").fill("6");
    await record.getByRole("button", { name: "Record the gift" }).click();
    await expect(record).toBeHidden();

    await page.goto("/giving/needs");
    const fulfilled = page.getByRole("article", { name: title });
    await expect(fulfilled.getByText("Fulfilled", { exact: true })).toBeVisible();
    await expect(fulfilled.getByTestId("need-progress")).toHaveText("10 received, 10 spoken for, of 10 Pieces");

    // Fulfilled, it is off the board.
    await donorPage.goto("/needs");
    await expect(donorPage.getByRole("article", { name: title })).toHaveCount(0);
    await donorContext.close();
  } finally {
    await unlist(baseURL!, institution);
  }
});

test("a donor cancels their pledge, and staff cancel another with a reason the donor reads", async ({ page, baseURL, browser }) => {
  const stamp = Date.now();
  test.setTimeout(240_000);
  const institution = await approvedInstitution(baseURL!, `E2E Needs Kovil ${stamp}`);
  await listed(baseURL!, institution);
  const title = `Rice packets ${stamp}`;
  const donor = await createAccount(baseURL!, "E2E Changing Donor");

  try {
    await signIn(page, institution.owner);
    await openNeed(page, title, "5");

    const donorPage = await (await browser.newContext()).newPage();
    await signIn(donorPage, donor);
    await donorPage.goto(`/i/${institution.slug}`);
    await pledgeFrom(donorPage, title, "5");
    await expect(donorPage.getByText("Your pledge is recorded. Thank you.").first()).toBeVisible();
    await expect(donorPage.getByText("All of this is pledged. Thank you.")).toBeVisible();

    // ---- the donor's own cancel gives it back
    await donorPage.goto("/giving/mine");
    await donorPage.getByRole("button", { name: `Cancel my pledge to ${title}` }).click();
    await expect(donorPage.getByText("Your pledge is cancelled.").first()).toBeVisible();
    await expect(donorPage.getByRole("article", { name: title }).getByText("Cancelled")).toBeVisible();

    // ---- again, and this time staff cancel it
    await donorPage.goto("/needs");
    await pledgeFrom(donorPage, title, "2");
    await expect(donorPage.getByText("Your pledge is recorded. Thank you.").first()).toBeVisible();

    await page.reload();
    await page.getByRole("button", { name: `Pledges on ${title}` }).click();
    const pledges = page.getByRole("dialog", { name: `Pledges on ${title}` });
    await pledges.getByRole("button", { name: "Cancel E2E Changing Donor's pledge" }).click();
    const cancel = page.getByRole("dialog", { name: "Cancel E2E Changing Donor's pledge" });
    await cancel.getByRole("button", { name: "Cancel the pledge" }).click();
    await expect(cancel.getByText("Say why it is cancelled.")).toBeVisible();
    await field(cancel, "Why is it cancelled?").fill("We found a supplier.");
    await cancel.getByRole("button", { name: "Cancel the pledge" }).click();
    await expect(page.getByText("Pledge cancelled.").first()).toBeVisible();
    await pledges.getByRole("button", { name: "Close", exact: true }).last().click(); // the footer's; the first is the sheet's own X

    await donorPage.goto("/giving/mine");
    await expect(donorPage.getByText("Reason: We found a supplier.")).toBeVisible();
    await donorPage.context().close();

    // ---- nothing pending and nothing received: it can be cancelled
    await page.getByRole("button", { name: `Cancel ${title}` }).click();
    await expect(page.getByRole("article", { name: title }).getByText("Cancelled", { exact: true })).toBeVisible();
  } finally {
    await unlist(baseURL!, institution);
  }
});

test("staff read the needs and their pledges, and change nothing", async ({ page, baseURL }) => {
  const stamp = Date.now();
  const institution = await approvedInstitution(baseURL!, `E2E Needs Staff ${stamp}`);
  const title = `Soap ${stamp}`;
  const staff = await createAccount(baseURL!, "E2E Needs Staff");
  grantInstitutionRole(staff, institution, "STAFF");

  try {
    await signIn(page, institution.owner);
    await openNeed(page, title, "5");
    await page.context().clearCookies();

    await signIn(page, staff);
    await page.goto("/giving/needs");
    const need = page.getByRole("article", { name: title });
    await expect(need.getByRole("button", { name: `Pledges on ${title}` })).toBeVisible();
    await expect(page.getByRole("button", { name: "Add a need" })).toHaveCount(0);
    await expect(need.getByRole("button", { name: `Change ${title}` })).toHaveCount(0);
    await expect(need.getByRole("button", { name: `Close ${title}` })).toHaveCount(0);
  } finally {
    await unlist(baseURL!, institution);
  }
});

for (const [locale, heading, add, board, pledgeWord] of [
  ["si", "අවශ්‍යතා", "අවශ්‍යතාවක් එක් කරන්න", "අවශ්‍යතා පුවරුව", "පොරොන්දු වන්න"],
  ["ta", "தேவைகள்", "தேவையைச் சேர்", "தேவைகள் பலகை", "உறுதியளி"],
] as const) {
  test(`needs, the board and the pledge sheet in ${locale} fit the phone @phone`, async ({ page, baseURL, browser }, testInfo) => {
    const stamp = Date.now();
    const institution = await approvedInstitution(baseURL!, `E2E Needs ${locale} ${stamp}`, locale);
    await listed(baseURL!, institution);
    const title = `E2E need ${locale} ${stamp}`;

    try {
      await signIn(page, institution.owner, locale);
      await page.goto(`/${locale}/giving/needs`);
      await expect(page.getByRole("heading", { name: heading, level: 1 })).toBeVisible();
      await page.getByRole("button", { name: add, exact: true }).click();
      const form = page.getByRole("dialog");
      expect(await scrollsSideways(page)).toBe(false);
      await page.screenshot({ path: shot(`sheet-${locale}-${testInfo.project.name}`), fullPage: true });
      await form.getByRole("textbox").first().fill(title);
      await form.getByRole("textbox").nth(2).fill("10");
      await form.locator('button[type="submit"]').click();
      await expect(form).toBeHidden();
      await page.getByRole("article", { name: title }).getByRole("button").nth(0).click();
      await expect(page.getByRole("article", { name: title }).getByRole("progressbar")).toBeVisible();
      expect(await scrollsSideways(page)).toBe(false);
      await page.screenshot({ path: shot(`list-${locale}-${testInfo.project.name}`), fullPage: true });

      const visitor = await browser.newContext({ viewport: page.viewportSize() ?? undefined });
      const anonymous = await visitor.newPage();
      await anonymous.goto(`/${locale}/needs`);
      await expect(anonymous.getByRole("heading", { name: board, level: 1 })).toBeVisible();
      const card = anonymous.getByRole("article", { name: title });
      await expect(card).toBeVisible();
      expect(await scrollsSideways(anonymous)).toBe(false);
      await anonymous.screenshot({ path: shot(`board-${locale}-${testInfo.project.name}`), fullPage: true });
      await card.getByRole("button", { name: pledgeWord }).click();
      await expect(anonymous.getByRole("dialog")).toBeVisible();
      expect(await scrollsSideways(anonymous)).toBe(false);
      await anonymous.screenshot({ path: shot(`pledge-${locale}-${testInfo.project.name}`), fullPage: true });
      await visitor.close();
    } finally {
      await unlist(baseURL!, institution);
    }
  });
}
