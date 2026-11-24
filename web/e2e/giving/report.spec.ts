import { mkdirSync, readFileSync } from "node:fs";
import path from "node:path";
import type { APIRequestContext, Page } from "@playwright/test";
import { apiAs, createAccount, signIn, type Account } from "../support/accounts";
import { approvedInstitution, bookingBy, colomboDate, listed, slotOf, type Institution } from "../support/institutions";
import { grantInstitutionRole } from "../support/services";
import { sql } from "../support/sql";
import { expect, scrollsSideways, test } from "../support/test";

/*  Plan 09, D.4: the monthly report on /reports, its file and its printed page, the donations list's
    file, and the two dashboard lines. What the figures mean is the API's integration tests
    (ReportEndpointsTests); these prove the screens show them, in words, on a 320 px phone too.

    The engine only books ahead, so a booking is made through the API and then moved into last month
    with its slot, which gives a whole month to report on whatever day this runs.                  */

function shot(name: string): string {
  const dir = path.join(__dirname, "..", ".screens");
  mkdirSync(dir, { recursive: true });
  return path.join(dir, `report-${name}.png`);
}

const today = colomboDate(0);
const thisMonth = today.slice(0, 7);

/** The first of last month, and how many days it had. */
function lastMonth(): { first: string; month: string; days: number } {
  const [y, m] = thisMonth.split("-").map(Number);
  const first = new Date(Date.UTC(y, m - 2, 1)).toISOString().slice(0, 10);
  return { first, month: first.slice(0, 7), days: new Date(Date.UTC(y, m - 1, 0)).getUTCDate() };
}

function userId(account: Account): string {
  return sql("SELECT Id FROM auth.Users WHERE PhoneNumber LIKE '%$(last9)';", { last9: account.phone.replace(/\D/g, "").slice(-9) })[0]!;
}

function institutionId(institution: Institution): string {
  return sql("SELECT Id FROM org.Institutions WHERE Slug = '$(slug)';", { slug: institution.slug })[0]!;
}

async function posted(api: APIRequestContext, url: string, data: unknown) {
  const response = await api.post(url, { data });
  if (!response.ok()) throw new Error(`${url}: ${response.status()} ${await response.text()}`);
  return (await response.json()) as { publicId: string; receiptNo: string };
}

const cash = (amount: number, date: string, method = "CASH", donorName = "E2E Walk-in Giver") => ({
  kind: "Money",
  amount,
  method,
  date,
  donorName,
});

/** A need on the board, through the API; its public id. */
async function openNeed(api: APIRequestContext, title: string, quantity: number): Promise<string> {
  const { publicId } = await posted(api, "/api/institution/needs", {
    title,
    description: null,
    category: "STATIONERY",
    unit: "PIECES",
    quantity,
    priority: "Normal",
    deadline: null,
    version: null,
  });
  const list = (await (await api.get("/api/institution/needs")).json()) as { publicId: string; version: string }[];
  await posted(api, `/api/institution/needs/${publicId}/open`, { version: list.find((n) => n.publicId === publicId)!.version });
  return publicId;
}

/** A month to report on: a booked day moved into last month, money by two methods, and a voided gift. */
async function monthBehind(baseURL: string, institution: Institution, giver: Account) {
  const last = lastMonth();
  const slot = await slotOf(baseURL, institution, { name: "E2E Lunch", requiresApproval: false });
  const donor = await createAccount(baseURL, "E2E Report Donor");
  await bookingBy(baseURL, donor, slot, colomboDate(1));
  sql(
    `UPDATE booking.MealSlots SET CreatedAt = DATEADD(YEAR, -1, CreatedAt) WHERE PublicId = '$(slot)';
     UPDATE booking.SlotInstances SET SlotDate = '$(first)' WHERE MealSlotId = (SELECT Id FROM booking.MealSlots WHERE PublicId = '$(slot)');`,
    { slot, first: last.first },
  );

  const api = await apiAs(baseURL, institution.owner);
  try {
    await posted(api, "/api/institution/donations", cash(5000, last.first));
    await posted(api, "/api/institution/donations", cash(1250.5, last.first, "CHEQUE"));
    const wrong = await posted(api, "/api/institution/donations", cash(999, last.first, "CASH", "E2E Wrong Giver"));

    // The screens that void are donations.spec.ts's subject; here it only has to have happened.
    sql(
      `UPDATE giving.Donations SET IsVoided = 1, VoidedBy = '$(by)', VoidReason = N'Recorded twice', VoidedAt = SYSUTCDATETIME()
       WHERE InstitutionId = $(institution) AND ReceiptNo = '$(receipt)';`,
      { by: userId(giver), institution: institutionId(institution), receipt: wrong.receiptNo },
    );
    return { ...last, voided: wrong.receiptNo };
  } finally {
    await api.dispose();
  }
}

async function previousMonth(page: Page, name: string) {
  await page.getByRole("button", { name }).click();
}

test("the owner reads last month, saves it as a file and prints it @phone", async ({ page, baseURL }, testInfo) => {
  test.setTimeout(180_000);
  const institution = await approvedInstitution(baseURL!, `E2E Report Vihara ${Date.now()}`);
  const manager = await createAccount(baseURL!, "E2E Second Person");
  grantInstitutionRole(manager, institution, "MANAGER");
  const last = await monthBehind(baseURL!, institution, manager);
  const percent = Math.round(100 / last.days);

  await signIn(page, institution.owner);
  await page.goto("/reports");
  await expect(page.getByRole("heading", { name: "Monthly report", level: 1 })).toBeVisible();

  // ---- this month has only just begun for this institution
  await expect(page.getByText("This month is not over.", { exact: false })).toBeVisible();
  await expect(page.getByText("No money received this month.")).toBeVisible();
  await expect(page.getByRole("button", { name: "Next month" })).toBeDisabled();

  // ---- last month
  await previousMonth(page, "Previous month");
  const money = page.getByRole("region", { name: "Money received by method" });
  await expect(money.getByText("LKR 5,000.00")).toBeVisible();
  await expect(money.getByText("LKR 1,250.50")).toBeVisible();
  await expect(money.getByText("LKR 6,250.50")).toBeVisible();
  await expect(page.getByText("This month is not over.", { exact: false })).toBeHidden();

  const slots = page.getByRole("region", { name: "Utilisation per meal slot" });
  await expect(slots.getByText("E2E Lunch")).toBeVisible();
  await expect(slots.getByText(`1 of ${last.days} days (${percent}%)`)).toBeVisible();

  const bookings = page.getByRole("region", { name: "Bookings by status" });
  await expect(bookings.getByRole("definition")).toHaveText(["0", "1", "0", "0", "0", "0", "1"]);

  // Voided: listed apart, and in no total.
  const voided = page.getByRole("region", { name: "Voided gifts" });
  await expect(voided.getByText("E2E Wrong Giver")).toBeVisible();
  await expect(voided.getByRole("link", { name: last.voided })).toBeVisible();
  await expect(voided.getByText("E2E Second Person", { exact: false })).toBeVisible();
  await expect(voided.getByText("Recorded twice", { exact: false })).toBeVisible();
  await expect(money.getByText("LKR 999.00")).toHaveCount(0);

  expect(await scrollsSideways(page)).toBe(false);
  await page.screenshot({ path: shot(`month-${testInfo.project.name}`), fullPage: true });

  // ---- the file
  const [download] = await Promise.all([page.waitForEvent("download"), page.getByRole("button", { name: "Save as CSV" }).click()]);
  expect(download.suggestedFilename()).toBe(`sethika-report-${last.month}.csv`);
  const bytes = readFileSync(await download.path());
  expect([...bytes.subarray(0, 3)]).toEqual([0xef, 0xbb, 0xbf]);
  const text = bytes.toString("utf8");
  expect(text).toContain("Total,,6250.50");
  expect(text).toContain(`,1,${last.days},${percent}`);
  expect(text).toContain("E2E Wrong Giver");

  // ---- on paper: the shell and the buttons go, and the page says whose report it is
  const print = page.getByRole("button", { name: "Print or save as PDF" });
  await expect(print).toBeVisible();
  await page.emulateMedia({ media: "print" });
  await expect(print).toBeHidden();
  await expect(page.locator("header").first()).toBeHidden();
  const paper = page.getByRole("article", { name: "The month's report" });
  await expect(paper.getByText(institution.name)).toBeVisible();
  await expect(paper.getByText("Monthly report:", { exact: false })).toBeVisible();
  await page.screenshot({ path: shot(`print-${testInfo.project.name}`), fullPage: true });
  await page.emulateMedia({ media: "screen" });
});

for (const [locale, title, money] of [
  ["si", "මාසික වාර්තාව", "ක්‍රමය අනුව ලැබුණු මුදල්"],
  ["ta", "மாதாந்திர அறிக்கை", "முறையின்படி பெற்ற பணம்"],
] as const) {
  test(`the report reads in ${locale} and fits a phone @phone`, async ({ page, baseURL }, testInfo) => {
    test.setTimeout(180_000);
    const institution = await approvedInstitution(baseURL!, `E2E Report ${locale} ${Date.now()}`, locale);
    const manager = await createAccount(baseURL!, "E2E Second Person", locale);
    grantInstitutionRole(manager, institution, "MANAGER");
    await monthBehind(baseURL!, institution, manager);

    await signIn(page, institution.owner, locale);
    await page.goto(`/${locale}/reports`);
    await expect(page.getByRole("heading", { name: title, level: 1 })).toBeVisible();

    await page.getByRole("group").getByRole("button").first().click();
    await expect(page.getByRole("region", { name: money })).toBeVisible();
    await expect(page.getByText("E2E Wrong Giver")).toBeVisible();

    expect(await scrollsSideways(page)).toBe(false);
    await page.screenshot({ path: shot(`${locale}-${testInfo.project.name}`), fullPage: true });
  });
}

test("each role sees its own giving line, and staff have no report @phone", async ({ page, baseURL, browser }, testInfo) => {
  test.setTimeout(180_000);
  const institution = await approvedInstitution(baseURL!, `E2E Line Vihara ${Date.now()}`);
  const staff = await createAccount(baseURL!, "E2E Counter Staff");
  grantInstitutionRole(staff, institution, "STAFF");

  const api = await apiAs(baseURL!, institution.owner);
  await posted(api, "/api/institution/donations", cash(1500, today));
  await openNeed(api, `E2E Books ${Date.now()}`, 10);
  await api.dispose();

  // ---- staff: the line, the gifts, and no file or report
  await signIn(page, staff);
  await page.goto("/");
  const line = page.getByTestId("giving-line");
  await expect(line.getByText("LKR 1,500.00")).toBeVisible();
  await expect(line.getByRole("definition").nth(1)).toHaveText("1");
  await expect(line.getByRole("definition").nth(2)).toHaveText("0");
  await expect(page.getByRole("link", { name: "Open the gifts" })).toBeVisible();
  await expect(page.getByRole("link", { name: "Open the report" })).toHaveCount(0);
  expect(await scrollsSideways(page)).toBe(false);
  await page.screenshot({ path: shot(`line-${testInfo.project.name}`), fullPage: true });

  await page.goto("/giving/donations");
  await expect(page.getByRole("heading", { name: "Donations and receipts", level: 1 })).toBeVisible();
  await expect(page.getByRole("button", { name: "Record a gift" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Save as CSV" })).toHaveCount(0);

  await page.goto("/reports");
  await expect(page.getByText("Your role in this institution does not include this page.")).toBeVisible();
  await expect(page.getByRole("region", { name: "Money received by method" })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Save as CSV" })).toHaveCount(0);

  // ---- the owner: the line leads to the report, and the list has its file
  const ownerPage = await (await browser.newContext({ viewport: page.viewportSize() })).newPage();
  await signIn(ownerPage, institution.owner);
  await ownerPage.goto("/");
  await ownerPage.getByRole("link", { name: "Open the report" }).click();
  await expect(ownerPage.getByRole("heading", { name: "Monthly report", level: 1 })).toBeVisible();

  await ownerPage.goto("/giving/donations");
  const [download] = await Promise.all([ownerPage.waitForEvent("download"), ownerPage.getByRole("button", { name: "Save as CSV" }).click()]);
  expect(download.suggestedFilename()).toBe(`sethika-donations-${thisMonth}.csv`);
  const bytes = readFileSync(await download.path());
  expect([...bytes.subarray(0, 3)]).toEqual([0xef, 0xbb, 0xbf]);
  expect(bytes.toString("utf8")).toContain(",Money,1500.00,Cash,");
  await ownerPage.context().close();
});

test("a donor's home shows their latest gifts and the pledges still owed @phone", async ({ page, baseURL }, testInfo) => {
  test.setTimeout(180_000);
  const stamp = Date.now();
  const institution = await approvedInstitution(baseURL!, `E2E My Giving ${stamp}`);
  await listed(baseURL!, institution);
  const donor = await createAccount(baseURL!, "E2E Home Donor");

  // ---- nothing yet
  await signIn(page, donor);
  await page.goto("/");
  const panel = page.getByRole("region", { name: "My giving" });
  await expect(panel.getByText("Your gifts and pledges will show here.")).toBeVisible();

  // ---- two pledges: one delivered, so it has a receipt; one still owed
  const owner = await apiAs(baseURL!, institution.owner);
  const need = await openNeed(owner, `E2E Rice ${stamp}`, 20);
  const donorApi = await apiAs(baseURL!, donor);
  const delivered = await posted(donorApi, `/api/needs/${need}/pledges`, { quantity: 5, expectedDate: null });
  await posted(donorApi, `/api/needs/${need}/pledges`, { quantity: 3, expectedDate: null });
  await donorApi.dispose();
  const pledges = (await (await owner.get(`/api/institution/needs/${need}/pledges`)).json()) as { publicId: string; version: string }[];
  await posted(owner, `/api/institution/pledges/${delivered.publicId}/receive`, {
    quantity: 5,
    version: pledges.find((p) => p.publicId === delivered.publicId)!.version,
  });
  await owner.dispose();

  await page.reload();
  const receipts = panel.getByRole("list", { name: "My latest gifts" });
  await expect(receipts.getByRole("listitem")).toHaveCount(1);
  await expect(receipts.getByRole("link", { name: /^Receipt RCP-/ })).toBeVisible();
  const owed = panel.getByRole("list", { name: "Pledges still to give" });
  await expect(owed.getByRole("listitem")).toHaveCount(1);
  await expect(owed.getByText("3 pledged", { exact: false })).toBeVisible();
  expect(await scrollsSideways(page)).toBe(false);
  await page.screenshot({ path: shot(`donor-${testInfo.project.name}`), fullPage: true });

  await panel.getByRole("link", { name: "All my giving" }).click();
  await expect(page).toHaveURL(/\/giving\/mine$/);
});
