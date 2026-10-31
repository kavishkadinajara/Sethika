import { apiAs, createAccount, grantPlatformRole } from "../support/accounts";
import { submittedApplication } from "../support/applications";
import { field } from "../support/fields";
import { expect, scrollsSideways, test } from "../support/test";

const question = "Please send a clearer copy of the registration certificate.";

test("the applicant answers a reviewer's questions, and may change details first @phone", async ({ applicant: { page, account }, baseURL }) => {
  const asApplicant = await apiAs(baseURL!, account);
  const requestNo = await submittedApplication(asApplicant, "E2E Questions Temple");
  await asApplicant.dispose();

  // The reviewer has no screen yet (5.6): the review is started and the question asked through the API.
  const reviewer = await createAccount(baseURL!, "E2E Reviewer");
  grantPlatformRole(reviewer, "PLATFORM_REVIEWER");
  const asReviewer = await apiAs(baseURL!, reviewer);
  const at = `/api/admin/registrations/${requestNo}`;
  expect((await asReviewer.post(`${at}/start-review`)).status()).toBe(204);
  expect((await asReviewer.post(`${at}/questions`, { data: { questions: [question] } })).status()).toBe(204);
  await asReviewer.dispose();

  await page.goto("/apply");
  await expect(page.getByText("Questions for you").first()).toBeVisible();
  await page.goto(`/apply/${requestNo}`);
  await expect(page.getByRole("heading", { name: "The reviewer's questions" })).toBeVisible();
  await expect(page.getByText(question)).toBeVisible();
  expect(await scrollsSideways(page)).toBe(false);

  // Changing details while questions are open: three steps, no declaration again.
  await page.getByRole("button", { name: "Change details, office bearers or documents" }).click();
  await expect(page.getByText("Step 1 of 3 · Institution and you")).toBeVisible();
  await field(page, "Town or city").fill("Matara");
  await page.getByRole("button", { name: "Save and continue" }).click();
  await expect(page.getByText("Step 2 of 3 · Office bearers")).toBeVisible();
  await page.getByRole("button", { name: "Save and continue" }).click();
  await expect(page.getByText("Step 3 of 3 · Documents")).toBeVisible();
  await page.getByRole("button", { name: "Save and continue" }).click();

  // Back on the status, the question still waits for its answer.
  await page.getByRole("button", { name: "Send your answers" }).click();
  await expect(page.getByText("Answer every question.")).toBeVisible();
  await field(page, "Your answer").fill("A clearer copy is uploaded now.");
  await page.getByRole("button", { name: "Send your answers" }).click();

  await expect(page.getByText("You answered: A clearer copy is uploaded now.")).toBeVisible();
  await expect(page.getByRole("button", { name: "Send your answers" })).toBeHidden();
});
