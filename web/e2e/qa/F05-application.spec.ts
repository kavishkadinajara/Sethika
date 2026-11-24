import type { APIRequestContext, Page } from "@playwright/test";
import si from "../../messages/si.json";
import { signIn } from "../support/accounts";
import { field } from "../support/fields";
import { clearRateLimits } from "../support/redis";
import { sql } from "../support/sql";
import { expect, pdf, scrollsSideways, test } from "../support/test";
import { anonymous, bearer, codeOf, mobileLogin, signedIn, throwaway } from "./support/api";
import { answer, account, evidence, persona, tenant } from "./support/world";
import {
  APPS, REVIEW, at, bearer1, close, complete, details, draft, lastTextId, lookups, pdfBytes, registrationNo, reviewAt, statusOf, submit, submitted, textArrives, upload,
} from "./support/onboarding";

/*  F05 - Institution application: apply, documents, declaration, withdraw, clarifications (P1).
    Cases: docs/qa/cases/F05-application.md. A1 (Imran Farook) is the applicant; T6 (his church)
    is already InfoRequested and stays so. Every application a case opens is closed by the case
    (withdrawn, or rejected by P1 once past withdrawing), so A1 stays under the limit of three. */

const A1 = () => account("A1");

async function as<T>(key: "A1" | "P2" | "D1", work: (api: APIRequestContext) => Promise<T>): Promise<T> {
  const api = await signedIn(account(key));
  try {
    return await work(api);
  } finally {
    await api.dispose();
  }
}

async function fillDetails(page: Page, name: string, regNo: string) {
  await field(page, "Type of institution").selectOption("ELDERS_HOME");
  await field(page, "Name in English").fill(name);
  await field(page, "Registration number").fill(regNo);
  await field(page, "Registered with").fill("National Secretariat for Elders");
  await field(page, "Date registered").fill("2014-05-20");
  await field(page, "Year founded").fill("1975");
  await field(page, "Address").fill("7 Lake Road");
  await field(page, "Town or city").fill("Kurunegala");
  await field(page, "District").selectOption("KRN");
  await field(page, "Latitude").fill("7.4863");
  await field(page, "Longitude").fill("80.3647");
  await field(page, "Office phone").fill("0372222333");
  await field(page, "Full name").fill("Imran Farook");
  await field(page, "NIC number").fill("198012345678");
  await field(page, "Your role").fill("Secretary");
  await field(page, "Phone").fill(persona("A1").phone);
}

test("QA-F05-01 A1 applies for a care home on the screen, from a blank form to submitted, and is texted @phone", async ({ page }, testInfo) => {
  const name = `QA Karuna Elders' Home ${testInfo.project.name}`;
  const before = lastTextId(persona("A1").phone);
  await signIn(page, A1());
  await page.goto("/apply");
  await expect(page.getByText("St. Mary's Church Negombo")).toBeVisible();
  await page.getByRole("link", { name: /Start an application|Start another application|New application/ }).first().click();
  await expect(page).toHaveURL(/\/apply\/new$/);
  await fillDetails(page, name, registrationNo("F05-01"));
  expect(await scrollsSideways(page)).toBe(false);
  await page.getByRole("button", { name: "Save and continue" }).click();
  await expect(page).toHaveURL(/\/apply\/REQ-\d{4}-\d+$/);
  const requestNo = /REQ-\d{4}-\d+/.exec(page.url())![0];
  try {
    const bearer = page.getByRole("group", { name: "Office bearer 1" });
    await field(bearer, "Full name").fill(bearer1.fullName);
    await field(bearer, "NIC number").fill(bearer1.nic);
    await field(bearer, "Role").fill(bearer1.designation);
    await field(bearer, "Phone").fill(bearer1.phone);
    await page.getByRole("button", { name: "Save and continue" }).click();

    const forms = page.locator("form").filter({ has: page.getByText("Required", { exact: true }) });
    await expect(forms.first()).toBeVisible();
    const count = await forms.count();
    expect(count, "an elders' home needs four documents").toBe(4);
    for (let i = 0; i < count; i++) {
      const form = forms.nth(i);
      await form.locator('input[type="file"]').setInputFiles(pdf(`doc-${i + 1}.pdf`));
      if (await field(form, "Reference number").count()) await field(form, "Reference number").fill(`QA-REF-${i + 1}`);
      if (await field(form, "Date issued").count()) await field(form, "Date issued").fill("2014-05-20");
      await form.getByRole("button", { name: "Upload" }).click();
      await expect(form.getByText(`Uploaded: doc-${i + 1}.pdf · Waiting for review`)).toBeVisible();
    }
    expect(await scrollsSideways(page)).toBe(false);
    await evidence(page, "QA-F05-01", testInfo, "documents");
    await page.getByRole("button", { name: "Save and continue" }).click();

    await expect(page.getByText("Everything required is in place.")).toBeVisible();
    await page.getByRole("checkbox").check();
    await page.getByRole("button", { name: "Submit the application" }).click();
    await expect(page.getByText("A reviewer will look at your application. We will text you when there is news.")).toBeVisible();
    expect(await scrollsSideways(page)).toBe(false);
    await evidence(page, "QA-F05-01", testInfo, "submitted");

    expect(statusOf(requestNo)).toBe("Submitted");
    const texts = await textArrives(persona("A1").phone, before, `ONBOARD_SUBMITTED:${requestNo}:`);
    expect(texts, "one text, in English, naming the request").toHaveLength(1);
    expect(texts[0]).toContain(requestNo);
    // The declaration accepted is the current one, with where it came from.
    const [declared] = sql("SELECT CONCAT(d.Code, '|', d.IsCurrent, '|', IIF(r.DeclarationIp IS NULL, 'no-ip', 'ip')) FROM org.RegistrationRequests r JOIN core.LegalDocuments d ON d.Id = r.DeclarationDocumentId WHERE r.RequestNo = '$(no)';", { no: requestNo });
    expect(declared).toBe("INSTITUTION_DECLARATION|1|ip");
  } finally {
    await close(A1(), requestNo);
  }
});

test("QA-F05-02 the required documents and office bearers are enforced before submit, per document rule", async () => {
  await as("A1", async (api) => {
    const all = await lookups(api);
    // Every type lists its documents; recorded for the report: all six list the same four DRAFT rows.
    for (const type of all.institutionTypes) expect(all.documents.filter((d) => d.institutionType === type.code && d.isMandatory).length, type.code).toBeGreaterThan(0);

    const requestNo = await draft(api, "ELDERS_HOME", "QA F05-02 rules");
    try {
      // Nothing yet: documents are checked first.
      expect(await codeOf(await submit(api, requestNo))).toBe("documents_missing");
      // A document type the institution type does not take, and one that does not exist.
      for (const code of ["TRUST_DEED", "../APPLICANT_NIC"]) {
        const r = await answer(await api.put(`${at(requestNo)}/documents/${encodeURIComponent(code)}`, upload("APPLICANT_NIC")));
        expect([r.status, r.body?.code], code).toEqual([400, "document_type_not_allowed"]);
      }
      // Per-document fields: the certificate needs a reference number; no issue date in the future; expiry after issue.
      const tomorrow = new Date(Date.now() + 36 * 3600_000).toISOString().slice(0, 10);
      const cases: [Record<string, string>, string][] = [
        [{ referenceNo: "" }, "referenceNo"],
        [{ referenceNo: "QA-රෙජි" }, "referenceNo"],
        [{ issuedDate: "" }, "issuedDate"],
        [{ issuedDate: tomorrow }, "issuedDate"],
        [{ issuedDate: "20/05/2014" }, "issuedDate"],
        [{ expiryDate: "2014-05-20" }, "expiryDate"],
      ];
      for (const [fields, wrong] of cases) {
        const r = await answer(await api.put(`${at(requestNo)}/documents/REGISTRATION_CERTIFICATE`, upload("REGISTRATION_CERTIFICATE", {}, fields)));
        expect([r.status, r.body?.code, r.body?.fields], JSON.stringify(fields)).toEqual([400, "invalid_details", [wrong]]);
      }
      // Three of four documents: still missing.
      await complete(api, requestNo, "ELDERS_HOME", ["PROOF_OF_ADDRESS"]);
      expect(await codeOf(await submit(api, requestNo))).toBe("documents_missing");
      expect((await api.put(`${at(requestNo)}/documents/PROOF_OF_ADDRESS`, upload("PROOF_OF_ADDRESS"))).status()).toBe(204);
      // Office bearers: none, no signatory, the same NIC twice, sixteen.
      expect((await api.put(`${at(requestNo)}/office-bearers`, { data: { officeBearers: [] } })).status()).toBe(204);
      expect(await codeOf(await submit(api, requestNo))).toBe("office_bearer_required");
      expect((await api.put(`${at(requestNo)}/office-bearers`, { data: { officeBearers: [{ ...bearer1, isSignatory: false }] } })).status()).toBe(204);
      expect(await codeOf(await submit(api, requestNo))).toBe("signatory_required");
      const twice = await answer(await api.put(`${at(requestNo)}/office-bearers`, { data: { officeBearers: [bearer1, { ...bearer1, fullName: "QA Other" }] } }));
      expect([twice.status, twice.body?.code]).toEqual([400, "office_bearer_duplicate"]);
      const sixteen = Array.from({ length: 16 }, (_, i) => ({ ...bearer1, fullName: `QA Bearer ${i}`, nic: `19751234${String(5000 + i)}` }));
      expect((await answer(await api.put(`${at(requestNo)}/office-bearers`, { data: { officeBearers: sixteen } }))).status, "16 office bearers").toBe(400);
      expect((await api.put(`${at(requestNo)}/office-bearers`, { data: { officeBearers: sixteen.slice(0, 15) } })).status(), "15 office bearers").toBe(204);
      expect((await submit(api, requestNo)).status(), "all in place").toBe(204);
    } finally {
      await close(A1(), requestNo);
    }
  });
});

test("QA-F05-03 a document's file is checked by type, by content and by size, and the screen refuses first", async ({ page }, testInfo) => {
  const requestNo = await as("A1", (api) => draft(api, "ELDERS_HOME", "QA F05-03 files"));
  try {
    await as("A1", async (api) => {
      const put = (file: { name?: string; mimeType?: string; buffer?: Buffer }) => api.put(`${at(requestNo)}/documents/APPLICANT_NIC`, upload("APPLICANT_NIC", file));
      const mb = 1024 * 1024;
      const sized = (bytes: number) => Buffer.concat([Buffer.from("%PDF-1.4\n"), Buffer.alloc(bytes - 9, 0x20)]);
      const refused: [string, { name?: string; mimeType?: string; buffer?: Buffer }, number, string][] = [
        ["a text file", { name: "nic.txt", mimeType: "text/plain", buffer: Buffer.from("not a card") }, 400, "file_type_not_allowed"],
        ["a program", { name: "nic.exe", mimeType: "application/octet-stream", buffer: Buffer.from("MZ\x90\x00") }, 400, "file_type_not_allowed"],
        ["a program named .pdf", { name: "nic.pdf", buffer: Buffer.from("MZ\x90\x00\x03\x00\x00\x00") }, 400, "file_content_mismatch"],
        ["a PNG named .jpg", { name: "nic.jpg", mimeType: "image/jpeg", buffer: Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]) }, 400, "file_content_mismatch"],
        ["no extension", { name: "nic", buffer: pdfBytes() }, 400, "file_type_not_allowed"],
        ["an empty file", { name: "nic.pdf", buffer: Buffer.alloc(0) }, 400, "file_empty"],
        ["5 MB + 1 byte", { name: "nic.pdf", buffer: sized(5 * mb + 1) }, 400, "file_too_large"],
      ];
      for (const [what, file, status, code] of refused) {
        const r = await answer(await put(file));
        expect([r.status, r.body?.code], what).toEqual([status, code]);
      }
      // Past the request limit (8 MB): refused by size, never a server error.
      // Kestrel answers 413 and closes the connection before reading the body, so a client still
      // sending sees either the 413 or a reset - both a refusal; what must never come is a 5xx.
      const huge = await put({ name: "nic.pdf", buffer: sized(9 * mb) }).then(
        (r) => r.status(),
        (e: Error) => (/ECONNRESET|EPIPE|socket hang up/.test(e.message) ? 413 : Promise.reject(e)),
      );
      expect(huge, "9 MB").toBe(413);
      // Exactly 5 MB is allowed; a real PNG and a JPEG too. Each upload supersedes the one before.
      expect((await put({ name: "nic.pdf", buffer: sized(5 * mb) })).status(), "exactly 5 MB").toBe(204);
      expect((await put({ name: "nic.png", mimeType: "image/png", buffer: Buffer.concat([Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]), Buffer.alloc(32)]) })).status()).toBe(204);
      expect((await put({ name: "my nic (front).JPEG", mimeType: "image/jpeg", buffer: Buffer.concat([Buffer.from([0xff, 0xd8, 0xff, 0xe0]), Buffer.alloc(32)]) })).status()).toBe(204);
      const [live] = sql("SELECT CONCAT(COUNT(*), '|', SUM(IIF(d.VerificationStatus = 'Superseded', 1, 0))) FROM org.RegistrationRequestDocuments d JOIN org.RegistrationRequests r ON r.Id = d.RequestId JOIN org.DocumentTypes t ON t.Id = d.DocumentTypeId WHERE r.RequestNo = '$(no)' AND t.Code = 'APPLICANT_NIC';", { no: requestNo });
      expect(live, "three uploads, two superseded").toBe("3|2");
      // The file comes back as what the server decided it is, as an attachment, never sniffed.
      const file = await api.get(`${at(requestNo)}/documents/APPLICANT_NIC/file`);
      expect(file.status()).toBe(200);
      expect(file.headers()["content-type"]).toBe("image/jpeg");
      expect(file.headers()["content-disposition"]).toMatch(/^attachment/);
      expect(file.headers()["x-content-type-options"]).toBe("nosniff");
    });

    // The screen refuses a wrong type and an oversized file before sending anything.
    await signIn(page, A1());
    await page.goto(`/apply/${requestNo}`);
    await page.getByRole("button", { name: "Save and continue" }).click(); // details -> office bearers
    const bearer = page.getByRole("group", { name: "Office bearer 1" });
    await field(bearer, "Full name").fill(bearer1.fullName);
    await field(bearer, "NIC number").fill(bearer1.nic);
    await field(bearer, "Role").fill(bearer1.designation);
    await field(bearer, "Phone").fill(bearer1.phone);
    await page.getByRole("button", { name: "Save and continue" }).click();
    const form = page.locator("form").filter({ has: page.getByText("Required", { exact: true }) }).nth(1);
    let sent = 0;
    page.on("request", (r) => {
      if (r.method() === "PUT" && r.url().includes("/documents/")) sent++;
    });
    await form.locator('input[type="file"]').setInputFiles({ name: "notes.txt", mimeType: "text/plain", buffer: Buffer.from("x") });
    await expect(form.getByText(/This type of file is not accepted here/)).toBeVisible();
    await form.locator('input[type="file"]').setInputFiles({ name: "big.pdf", mimeType: "application/pdf", buffer: Buffer.concat([Buffer.from("%PDF-"), Buffer.alloc(6 * 1024 * 1024)]) });
    await expect(form.getByText("This file is larger than 5 MB.")).toBeVisible();
    await evidence(page, "QA-F05-03", testInfo);
    expect(sent, "nothing uploaded").toBe(0);
  } finally {
    await close(A1(), requestNo);
  }
});

test("QA-F05-04 a draft keeps what was saved, and each detail is checked at its boundary", async ({ page }) => {
  await as("A1", async (api) => {
    // Refused attempts count towards the ten creates an hour too (OnboardingRateLimits), so the counter is cleared for each.
    const bad = async (overrides: Record<string, unknown>) => (await clearRateLimits(), await answer(await api.post(`${APPS}/`, { data: details("ELDERS_HOME", "QA F05-04 bad", overrides) }))).body?.fields;
    const nextYear = new Date().getFullYear() + 1;
    const tomorrow = new Date(Date.now() + 36 * 3600_000).toISOString().slice(0, 10);
    expect(await bad({ latitude: 5.49 })).toEqual(["latitude"]);
    expect(await bad({ latitude: 10.01 })).toEqual(["latitude"]);
    expect(await bad({ longitude: 79.29 })).toEqual(["longitude"]);
    expect(await bad({ longitude: 82.11 })).toEqual(["longitude"]);
    expect(await bad({ registrationDate: tomorrow })).toEqual(["registrationDate"]);
    expect(await bad({ registrationDate: "1799-12-31" })).toEqual(["registrationDate"]);
    expect(await bad({ establishedYear: nextYear })).toEqual(["establishedYear"]);
    expect(await bad({ applicantNic: "12345" })).toEqual(["applicantNic"]);
    expect(await bad({ institutionType: "CASINO" })).toEqual(["institutionType"]);
    expect(await bad({ district: "XXX" })).toEqual(["district"]);
    expect(await bad({ officeEmail: "not-an-email" })).toEqual(["officeEmail"]);
    expect(await bad({ website: "javascript:alert(1)" })).toEqual(["website"]);
    expect(await bad({ proposedName: "   " })).toEqual(["proposedName"]);

    // The edges themselves are fine, and nothing above made a draft.
    await clearRateLimits();
    const before = sql("SELECT COUNT(*) FROM org.RegistrationRequests;")[0];
    const requestNo = await draft(api, "ELDERS_HOME", "QA F05-04 edges", { latitude: 5.5, longitude: 82.1, establishedYear: new Date().getFullYear(), registrationDate: "1800-01-01", applicantNic: "801234567V" });
    expect(Number(sql("SELECT COUNT(*) FROM org.RegistrationRequests;")[0])).toBe(Number(before) + 1);
    try {
      expect((await api.put(at(requestNo), { data: details("ELDERS_HOME", "QA F05-04 renamed", { city: "Kuliyapitiya" }) })).status()).toBe(204);
      const view = (await (await api.get(at(requestNo))).json()) as { proposedName: string; city: string; status: string };
      expect([view.proposedName, view.city, view.status]).toEqual(["QA F05-04 renamed", "Kuliyapitiya", "Draft"]);

      // Reopened later on the screen: the details are there and it opens at the first step left to do.
      await signIn(page, A1());
      await page.goto("/apply");
      // By its number: earlier runs' withdrawn drafts of the same name stay in the list.
      await expect(page.locator("li").filter({ hasText: requestNo }).getByText("QA F05-04 renamed")).toBeVisible();
      await page.goto(`/apply/${requestNo}`);
      await expect(page.getByText("Step 2 of 4 · Office bearers")).toBeVisible();
      await page.getByRole("button", { name: "Back" }).click();
      await expect(field(page, "Town or city")).toHaveValue("Kuliyapitiya");
    } finally {
      await close(A1(), requestNo);
    }
  });
});

test("QA-F05-05 submitting twice - one after the other, or two at once - submits once", async () => {
  await as("A1", async (api) => {
    for (const mode of ["again", "together"] as const) {
      const requestNo = await draft(api, "ELDERS_HOME", `QA F05-05 ${mode}`);
      try {
        await complete(api, requestNo, "ELDERS_HOME");
        const before = lastTextId(persona("A1").phone);
        const answers = mode === "again" ? [await submit(api, requestNo), await submit(api, requestNo)] : await Promise.all([submit(api, requestNo), submit(api, requestNo)]);
        const statuses = answers.map((a) => a.status()).sort();
        expect(statuses, mode).toEqual([204, 409]);
        const [moves] = sql("SELECT COUNT(*) FROM org.RegistrationRequestStatusHistory h JOIN org.RegistrationRequests r ON r.Id = h.RequestId WHERE r.RequestNo = '$(no)' AND h.ToStatus = 'Submitted';", { no: requestNo });
        expect(moves, "one Submitted in the history").toBe("1");
        await textArrives(persona("A1").phone, before, `ONBOARD_SUBMITTED:${requestNo}:`);
        await new Promise((r) => setTimeout(r, 1500));
        const texts = sql("SELECT COUNT(*) FROM notify.Notifications WHERE Id > $(after) AND DedupKey LIKE 'ONBOARD_SUBMITTED:$(no):%';", { after: String(before), no: requestNo });
        expect(texts[0], "one text").toBe("1");
        expect((await api.put(`${at(requestNo)}/documents/APPLICANT_NIC`, upload("APPLICANT_NIC"))).status(), "no uploads once submitted").toBe(409);
        expect((await api.put(at(requestNo), { data: details("ELDERS_HOME", "QA late change") })).status(), "no edits once submitted").toBe(409);
      } finally {
        await close(A1(), requestNo);
      }
    }
  });
});

test("QA-F05-06 the declaration must be the current one the applicant was shown", async () => {
  await as("A1", async (api) => {
    const { declaration } = await lookups(api);
    const requestNo = await draft(api, "ELDERS_HOME", "QA F05-06 declaration");
    try {
      await complete(api, requestNo, "ELDERS_HOME");
      for (const hash of [undefined, "", "0".repeat(64), declaration!.contentSha256.slice(0, 63) + "0"]) {
        const r = await answer(await api.post(`${at(requestNo)}/submit`, { data: { declarationSha256: hash } }));
        expect([r.status, r.body?.code], String(hash)).toEqual([400, "declaration_required"]);
      }
      expect(statusOf(requestNo)).toBe("Draft");
      // The same hash in capitals is the same text.
      expect((await api.post(`${at(requestNo)}/submit`, { data: { declarationSha256: declaration!.contentSha256.toUpperCase() } })).status()).toBe(204);
    } finally {
      await close(A1(), requestNo);
    }
  });
});

test("QA-F05-07 withdrawing: allowed from Draft, Submitted and InfoRequested, final, refused under review", async ({ page }, testInfo) => {
  const opened: string[] = [];
  try {
    await as("A1", async (api) => {
      const fromDraft = await draft(api, "ELDERS_HOME", "QA F05-07 draft");
      opened.push(fromDraft);
      expect((await api.post(`${at(fromDraft)}/withdraw`)).status()).toBe(204);
      // Final: nothing changes it after.
      expect(await codeOf(await api.post(`${at(fromDraft)}/withdraw`))).toBe("invalid_transition");
      expect(await codeOf(await api.put(at(fromDraft), { data: details("ELDERS_HOME", "QA zombie") }))).toBe("application_not_editable");
      expect(await codeOf(await api.put(`${at(fromDraft)}/documents/APPLICANT_NIC`, upload("APPLICANT_NIC")))).toBe("application_not_editable");
      expect(await codeOf(await submit(api, fromDraft))).toBe("invalid_transition");

      const underReview = await submitted(api, "ELDERS_HOME", "QA F05-07 under review");
      opened.push(underReview);
      await as("P2", async (reviewer) => expect((await reviewer.post(`${reviewAt(underReview)}/start-review`)).status()).toBe(204));
      expect(await codeOf(await api.post(`${at(underReview)}/withdraw`)), "under review").toBe("invalid_transition");

      const asked = await submitted(api, "ELDERS_HOME", "QA F05-07 asked");
      opened.push(asked);
      await as("P2", async (reviewer) => {
        await reviewer.post(`${reviewAt(asked)}/start-review`);
        expect((await reviewer.post(`${reviewAt(asked)}/questions`, { data: { questions: ["QA: is the home still open?"] } })).status()).toBe(204);
      });
      expect((await api.post(`${at(asked)}/withdraw`)).status(), "with a question open").toBe(204);
    });

    // On the screen: a submitted one asks first, then says so.
    const onScreen = await as("A1", (api) => submitted(api, "ELDERS_HOME", "QA F05-07 on screen"));
    opened.push(onScreen);
    await signIn(page, A1());
    await page.goto(`/apply/${onScreen}`);
    await page.getByRole("button", { name: "Withdraw the application" }).click();
    await expect(page.getByText("Withdraw this application? It cannot be undone.")).toBeVisible();
    await page.getByRole("button", { name: "Withdraw", exact: true }).click();
    await expect(page.getByText("You withdrew this application.")).toBeVisible();
    await expect(page.getByRole("button", { name: "Withdraw the application" })).toHaveCount(0);
    await evidence(page, "QA-F05-07", testInfo);
    expect(statusOf(onScreen)).toBe("Withdrawn");
  } finally {
    for (const no of opened) await close(A1(), no);
  }
});

test("QA-F05-08 A1 answers the reviewer's questions: every one, after replacing a document, then it is back under review", async ({ page }, testInfo) => {
  const requestNo = await as("A1", (api) => submitted(api, "ELDERS_HOME", "QA F05-08 questions"));
  try {
    const before = lastTextId(persona("A1").phone);
    await as("P2", async (reviewer) => {
      await reviewer.post(`${reviewAt(requestNo)}/start-review`);
      expect((await reviewer.post(`${reviewAt(requestNo)}/questions`, { data: { questions: ["QA: how many residents live there now?", "QA: please upload a clearer NIC copy."] } })).status()).toBe(204);
    });
    const texts = await textArrives(persona("A1").phone, before, `ONBOARD_INFO_REQUESTED:${requestNo}:`);
    expect(texts).toHaveLength(1);

    await as("A1", async (api) => {
      const one = await answer(await api.post(`${at(requestNo)}/answers`, { data: { answers: ["Forty"] } }));
      expect([one.status, one.body?.code], "one of two").toEqual([400, "questions_unanswered"]);
      const blank = await answer(await api.post(`${at(requestNo)}/answers`, { data: { answers: ["Forty", "   "] } }));
      expect([blank.status, blank.body?.code], "a blank answer").toEqual([400, "questions_unanswered"]);
      const long = await answer(await api.post(`${at(requestNo)}/answers`, { data: { answers: ["x".repeat(2001), "ok"] } }));
      expect(long.status, "2001 characters").toBe(400);
    });

    // On the screen: both questions; one answer alone is refused there too.
    await signIn(page, A1());
    await page.goto(`/apply/${requestNo}`);
    await expect(page.getByText("QA: how many residents live there now?")).toBeVisible();
    const boxes = page.getByLabel("Your answer");
    await expect(boxes).toHaveCount(2);
    await boxes.nth(0).fill("Forty residents.");
    await page.getByRole("button", { name: "Send your answers" }).click();
    await expect(page.getByText("Answer every question.").first()).toBeVisible();
    // While questions are open, a document can be replaced.
    const nic = page.locator("form").filter({ has: page.getByText("Required", { exact: true }) }).filter({ hasText: /NIC/i }).first();
    await nic.locator('input[type="file"]').setInputFiles(pdf("clearer-nic.pdf"));
    await nic.getByRole("button", { name: "Upload a new file" }).click();
    await expect(nic.getByText("Uploaded: clearer-nic.pdf · Waiting for review")).toBeVisible();
    await boxes.nth(1).fill("A clearer copy is uploaded.");
    await evidence(page, "QA-F05-08", testInfo);
    await page.getByRole("button", { name: "Send your answers" }).click();
    await expect(page.getByText("A reviewer is looking at your application.")).toBeVisible();
    await expect(page.getByText("You answered: Forty residents.")).toBeVisible();
    expect(statusOf(requestNo)).toBe("UnderReview");

    await as("A1", async (api) => {
      expect(await codeOf(await api.post(`${at(requestNo)}/answers`, { data: { answers: ["again", "again"] } })), "answered already").toBe("invalid_transition");
      expect(await codeOf(await api.put(`${at(requestNo)}/documents/APPLICANT_NIC`, upload("APPLICANT_NIC"))), "no uploads under review").toBe("application_not_editable");
    });

    // T6's question (from the setup) is still waiting for A1, and is shown.
    await page.goto(`/apply/${tenant("T6").requestNo}`);
    await expect(page.getByText("QA: please upload a clearer copy of the trust deed's first page.")).toBeVisible();
  } finally {
    await close(A1(), requestNo);
  }
});

test("QA-F05-09 an application is only ever its applicant's: other people, other sessions and no session get nothing", async ({ page }) => {
  const t6 = tenant("T6").requestNo;
  const missing = "REQ-2026-999999";
  await as("D1", async (api) => {
    for (const no of [t6, missing]) {
      const read = await answer(await api.get(at(no)));
      expect([read.status, read.body?.code], `read ${no}`).toEqual([404, "registration_not_found"]);
      expect((await api.get(`${at(no)}/documents/APPLICANT_NIC/file`)).status(), `file ${no}`).toBe(404);
      expect((await api.put(at(no), { data: details("CHURCH", "QA hijack") })).status(), `edit ${no}`).toBe(404);
      expect((await api.post(`${at(no)}/withdraw`)).status(), `withdraw ${no}`).toBe(404);
      expect((await api.post(`${at(no)}/answers`, { data: { answers: ["x"] } })).status(), `answer ${no}`).toBe(404);
      expect((await api.put(`${at(no)}/documents/APPLICANT_NIC`, upload("APPLICANT_NIC"))).status(), `upload ${no}`).toBe(404);
    }
    const mine = JSON.stringify(await (await api.get(`${APPS}/`)).json());
    expect(mine).not.toContain(t6);
  });
  expect(statusOf(t6)).toBe("InfoRequested");

  // An institution session (O1 in T1) is not a personal one: the applicant's routes refuse it (B1).
  const o1 = (await mobileLogin(account("O1"))).session!;
  expect(o1.activeInstitution).toBe(tenant("T1").slug);
  const inT1 = await bearer(o1.accessToken);
  try {
    expect((await inT1.get(`${APPS}/`)).status()).toBe(403);
    expect((await inT1.post(`${APPS}/`, { data: details("TEMPLE", "QA from inside T1") })).status()).toBe(403);
  } finally {
    await inT1.dispose();
  }
  const nobody = await anonymous();
  try {
    expect((await nobody.get(at(t6))).status()).toBe(401);
    expect((await nobody.get(`${REVIEW}/`)).status()).toBe(401);
  } finally {
    await nobody.dispose();
  }

  await signIn(page, account("D1"));
  await page.goto(`/apply/${t6}`);
  await expect(page.getByText("St. Mary's Church Negombo")).toHaveCount(0);
  await expect(page.getByText(si.Apply.errors.registration_not_found).first()).toBeVisible();
});

test("QA-F05-10 three open applications at most, counted safely when four are started at once", async () => {
  const person = await throwaway("apply-limit");
  const api = await signedIn(person);
  try {
    const three = [];
    for (let i = 1; i <= 3; i++) three.push(await draft(api, "TEMPLE", `QA F05-10 ${i}`));
    const fourth = await answer(await api.post(`${APPS}/`, { data: details("TEMPLE", "QA F05-10 4") }));
    expect([fourth.status, fourth.body?.code]).toEqual([409, "too_many_applications"]);
    expect((await api.post(`${at(three[0])}/withdraw`)).status()).toBe(204);
    expect((await api.post(`${APPS}/`, { data: details("TEMPLE", "QA F05-10 4") })).status(), "a withdrawn one frees its place").toBe(201);
  } finally {
    await api.dispose();
  }

  const racer = await throwaway("apply-race");
  const race = await signedIn(racer);
  try {
    const answers = await Promise.all([1, 2, 3, 4, 5].map((i) => race.post(`${APPS}/`, { data: details("TEMPLE", `QA F05-10 race ${i}`) })));
    expect(answers.map((a) => a.status()).sort()).toEqual([201, 201, 201, 409, 409]);
    const [open] = sql("SELECT COUNT(*) FROM org.RegistrationRequests r JOIN auth.Users u ON u.Id = r.ApplicantUserId WHERE RIGHT(u.PhoneNumber, 9) = '$(last9)';", { last9: racer.phone.slice(1) });
    expect(open).toBe("3");
  } finally {
    await race.dispose();
  }
});

test("QA-F05-11 a registration number already held by a live application or institution of the same type is refused, also when changed later", async () => {
  // T1 (approved) holds TEMPLE + QA/T1/2011/101.
  const [t1No] = sql("SELECT OfficialRegistrationNo FROM org.RegistrationRequests WHERE RequestNo = '$(no)';", { no: tenant("T1").requestNo });
  const person = await throwaway("regno");
  const api = await signedIn(person);
  const opened: string[] = [];
  try {
    // A draft may carry it; submitting is refused.
    const copy = await draft(api, "TEMPLE", "QA F05-11 copy", { officialRegistrationNo: t1No });
    opened.push(copy);
    await complete(api, copy, "TEMPLE");
    const refused = await answer(await submit(api, copy));
    expect([refused.status, refused.body?.code]).toEqual([409, "registration_no_in_use"]);
    // The same number for another type of institution is another registry's number.
    const otherType = await submitted(api, "CHURCH", "QA F05-11 other type", { officialRegistrationNo: t1No });
    opened.push(otherType);
    expect(statusOf(otherType)).toBe("Submitted");

    // Changed while answering a question (InfoRequested is a live status): the same refusal, not a crash.
    expect((await api.post(`${at(copy)}/withdraw`)).status()).toBe(204);
    const asked = await submitted(api, "TEMPLE", "QA F05-11 asked");
    opened.push(asked);
    await as("P2", async (reviewer) => {
      await reviewer.post(`${reviewAt(asked)}/start-review`);
      await reviewer.post(`${reviewAt(asked)}/questions`, { data: { questions: ["QA: please check the registration number."] } });
    });
    const changed = await answer(await api.put(at(asked), { data: details("TEMPLE", "QA F05-11 asked", { officialRegistrationNo: t1No }) }));
    expect([changed.status, changed.body?.code], "changing to T1's number while InfoRequested").toEqual([409, "registration_no_in_use"]);
  } finally {
    await api.dispose();
    for (const no of opened) await close(person, no);
  }
});
