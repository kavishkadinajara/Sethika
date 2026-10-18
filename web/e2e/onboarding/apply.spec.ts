import type { Page } from "@playwright/test";
import { field } from "../support/fields";
import { expect, pdf, scrollsSideways, test } from "../support/test";

const stepLabel = (current: number, total: number, name: string) => `Step ${current} of ${total} · ${name}`;

async function fillDetails(page: Page, name: string) {
  await field(page, "Type of institution").selectOption({ index: 1 });
  await field(page, "Name in English").fill(name);
  await field(page, "Registration number").fill(`E2E/${Date.now()}`);
  await field(page, "Registered with").fill("Department of Buddhist Affairs");
  await field(page, "Date registered").fill("2015-06-01");
  await field(page, "Year founded").fill("1950");
  await field(page, "Address").fill("12 Temple Road");
  await field(page, "Town or city").fill("Galle");
  await field(page, "District").selectOption({ index: 1 });
  await field(page, "Latitude").fill("6.0329");
  await field(page, "Longitude").fill("80.2168");
  await field(page, "Office phone").fill("0912234567");
  await field(page, "Full name").fill("E2E Applicant");
  await field(page, "NIC number").fill("199012345678");
  await field(page, "Your role").fill("Secretary");
  await field(page, "Phone").fill("0771234567");
}

async function expectNoSidewaysScroll(page: Page) {
  expect(await scrollsSideways(page), `sideways scroll on ${page.url()}`).toBe(false);
}

test("an application goes from a blank form to submitted, with every rule shown on the way @phone", async ({ applicant: { page } }) => {
  await page.goto("/apply");
  await expect(page.getByText("You have not applied yet.")).toBeVisible();
  await page.getByRole("link", { name: "Start an application" }).click();

  // ---- details: nothing filled in is refused, with the fields marked
  await expect(page.getByText(stepLabel(1, 4, "Institution and you"))).toBeVisible();
  await expectNoSidewaysScroll(page);
  await page.getByRole("button", { name: "Save and continue" }).click();
  await expect(page.getByText("Some answers need another look. They are marked below.")).toBeVisible();
  expect(await page.locator('[aria-invalid="true"]').count()).toBeGreaterThan(3);
  await expect(page).toHaveURL(/\/apply\/new$/);

  // A map link appears once there is a pin to show.
  await fillDetails(page, "E2E Sri Sumana Temple");
  await expect(page.getByRole("link", { name: "Check the pin on the map" })).toHaveAttribute("href", /openstreetmap\.org.*6\.0329.*80\.2168/);
  await page.getByRole("button", { name: "Save and continue" }).click();

  // ---- office bearers: the draft has its own address now
  await expect(page).toHaveURL(/\/apply\/REQ-\d{4}-\d+$/);
  await expect(page.getByText(stepLabel(2, 4, "Office bearers"))).toBeVisible();
  await expectNoSidewaysScroll(page);
  const bearer = page.getByRole("group", { name: "Office bearer 1" });
  await field(bearer, "Full name").fill("E2E Bearer");
  await field(bearer, "NIC number").fill("198512345678");
  await field(bearer, "Role").fill("Chairman");
  await field(bearer, "Phone").fill("0772345678");
  // The first office bearer starts as the signatory; without one the step refuses to save.
  await expect(bearer.getByLabel("Can sign for the institution")).toBeChecked();
  await bearer.getByLabel("Can sign for the institution").uncheck();
  await page.getByRole("button", { name: "Save and continue" }).click();
  await expect(page.getByText("Mark at least one office bearer who can sign for the institution.")).toBeVisible();
  await bearer.getByLabel("Can sign for the institution").check();
  await page.getByRole("button", { name: "Save and continue" }).click();

  // ---- documents: none uploaded yet, a wrong type refused before it is sent
  await expect(page.getByText(stepLabel(3, 4, "Documents"))).toBeVisible();
  await expectNoSidewaysScroll(page);
  await page.getByRole("button", { name: "Save and continue" }).click();
  await expect(page.getByText("Upload every required document to continue.")).toBeVisible();

  const forms = page.locator("form").filter({ has: page.getByText("Required", { exact: true }) });
  const count = await forms.count();
  expect(count).toBeGreaterThan(0);

  const first = forms.first();
  await first.locator('input[type="file"]').setInputFiles({ name: "notes.txt", mimeType: "text/plain", buffer: Buffer.from("not a certificate") });
  await expect(first.getByText(/This type of file is not accepted here/)).toBeVisible();

  for (let i = 0; i < count; i++) {
    const form = forms.nth(i);
    await form.locator('input[type="file"]').setInputFiles(pdf());
    await field(form, "Reference number").fill("E2E-REF-1");
    await field(form, "Date issued").fill("2015-06-01");
    if (await field(form, "Expiry date").count()) await field(form, "Expiry date").fill("2030-12-31");
    await form.getByRole("button", { name: "Upload" }).click();
    await expect(form.getByText("Uploaded: certificate.pdf · Waiting for review")).toBeVisible();
  }
  await page.getByRole("button", { name: "Save and continue" }).click();

  // ---- declaration: nothing sent until it is confirmed
  await expect(page.getByText(stepLabel(4, 4, "Declaration"))).toBeVisible();
  await expect(page.getByText("Everything required is in place.")).toBeVisible();
  await expectNoSidewaysScroll(page);
  await page.getByRole("button", { name: "Submit the application" }).click();
  await expect(page.getByText("Confirm the declaration to submit.")).toBeVisible();
  await page.getByRole("checkbox").check();
  await page.getByRole("button", { name: "Submit the application" }).click();

  // ---- submitted: the status, and the list agrees
  await expect(page.getByText("A reviewer will look at your application. We will text you when there is news.")).toBeVisible();
  await expect(page.getByText("Submitted", { exact: true }).first()).toBeVisible();
  await expectNoSidewaysScroll(page);
  await page.getByRole("link", { name: "All your applications" }).click();
  await expect(page.getByText("E2E Sri Sumana Temple")).toBeVisible();
});

test("a draft reopens at the first step left to do, and can be withdrawn", async ({ applicant: { page } }) => {
  await page.goto("/apply/new");
  await fillDetails(page, "E2E Kovil Draft");
  await page.getByRole("button", { name: "Save and continue" }).click();
  await expect(page.getByText(stepLabel(2, 4, "Office bearers"))).toBeVisible();
  const address = page.url();

  // Coming back later: the details are kept, and it opens on office bearers.
  await page.goto("/apply");
  await expect(page.getByText("E2E Kovil Draft")).toBeVisible();
  await page.goto(address);
  await expect(page.getByText(stepLabel(2, 4, "Office bearers"))).toBeVisible();
  await page.getByRole("button", { name: "Back" }).click();
  await expect(field(page, "Name in English")).toHaveValue("E2E Kovil Draft");

  // Withdrawing asks first, and "Keep it" changes nothing.
  await page.getByRole("button", { name: "Withdraw the application" }).click();
  await expect(page.getByText("Withdraw this application? It cannot be undone.")).toBeVisible();
  await page.getByRole("button", { name: "Keep it" }).click();
  await expect(page.getByText("Withdraw this application? It cannot be undone.")).toBeHidden();

  await page.getByRole("button", { name: "Withdraw the application" }).click();
  await page.getByRole("button", { name: "Withdraw", exact: true }).click();
  await expect(page.getByText("You withdrew this application.")).toBeVisible();
});

test("an application number that is not the applicant's own is not found", async ({ applicant: { page } }) => {
  await page.goto("/apply/REQ-2026-999999");
  await expect(page.getByText("There is no application with this number.")).toBeVisible();
});
