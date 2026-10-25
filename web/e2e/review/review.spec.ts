import { mkdirSync } from "node:fs";
import path from "node:path";
import type { Page } from "@playwright/test";
import { apiAs, createAccount, grantPlatformRole, lastTextId, signIn, stepUp, type Account } from "../support/accounts";
import { submittedApplication } from "../support/applications";
import { field } from "../support/fields";
import { expect, scrollsSideways, test } from "../support/test";

/*  Plan 02, 5.6: the reviewer's and the administrator's screens. Each test makes its own
    applicant and application through the API, and its own staff account, so the tests run in
    parallel; the queue holds other tests' applications too, so each finds its own by number.  */

/** Where screenshots for a person - or Claude - to look at are kept between runs. */
function screenDir(): string {
  const dir = path.join(__dirname, "..", ".screens");
  mkdirSync(dir, { recursive: true });
  return dir;
}

async function applicationBy(baseURL: string, name: string): Promise<{ applicant: Account; requestNo: string }> {
  const applicant = await createAccount(baseURL, "E2E Applicant");
  const api = await apiAs(baseURL, applicant);
  const requestNo = await submittedApplication(api, name);
  await api.dispose();
  return { applicant, requestNo };
}

async function staff(page: Page, baseURL: string, role: "PLATFORM_REVIEWER" | "PLATFORM_ADMIN"): Promise<Account> {
  const account = await createAccount(baseURL, role === "PLATFORM_ADMIN" ? "E2E Admin" : "E2E Reviewer");
  grantPlatformRole(account, role);
  await signIn(page, account);
  return account;
}

/** Accepts every mandatory document and answers every checklist item yes. */
async function reviewEverything(page: Page) {
  const documents = page.locator("li").filter({ has: page.getByRole("button", { name: "Accept", exact: true }) });
  const count = await documents.count();
  for (let i = 0; i < count; i++) {
    const document = documents.nth(i);
    await document.getByRole("button", { name: "Accept", exact: true }).click();
    await expect(document.getByText("Accepted", { exact: true })).toBeVisible();
  }
  for (const yes of await page.getByRole("radio", { name: "Yes" }).all()) await yes.check();
  await page.getByRole("button", { name: "Save the checklist" }).click();
  await expect(/* the toast, and its screen-reader copy */ page.getByText("Checklist saved.").first()).toBeVisible();
}

test("a reviewer starts a review, judges the documents, answers the checklist and asks a question @phone", async ({ page, baseURL }) => {
  const { requestNo } = await applicationBy(baseURL!, "E2E Review Temple");
  await staff(page, baseURL!, "PLATFORM_REVIEWER");

  // The dashboard leads to the queue, where the new application waits.
  await page.goto("/");
  await page.getByRole("main").getByRole("link", { name: /Registration requests/ }).click();
  await expect(page.getByRole("heading", { name: "Institution applications" })).toBeVisible();
  const row = page.locator("li").filter({ hasText: requestNo });
  await expect(row.getByText("Submitted", { exact: true })).toBeVisible();
  expect(await scrollsSideways(page)).toBe(false);

  // The filter is in the address: "Approved" does not hold it, "New" does.
  await field(page, "Show").selectOption({ label: "Approved" });
  await expect(page).toHaveURL(/status=Approved/);
  await expect(page.locator("li").filter({ hasText: requestNo })).toHaveCount(0);
  await field(page, "Show").selectOption({ label: "New" });
  await row.getByRole("link", { name: /Open/ }).click();

  // Nothing to judge until the review starts.
  await expect(page.getByRole("heading", { name: "E2E Review Temple" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Accept", exact: true })).toHaveCount(0);
  await page.getByRole("button", { name: "Start the review" }).click();
  await expect(page.getByText("Under review", { exact: true }).first()).toBeVisible();
  expect(await scrollsSideways(page)).toBe(false);

  // A refusal needs a reason the applicant can act on.
  // Found by its heading: its buttons come and go as the refusal form opens and closes.
  const name = await page.locator("li").filter({ has: page.getByRole("button", { name: "Accept", exact: true }) }).first().getByRole("heading").textContent();
  const first = page.locator("li").filter({ has: page.getByRole("heading", { name: name!, exact: true }) });
  await first.getByRole("button", { name: "Do not accept" }).click();
  await first.getByRole("button", { name: "Save", exact: true }).click();
  await expect(first.getByText("Say what is wrong with the document.")).toBeVisible();
  await field(first, "What is wrong with it").fill("The seal is not readable.");
  await first.getByRole("button", { name: "Save", exact: true }).click();
  await expect(first.getByText("Reviewer: The seal is not readable.")).toBeVisible();
  await expect(first.getByText("Not accepted", { exact: true })).toBeVisible();

  // The checklist: nothing answered is refused; answers are kept after a reload.
  await page.getByRole("button", { name: "Save the checklist" }).click();
  await expect(page.getByText("Answer at least one item first.")).toBeVisible();
  const items = await page.getByRole("radio", { name: "Yes" }).count();
  expect(items).toBeGreaterThan(0);
  await page.getByRole("radio", { name: "Yes" }).first().check();
  await page.getByRole("button", { name: "Save the checklist" }).click();
  await expect(/* the toast, and its screen-reader copy */ page.getByText("Checklist saved.").first()).toBeVisible();
  await page.reload();
  await expect(page.getByRole("radio", { name: "Yes" }).first()).toBeChecked();

  // A reviewer cannot decide; an administrator does.
  await expect(page.getByText("A platform administrator makes the decision.")).toBeVisible();
  await expect(page.getByRole("button", { name: "Approve", exact: true })).toHaveCount(0);

  // A question sends the application back to the applicant.
  await page.getByRole("button", { name: "Send the questions" }).click();
  await expect(page.getByText("Write at least one question.")).toBeVisible();
  await field(page, "Question 1").fill("Please send a clearer copy of the certificate.");
  await page.getByRole("button", { name: "Add another question" }).click();
  await field(page, "Question 2").fill("Who signs for the temple?");
  await page.getByRole("button", { name: "Send the questions" }).click();
  await expect(page.getByText("Waiting for answers", { exact: true }).first()).toBeVisible();
  await expect(page.getByText("Not answered yet.")).toHaveCount(2);
  await expect(page.getByText("Questions were sent.", { exact: false })).toBeVisible();
  expect(await scrollsSideways(page)).toBe(false);
  await page.screenshot({ path: path.join(screenDir(), `${test.info().project.name}-en-review-detail.png`), fullPage: true });
});

test("an administrator approves once everything is checked, with a texted code, and the applicant gets in", async ({ page, baseURL, browser }) => {
  const { applicant, requestNo } = await applicationBy(baseURL!, "E2E Approved Kovil");
  const admin = await staff(page, baseURL!, "PLATFORM_ADMIN");

  // The applicant has Sethika open on their applications, in their own account, while the
  // decision is made (B8).
  const context = await browser.newContext();
  const applicantPage = await context.newPage();
  await signIn(applicantPage, applicant);
  await applicantPage.goto("/apply");
  await expect(applicantPage.getByText("E2E Approved Kovil")).toBeVisible();

  await page.goto(`/admin/registrations/${requestNo}`);
  await page.getByRole("button", { name: "Start the review" }).click();

  // Approve waits for every required document and checklist item, and says which.
  await expect(page.getByText("Before it can be approved")).toBeVisible();
  await expect(page.getByRole("button", { name: "Approve", exact: true })).toBeDisabled();
  await reviewEverything(page);
  await expect(page.getByText("Before it can be approved")).toBeHidden();

  await page.getByRole("button", { name: "Approve", exact: true }).click();
  await expect(page.getByText(/Approve E2E Approved Kovil\?/)).toBeVisible();
  const before = lastTextId(admin.phone);
  await page.getByRole("button", { name: "Yes, approve" }).click();
  await stepUp(page, admin, before);

  await expect(page.getByText(/^Approved\. The institution is on Sethika as /)).toBeVisible();
  await expect(page.getByRole("button", { name: "Approve", exact: true })).toHaveCount(0);

  // Told by text, they open the application from the list still on screen - no reload, so the
  // session is still their own - see the decision, and open their institution.
  await applicantPage.locator("li").filter({ hasText: requestNo }).getByRole("link", { name: "Open" }).click();
  await expect(applicantPage.getByText(/^Your institution is on Sethika as /)).toBeVisible();
  await applicantPage.getByRole("button", { name: "Open the institution" }).click();
  await applicantPage.waitForURL((url) => !url.pathname.includes("/apply"));
  await expect(applicantPage.getByText("Here is E2E Approved Kovil at a glance.")).toBeVisible();
  await context.close();

  // Signing in afresh, the owner acts for their only institution at once, and the application
  // screens say plainly why they are closed to it, with the way back to their own account
  // (plan 04, P1).
  const fresh = await browser.newContext();
  const ownerPage = await fresh.newPage();
  await signIn(ownerPage, applicant);
  await ownerPage.goto("/");
  await expect(ownerPage.getByText("Here is E2E Approved Kovil at a glance.")).toBeVisible();
  await ownerPage.goto("/apply");
  await expect(ownerPage.getByText("Applications are made from your own account.", { exact: false })).toBeVisible();
  await ownerPage.getByRole("button", { name: "Switch to my own account" }).click();
  await expect(ownerPage.getByText(requestNo)).toBeVisible();
  await fresh.close();
});

test("an administrator does not approve, with a reason and a note the applicant reads", async ({ page, baseURL, browser }) => {
  const { applicant, requestNo } = await applicationBy(baseURL!, "E2E Rejected Church");
  const admin = await staff(page, baseURL!, "PLATFORM_ADMIN");

  await page.goto(`/admin/registrations/${requestNo}`);
  await page.getByRole("button", { name: "Start the review" }).click();
  await page.getByRole("button", { name: "Do not approve" }).click();

  // A reason is needed; the note is optional but read by the applicant.
  await page.getByRole("button", { name: "Do not approve" }).click();
  await expect(page.getByText("Choose a reason.")).toBeVisible();
  await field(page, "Reason").selectOption({ label: "The institution cannot be registered on Sethika" });
  await field(page, "Note to the applicant").fill("Only religious and care institutions can register.");
  await page.getByRole("button", { name: "Do not approve" }).click();

  // Cancel at the last question changes nothing.
  await page.getByRole("button", { name: "Cancel" }).click();
  await expect(field(page, "Reason")).toBeVisible();
  await page.getByRole("button", { name: "Do not approve" }).click();
  const before = lastTextId(admin.phone);
  await page.getByRole("button", { name: "Yes, do not approve" }).click();
  await stepUp(page, admin, before);

  await expect(page.getByText("Reason: The institution cannot be registered on Sethika")).toBeVisible();
  await expect(page.getByText("Not approved", { exact: true }).first()).toBeVisible();

  const context = await browser.newContext();
  const applicantPage = await context.newPage();
  await signIn(applicantPage, applicant);
  await applicantPage.goto(`/apply/${requestNo}`);
  await expect(applicantPage.getByText("Your application was not approved.")).toBeVisible();
  await expect(applicantPage.getByText("Reviewer's note: Only religious and care institutions can register.")).toBeVisible();
  await context.close();
});

test("an applicant is not let into the review screens", async ({ applicant: { page } }) => {
  await page.goto("/admin/registrations");
  await expect(page.getByText("This account cannot review applications.")).toBeVisible();
  await page.goto("/");
  await expect(page.getByRole("link", { name: /Registration requests/ })).toHaveCount(0);
});

test("the review screens in Sinhala and Tamil fit a phone, with nothing clipped @phone", async ({ page, baseURL }, testInfo) => {
  const { requestNo } = await applicationBy(baseURL!, "E2E Language Temple");
  const reviewer = await createAccount(baseURL!, "E2E Reviewer", "si");
  grantPlatformRole(reviewer, "PLATFORM_REVIEWER");
  await signIn(page, reviewer, "si");

  const screens = screenDir();
  // A Sinhala account: an address without a language lands in Sinhala (A11), so English has its
  // screenshots from the reviewer's test.
  for (const locale of ["si", "ta"] as const) {
    const prefix = `/${locale}`;
    await page.goto(`${prefix}/admin/registrations`);
    await expect(page.locator("li").filter({ hasText: requestNo })).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);
    await page.screenshot({ path: path.join(screens, `${testInfo.project.name}-${locale}-review-queue.png`), fullPage: true });

    await page.goto(`${prefix}/admin/registrations/${requestNo}`);
    await expect(page.locator("html")).toHaveAttribute("lang", locale);
    await expect(page.getByRole("heading", { name: "E2E Language Temple" })).toBeVisible();
    await page.waitForLoadState("networkidle");
    expect(await scrollsSideways(page)).toBe(false);
    const clipped = await page.locator("label, a, button").evaluateAll((elements) =>
      elements.filter((e) => e.scrollWidth > e.clientWidth + 1 && getComputedStyle(e).overflow !== "visible").map((e) => e.textContent),
    );
    expect(clipped).toEqual([]);
    await page.screenshot({ path: path.join(screens, `${testInfo.project.name}-${locale}-review-detail.png`), fullPage: true });
  }
});
