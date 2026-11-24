import { createHash } from "node:crypto";
import { mkdirSync } from "node:fs";
import path from "node:path";
import type { APIRequestContext, Page } from "@playwright/test";
import { createAccount, lastTextId, signIn, stepUp, type Account } from "../support/accounts";
import { field } from "../support/fields";
import { approvedInstitution, listed, unlist, type Institution } from "../support/institutions";
import { sql } from "../support/sql";
import { expect, scrollsSideways, test } from "../support/test";

/*  Plan 09, D.3: the merchant on /giving/payments, "Give online" on /i/[slug], the form the browser posts
    to PayHere, and the return page. No test reaches PayHere: its sandbox address is answered here by a
    stand-in page, and its notification is posted by the test, signed with the test's merchant secret,
    through the web app's own /api rewrite. The rules (signatures, forgeries, repeats, the race, RLS) are
    the API's integration tests (PaymentEndpointsTests); these prove the screens use the API, say what
    happened in words, and fit a 320 px phone.                                                         */

const CHECKOUT = "https://sandbox.payhere.lk/pay/checkout";
const MERCHANT = "1211149";
const SECRET = "e2e-merchant-secret-Mzk1NjI";

const year = new Date().getFullYear();

function shot(name: string): string {
  const dir = path.join(__dirname, "..", ".screens");
  mkdirSync(dir, { recursive: true });
  return path.join(dir, `payments-${name}.png`);
}

const md5 = (text: string) => createHash("md5").update(text).digest("hex").toUpperCase();

/** What PayHere's server would post: form-encoded and signed. */
async function notify(request: APIRequestContext, baseURL: string, orderId: string, amount: string, status: string) {
  const response = await request.post(`${baseURL}/api/payments/payhere/notify`, {
    form: {
      merchant_id: MERCHANT,
      order_id: orderId,
      payment_id: "320025012345",
      payhere_amount: amount,
      payhere_currency: "LKR",
      status_code: status,
      md5sig: md5(MERCHANT + orderId + amount + "LKR" + status + md5(SECRET)),
    },
  });
  expect(response.status()).toBe(200);
}

/** Answers PayHere's address with a stand-in page, and keeps what the browser posted to it. */
async function standInForPayHere(page: Page) {
  const seen: { fields: URLSearchParams | null; referer: string | undefined } = { fields: null, referer: undefined };
  await page.route(CHECKOUT, async (route) => {
    seen.fields = new URLSearchParams(route.request().postData() ?? "");
    seen.referer = (await route.request().allHeaders()).referer;
    await route.fulfill({ contentType: "text/html", body: "<!doctype html><title>PayHere</title><h1>Stand-in for PayHere</h1>" });
  });
  return seen;
}

/** The owner enters the merchant, behind a step-up. */
async function setMerchant(page: Page, owner: Account, locale: "" | "si" | "ta" = "", save = "Save") {
  await page.goto(`${locale ? `/${locale}` : ""}/giving/payments`);
  await page.locator('input[autocomplete="off"]').fill(MERCHANT);
  await page.locator('input[type="password"]').fill(SECRET);
  await page.locator('input[type="checkbox"]').check();
  const before = lastTextId(owner.phone);
  await page.getByRole("button", { name: save, exact: true }).click();
  await stepUp(page, owner, before, locale);
  await expect(page.getByTestId("merchant-saved")).toBeVisible();
}

async function fillGift(page: Page, amount: string) {
  const sheet = page.getByRole("dialog");
  await field(sheet, "Amount (LKR)").fill(amount);
  await field(sheet, "Email").fill("e2e.donor@example.com");
  await field(sheet, "Address").fill("12 Temple Road");
  await field(sheet, "City").fill("Kandy");
  await sheet.getByRole("button", { name: "Go to PayHere" }).click();
}

async function placeWithMerchant(page: Page, baseURL: string, name: string): Promise<Institution> {
  const institution = await approvedInstitution(baseURL, `${name} ${Date.now()}`);
  await listed(baseURL, institution);
  await signIn(page, institution.owner);
  await setMerchant(page, institution.owner);
  return institution;
}

test("the owner sets the merchant, a donor gives, and the signed notification makes the receipt @phone", async ({ page, baseURL, browser, request }, testInfo) => {
  test.setTimeout(240_000);
  const institution = await approvedInstitution(baseURL!, `E2E Payments Vihara ${Date.now()}`);
  await listed(baseURL!, institution);
  const donor = await createAccount(baseURL!, "E2E Online Donor");

  try {
    // ---- before any merchant: no offer on the page
    const visitor = await browser.newContext();
    const anonymous = await visitor.newPage();
    await anonymous.goto(`/i/${institution.slug}`);
    await expect(anonymous.getByRole("heading", { name: institution.name, level: 1 })).toBeVisible();
    await expect(anonymous.getByRole("button", { name: "Give online" })).toBeHidden();

    // ---- the form says what is missing; the first save needs a secret
    await signIn(page, institution.owner);
    await page.goto("/giving/payments");
    await expect(page.getByRole("heading", { name: "Online payments", level: 1 })).toBeVisible();
    await page.locator('input[autocomplete="off"]').fill("not a merchant id");
    const refused = lastTextId(institution.owner.phone);
    await page.getByRole("button", { name: "Save", exact: true }).click();
    await stepUp(page, institution.owner, refused);
    await expect(page.getByText("Enter the merchant ID from PayHere: letters and digits only.")).toBeVisible();
    await expect(page.getByText("Enter the merchant secret from PayHere.")).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);

    await setMerchant(page, institution.owner);

    // ---- the secret is write-only: back on the page, the box is empty and says so
    await page.reload();
    await expect(page.getByTestId("merchant-saved")).toContainText("E2E Owner");
    await expect(page.locator('input[type="password"]')).toHaveValue("");
    await expect(page.getByText("Leave this empty to keep it.", { exact: false })).toBeVisible();
    await page.screenshot({ path: shot(`settings-${testInfo.project.name}`), fullPage: true });

    // ---- anonymous: the offer is there, and giving asks for a sign-in that comes back to the sheet
    await anonymous.reload();
    await anonymous.getByRole("button", { name: "Give online" }).click();
    const gate = anonymous.getByRole("dialog");
    await expect(gate.getByText("Sign in to give, so your receipt comes to you.", { exact: false })).toBeVisible();
    await expect(gate.getByRole("link", { name: "Sign in" })).toHaveAttribute("href", new RegExp(`next=${encodeURIComponent(`/i/${institution.slug}?give=1`).replace(/[?]/g, "\\?")}`));
    await visitor.close();

    // ---- the donor: the sheet checks what it can, then leaves for PayHere's page
    const donorContext = await browser.newContext({ viewport: page.viewportSize() ?? undefined });
    const giver = await donorContext.newPage();
    await signIn(giver, donor);
    const payhere = await standInForPayHere(giver);
    await giver.goto(`/i/${institution.slug}?give=1`);
    const sheet = giver.getByRole("dialog");
    await expect(sheet.getByRole("heading", { name: `Give to ${institution.name}` })).toBeVisible();
    await sheet.getByRole("button", { name: "Go to PayHere" }).click();
    await expect(sheet.getByText("Enter an amount from 100 to 1,000,000, with at most two decimals.")).toBeVisible();
    await expect(sheet.getByText("Enter your city.")).toBeVisible();
    await field(sheet, "Amount (LKR)").fill("99");
    await field(sheet, "Email").fill("not an email");
    await field(sheet, "Address").fill("12 Temple Road");
    await field(sheet, "City").fill("Kandy");
    await sheet.getByRole("button", { name: "Go to PayHere" }).click();
    await expect(sheet.getByText("Enter an amount from 100 to 1,000,000, with at most two decimals.")).toBeVisible();
    expect(await scrollsSideways(giver)).toBe(false);
    await giver.screenshot({ path: shot(`sheet-${testInfo.project.name}`), fullPage: true });

    await field(sheet, "Amount (LKR)").fill("2,500");
    await sheet.getByRole("button", { name: "Go to PayHere" }).click();
    await expect(sheet.getByText("Enter a valid email address.")).toBeVisible();

    await fillGift(giver, "2500");
    await expect(giver.getByRole("heading", { name: "Stand-in for PayHere" })).toBeVisible();

    // ---- what reached PayHere's address: the signed form, and a Referer, which PayHere requires
    const fields = payhere.fields!;
    const orderId = fields.get("order_id")!;
    expect(fields.get("merchant_id")).toBe(MERCHANT);
    expect(fields.get("amount")).toBe("2500.00");
    expect(fields.get("currency")).toBe("LKR");
    expect(fields.get("hash")).toBe(md5(MERCHANT + orderId + "2500.00" + "LKR" + md5(SECRET)));
    expect(fields.get("email")).toBe("e2e.donor@example.com");
    expect(fields.get("city")).toBe("Kandy");
    expect(fields.get("first_name")).toBe("E2E");
    expect(fields.get("return_url")).toContain(`/giving/return/${orderId}`);
    expect(payhere.referer).toBeTruthy();
    expect([...fields.keys()].join(" ")).not.toContain("secret");

    // ---- back from PayHere before its notification: the return URL alone proves nothing
    await giver.goto(`/giving/return/${orderId}`);
    await expect(giver.getByRole("heading", { name: "Waiting for PayHere" })).toBeVisible();
    await expect(giver.getByRole("link", { name: /See receipt/ })).toBeHidden();
    expect(await scrollsSideways(giver)).toBe(false);
    await giver.screenshot({ path: shot(`waiting-${testInfo.project.name}`), fullPage: true });

    // ---- the signed notification arrives while the page waits
    await notify(request, baseURL!, orderId, "2500.00", "2");
    await expect(giver.getByRole("heading", { name: "Thank you" })).toBeVisible();
    await expect(giver.getByTestId("payment-return")).toContainText(institution.name);
    await expect(giver.getByTestId("payment-return")).toContainText("LKR 2,500.00");
    await giver.screenshot({ path: shot(`paid-${testInfo.project.name}`), fullPage: true });

    await giver.getByRole("link", { name: `See receipt RCP-${year}-000001` }).click();
    await expect(giver.getByText("Online (PayHere)")).toBeVisible();
    await expect(giver.getByRole("main").getByText("E2E Online Donor").first()).toBeVisible();

    await giver.goto("/giving/mine");
    await expect(giver.getByRole("article", { name: `RCP-${year}-000001` })).toBeVisible();
    await donorContext.close();

    // ---- and the institution sees it in its own book
    await page.goto("/giving/donations");
    await expect(page.getByRole("article", { name: `RCP-${year}-000001` }).getByRole("heading", { name: "LKR 2,500.00" })).toBeVisible();
  } finally {
    await unlist(baseURL!, institution);
  }
});

test("a payment that failed, and one the donor left, record nothing and say so", async ({ page, baseURL, browser, request }) => {
  test.setTimeout(180_000);
  const institution = await placeWithMerchant(page, baseURL!, "E2E Payments Failed");
  const donor = await createAccount(baseURL!, "E2E Online Donor");

  try {
    const context = await browser.newContext();
    const giver = await context.newPage();
    await signIn(giver, donor);
    const payhere = await standInForPayHere(giver);

    // ---- left at PayHere: cancel_url brings the donor back
    await giver.goto(`/i/${institution.slug}`);
    await giver.getByRole("button", { name: "Give online" }).click();
    await fillGift(giver, "500");
    await expect(giver.getByRole("heading", { name: "Stand-in for PayHere" })).toBeVisible();
    const left = payhere.fields!.get("order_id")!;
    await giver.goto(new URL(payhere.fields!.get("cancel_url")!).pathname + "?left=1");
    await expect(giver.getByRole("heading", { name: "The payment was not finished" })).toBeVisible();

    // ---- a signed success still beats the address the browser came back on
    await notify(request, baseURL!, left, "500.00", "2");
    await giver.reload();
    await expect(giver.getByRole("heading", { name: "Thank you" })).toBeVisible();

    // ---- declined by the bank
    await giver.goto(`/i/${institution.slug}`);
    await giver.getByRole("button", { name: "Give online" }).click();
    await fillGift(giver, "750");
    await expect(giver.getByRole("heading", { name: "Stand-in for PayHere" })).toBeVisible();
    const failed = payhere.fields!.get("order_id")!;
    await notify(request, baseURL!, failed, "750.00", "-2");
    await giver.goto(`/giving/return/${failed}`);
    await expect(giver.getByRole("heading", { name: "The payment did not go through" })).toBeVisible();
    await giver.getByRole("link", { name: `Back to ${institution.name}` }).click();
    await expect(giver.getByRole("heading", { name: institution.name, level: 1 })).toBeVisible();

    // ---- one gift in My giving: the one that was paid
    await giver.goto("/giving/mine");
    await expect(giver.getByRole("article", { name: `RCP-${year}-000001` })).toBeVisible();
    await expect(giver.getByRole("article", { name: `RCP-${year}-000002` })).toBeHidden();

    // ---- another person's order reads as not found
    const stranger = await createAccount(baseURL!, "E2E Stranger");
    const otherContext = await browser.newContext();
    const other = await otherContext.newPage();
    await signIn(other, stranger);
    await other.goto(`/giving/return/${left}`);
    await expect(other.getByText("There is no such payment on this account.")).toBeVisible();
    await otherContext.close();
    await context.close();
  } finally {
    await unlist(baseURL!, institution);
  }
});

test("a need's card offers money where the place gives online, and the owner can switch it off", async ({ page, baseURL, browser }) => {
  test.setTimeout(180_000);
  const institution = await placeWithMerchant(page, baseURL!, "E2E Payments Need");
  const title = `Rice ${Date.now()}`;
  const donor = await createAccount(baseURL!, "E2E Online Donor");

  try {
    await page.goto("/giving/needs");
    await page.getByRole("button", { name: "Add a need" }).click();
    const form = page.getByRole("dialog");
    await field(form, "What is needed").fill(title);
    await field(form, "How many").fill("10");
    await field(form, "Category").selectOption({ label: "Stationery" });
    await form.getByRole("button", { name: "Add the need" }).click();
    await expect(form).toBeHidden();
    await page.getByRole("button", { name: `Open ${title} on the board` }).click();
    await expect(page.getByRole("article", { name: title }).getByText("Open", { exact: true })).toBeVisible();

    const context = await browser.newContext();
    const giver = await context.newPage();
    await signIn(giver, donor);
    const payhere = await standInForPayHere(giver);

    // ---- the public board knows nothing of merchants, so it offers only the pledge
    await giver.goto("/needs");
    await expect(giver.getByRole("button", { name: `Pledge to ${title}` })).toBeVisible();
    await expect(giver.getByRole("button", { name: `Give money toward ${title}` })).toBeHidden();

    await giver.goto(`/i/${institution.slug}`);
    await giver.getByRole("button", { name: `Give money toward ${title}` }).click();
    await expect(giver.getByRole("dialog").getByRole("heading", { name: `Give toward ${title}` })).toBeVisible();
    await fillGift(giver, "1000");
    await expect(giver.getByRole("heading", { name: "Stand-in for PayHere" })).toBeVisible();
    expect(payhere.fields!.get("amount")).toBe("1000.00");
    const [forNeed] = sql(
      "SELECT n.Title FROM giving.OnlinePayments p JOIN giving.Needs n ON n.Id = p.NeedId WHERE p.OrderId = '$(order)';",
      { order: payhere.fields!.get("order_id")! },
    );
    expect(forNeed).toBe(title);

    // ---- switched off: the offer goes, and a sheet left open is told why
    await giver.goto(`/i/${institution.slug}`);
    await giver.getByRole("button", { name: "Give online" }).click();
    await page.goto("/giving/payments");
    await page.locator('input[type="checkbox"]').uncheck();
    const before = lastTextId(institution.owner.phone);
    await page.getByRole("button", { name: "Save", exact: true }).click();
    // A page load has no token in memory, so the step-up is asked for again (M28).
    await stepUp(page, institution.owner, before);
    await expect(page.getByText("The online payment settings are saved.").first()).toBeVisible();

    const sheet = giver.getByRole("dialog");
    await field(sheet, "Amount (LKR)").fill("1000");
    await field(sheet, "Email").fill("e2e.donor@example.com");
    await field(sheet, "Address").fill("12 Temple Road");
    await field(sheet, "City").fill("Kandy");
    await sheet.getByRole("button", { name: "Go to PayHere" }).click();
    await expect(sheet.getByText("This place is not taking online gifts right now.")).toBeVisible();

    await giver.reload();
    await expect(giver.getByRole("heading", { name: institution.name, level: 1 })).toBeVisible();
    await expect(giver.getByRole("button", { name: "Give online" })).toBeHidden();
    await context.close();
  } finally {
    await unlist(baseURL!, institution);
  }
});

test("staff without the merchant screen are kept out of it", async ({ page, baseURL }) => {
  const institution = await approvedInstitution(baseURL!, `E2E Payments Gate ${Date.now()}`);
  const donor = await createAccount(baseURL!, "E2E Online Donor");

  try {
    await signIn(page, institution.owner);
    await page.goto("/giving/donations");
    await expect(page.getByRole("link", { name: "Online payments" }).first()).toBeAttached();

    await page.context().clearCookies();
    await signIn(page, donor);
    await page.goto("/giving/payments");
    await expect(page.getByRole("heading", { name: "Online payments", level: 1 })).toBeVisible();
    await expect(page.locator('input[type="password"]')).toBeHidden();
  } finally {
    await unlist(baseURL!, institution);
  }
});

for (const [locale, settings, save, give, leave, leftTitle] of [
  ["si", "මාර්ගගත ගෙවීම්", "සුරකින්න", "මාර්ගගතව පරිත්‍යාග කරන්න", "PayHere වෙත යන්න", "ගෙවීම අවසන් කළේ නැත"],
  ["ta", "இணையவழிக் கட்டணங்கள்", "சேமி", "இணையவழியில் நன்கொடை அளியுங்கள்", "PayHere க்குச் செல்லுங்கள்", "கட்டணம் முடிக்கப்படவில்லை"],
] as const) {
  test(`the settings, the give sheet and the return page in ${locale} fit the phone @phone`, async ({ page, baseURL, browser }, testInfo) => {
    test.setTimeout(180_000);
    const institution = await approvedInstitution(baseURL!, `E2E Payments ${locale} ${Date.now()}`, locale);
    await listed(baseURL!, institution);
    const donor = await createAccount(baseURL!, "E2E Online Donor", locale);

    try {
      await signIn(page, institution.owner, locale);
      // setMerchant opens the page itself: a second load here would cut off the first load's session
      // refresh, and the rotated cookie would never arrive (A19).
      await setMerchant(page, institution.owner, locale, save);
      await expect(page.getByRole("heading", { name: settings, level: 1 })).toBeVisible();
      expect(await scrollsSideways(page)).toBe(false);
      await page.screenshot({ path: shot(`settings-${locale}-${testInfo.project.name}`), fullPage: true });

      const context = await browser.newContext({ viewport: page.viewportSize() ?? undefined, locale: "en-GB" });
      const giver = await context.newPage();
      await signIn(giver, donor, locale);
      const payhere = await standInForPayHere(giver);
      await giver.goto(`/${locale}/i/${institution.slug}`);
      await giver.getByRole("button", { name: give, exact: true }).click();
      const sheet = giver.getByRole("dialog");
      await expect(sheet).toBeVisible();
      expect(await scrollsSideways(giver)).toBe(false);
      await giver.screenshot({ path: shot(`sheet-${locale}-${testInfo.project.name}`), fullPage: true });

      await sheet.locator('input[inputmode="decimal"]').fill("500");
      await sheet.locator('input[type="email"]').fill("e2e.donor@example.com");
      await sheet.locator('input[autocomplete="street-address"]').fill("12 Temple Road");
      await sheet.locator('input[autocomplete="address-level2"]').fill("Kandy");
      await sheet.getByRole("button", { name: leave, exact: true }).click();
      await expect(giver.getByRole("heading", { name: "Stand-in for PayHere" })).toBeVisible();

      // PayHere sends the donor back in the language of their account.
      const back = new URL(payhere.fields!.get("cancel_url")!);
      expect(back.pathname).toBe(`/${locale}/giving/return/${payhere.fields!.get("order_id")}`);
      await giver.goto(back.pathname + back.search);
      await expect(giver.getByRole("heading", { name: leftTitle })).toBeVisible();
      expect(await scrollsSideways(giver)).toBe(false);
      await giver.screenshot({ path: shot(`return-${locale}-${testInfo.project.name}`), fullPage: true });
      await context.close();
    } finally {
      await unlist(baseURL!, institution);
    }
  });
}
