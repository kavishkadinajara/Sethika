import { latestCode, newPhone, password } from "../support/accounts";
import { expect, scrollsSideways, test } from "../support/test";

test("a new person signs up with a texted code and can then register an institution @phone", async ({ page }) => {
  const phone = newPhone();
  await page.goto("/signup");

  await page.getByRole("button", { name: "Send code" }).click();
  await expect(page.getByText("Enter your mobile number.")).toBeVisible();

  await page.getByLabel("Mobile number").fill(phone);
  await page.getByRole("button", { name: "Send code" }).click();
  await expect(page.getByText(`We sent a code to ${phone}.`)).toBeVisible();

  // The sixth digit sends the code by itself. A wrong code first: refused, and the person can try again.
  const { code } = await latestCode(phone);
  const wrong = code === "000000" ? "111111" : "000000";
  await page.getByLabel("6-digit code").fill(wrong);
  await expect(page.getByText(/That code is not right\. \d tries? left\./)).toBeVisible();
  await expect(page.getByRole("heading", { name: "About you" })).toBeHidden();

  await page.getByLabel("6-digit code").fill(code);
  await expect(page.getByRole("heading", { name: "About you" })).toBeVisible();
  await page.getByLabel("Full name").fill("E2E Signup Person");
  // A weak password: the checklist only guides, the API decides - and its refusal brings the
  // person back to this step with the password marked, the documents still accepted.
  await page.getByLabel("Password").fill("short");
  await page.getByRole("button", { name: "Continue" }).click();

  await expect(page.getByRole("heading", { name: "Before you join" })).toBeVisible();
  await page.getByRole("button", { name: "Create account" }).click();
  await expect(page.getByText("Please accept every document to continue.")).toBeVisible();
  for (const box of await page.getByRole("checkbox").all()) await box.check();
  expect(await scrollsSideways(page)).toBe(false);
  await page.getByRole("button", { name: "Create account" }).click();

  await expect(page.getByRole("heading", { name: "About you" })).toBeVisible();
  await expect(page.getByLabel("Password")).toHaveAttribute("aria-invalid", "true");
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Continue" }).click();
  await expect(page.getByRole("heading", { name: "Before you join" })).toBeVisible();
  for (const box of await page.getByRole("checkbox").all()) await expect(box).toBeChecked();
  await page.getByRole("button", { name: "Create account" }).click();

  // Signed in at once, and allowed to apply: the sidebar's "Register an institution" section (in
  // the menu sheet on a phone) leads there; the dashboard says only what today needs (plan 05, X3).
  await page.waitForURL((url) => !url.pathname.endsWith("/signup"));
  await page.goto("/");
  const small = (page.viewportSize()?.width ?? 1280) < 1024;
  if (small) await page.getByRole("button", { name: "Open the menu" }).click();
  const menu = small ? page.getByRole("dialog", { name: "Main menu" }) : page.getByRole("complementary", { name: "Sidebar" });
  await menu.getByRole("link", { name: "My applications" }).click();
  await expect(page.getByText("You have not applied yet.")).toBeVisible();
  expect(await scrollsSideways(page)).toBe(false);
});
