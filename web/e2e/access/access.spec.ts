import { mkdirSync } from "node:fs";
import path from "node:path";
import type { Page } from "@playwright/test";
import { createAccount, lastTextId, signIn, stepUp } from "../support/accounts";
import { approvedInstitution } from "../support/institutions";
import { clipsSideways, expect, scrollsSideways, test } from "../support/test";

/*  Plan 04, U3: the access screens, with real accounts end to end. An owner makes a role and says
    what it may do in the matrix (a step-up on save), invites someone by phone (another step-up),
    and the person accepts from their own dashboard and finds the institution's screens in their
    menu. Each refusal a person meets says what to do next.                                    */

const screens = path.join(__dirname, "..", ".screens");

async function shot(page: Page, project: string, name: string) {
  mkdirSync(screens, { recursive: true });
  await page.screenshot({ path: path.join(screens, `${project}-${name}.png`), fullPage: true });
}

/** A pill in the role editor: the screen's group, the action's label. */
const pill = (page: Page, screen: string, action: string) =>
  page.getByRole("group", { name: screen, exact: true }).locator("label").filter({ hasText: new RegExp(`^${action}`) });

test("an owner makes a role, invites someone into it, and they accept and find its screens @phone", async ({ page, baseURL, browser }, testInfo) => {
  const institution = await approvedInstitution(baseURL!, `E2E Access Kovil ${Date.now()}`);
  const owner = institution.owner;
  await signIn(page, owner);

  // ---- the roles, and a new one
  await page.goto("/access/roles");
  await expect(page.getByRole("heading", { level: 1, name: "Roles" })).toBeVisible();
  const list = page.getByRole("navigation", { name: "Roles in your institution" });
  await expect(list.getByRole("button", { name: /Owner/ })).toBeVisible();
  await shot(page, testInfo.project.name, "en-access-roles");

  await page.getByRole("button", { name: "New role" }).click();
  const sheet = page.getByRole("dialog");
  await sheet.getByLabel(/^Name/).fill("Kitchen helper");
  await expect(sheet.getByLabel(/^Code/)).toHaveValue("KITCHEN_HELPER");
  await sheet.getByLabel(/^Level/).selectOption({ label: "Staff" });
  await sheet.getByRole("button", { name: "Create role" }).click();
  await expect(page.getByText("Kitchen helper is ready.").first()).toBeVisible();
  await expect(page.getByRole("heading", { name: "What Kitchen helper may do" })).toBeVisible();

  // Ticking Approve ticks the calendar's View and the slots' View it needs (10_RbacCatalogue.sql).
  await pill(page, "Booking calendar", "Approve").click();
  await expect(pill(page, "Booking calendar", "Approve").getByRole("checkbox")).toBeChecked();
  await expect(pill(page, "Booking calendar", "View").getByRole("checkbox")).toBeChecked();
  await expect(pill(page, "Meals and days", "View").getByRole("checkbox")).toBeChecked();
  // A staff-level role can never manage access: that pill is closed, and says why.
  await expect(pill(page, "Roles", "Change").getByRole("checkbox")).toBeDisabled();
  expect(await scrollsSideways(page)).toBe(false);
  expect(await clipsSideways(page)).toEqual([]);
  await shot(page, testInfo.project.name, "en-access-role-editor");

  let before = lastTextId(owner.phone);
  await page.getByRole("button", { name: "Save changes" }).click();
  await stepUp(page, owner, before);
  await expect(page.getByText("Saved. Everyone holding Kitchen helper has the new permissions now.").first()).toBeVisible();
  await page.reload();
  await page.getByRole("navigation", { name: "Roles in your institution" }).getByRole("button", { name: /Kitchen helper/ }).click();
  await expect(pill(page, "Booking calendar", "Approve").getByRole("checkbox")).toBeChecked();

  // ---- inviting: a number with no account is told plainly, then a real one is invited
  const helper = await createAccount(baseURL!, "E2E Kitchen Helper");
  await page.goto("/access/members");
  await expect(page.getByRole("heading", { level: 1, name: "Staff" })).toBeVisible();
  await page.getByRole("button", { name: "Invite someone" }).click();
  let invite = page.getByRole("dialog");
  await invite.getByLabel(/^Their phone number/).fill("0770000001");
  await invite.getByLabel(/^Role/).selectOption({ label: "Kitchen helper · Staff" });
  before = lastTextId(owner.phone);
  await invite.getByRole("button", { name: "Send invitation" }).click();
  await stepUp(page, owner, before);
  await expect(invite.getByText("No Sethika account has this number.", { exact: false })).toBeVisible();

  await invite.getByLabel(/^Their phone number/).fill(helper.phone);
  await invite.getByRole("button", { name: "Send invitation" }).click();
  await expect(page.getByText(`Invitation sent to ${helper.phone}.`).first()).toBeVisible();
  invite = page.getByRole("dialog");
  await expect(invite).toBeHidden();
  await expect(page.getByRole("region", { name: "Waiting for an answer" }).getByText("E2E Kitchen Helper")).toBeVisible();
  expect(await scrollsSideways(page)).toBe(false);
  expect(await clipsSideways(page)).toEqual([]);
  await shot(page, testInfo.project.name, "en-access-members");

  // ---- the invitee accepts from their own dashboard and opens the institution
  const context = await browser.newContext({ viewport: page.viewportSize() ?? undefined });
  const helperPage = await context.newPage();
  await signIn(helperPage, helper);
  await helperPage.goto("/");
  const card = helperPage.getByRole("region", { name: "Invitations" });
  await expect(card.getByText(`invited you to join ${institution.name} as Kitchen helper.`, { exact: false })).toBeVisible();
  await card.getByRole("button", { name: "Accept" }).click();
  await card.getByRole("button", { name: `Open ${institution.name}` }).click();
  await expect(helperPage.getByText(`Here is ${institution.name} at a glance.`)).toBeVisible();
  // The role reads the calendar, so the dashboard lays today's table (plan 05, R4).
  await expect(helperPage.getByRole("main").getByRole("heading", { name: "Today's table" })).toBeVisible();
  await context.close();

  // The owner's staff list now has them, holding the new role.
  await page.reload();
  const row = page.getByRole("listitem").filter({ hasText: "E2E Kitchen Helper" });
  await expect(row.getByText("Kitchen helper", { exact: true })).toBeVisible();
  await expect(page.getByRole("region", { name: "Waiting for an answer" })).toHaveCount(0);
});

test("staff without access cannot open the access screens", async ({ applicant: { page } }) => {
  await page.goto("/access/roles");
  await expect(page.getByRole("heading", { level: 1, name: "Roles" })).toBeVisible();
  await expect(page.getByRole("navigation", { name: "Roles in your institution" })).toHaveCount(0);
  await page.goto("/access/members");
  await expect(page.getByRole("button", { name: "Invite someone" })).toHaveCount(0);
});
