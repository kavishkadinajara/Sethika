import type { APIRequestContext } from "@playwright/test";
import en from "../../messages/en.json";
import si from "../../messages/si.json";
import { grantPlatformRole, signIn, stepUp, type Account } from "../support/accounts";
import { field } from "../support/fields";
import { sql } from "../support/sql";
import { expect, scrollsSideways, test } from "../support/test";
import { anonymous, bearer, codeOf, mobileLogin, signedIn, stepUpApi, throwaway } from "./support/api";
import { answer, account, evidence, tenant, type PersonaKey } from "./support/world";
import {
  APPS, REVIEW, at, close, draft, lastTextId, reviewAt, reviewView, reviewed, statusOf, submitted, textArrives, upload,
} from "./support/onboarding";

/*  F06 - Registration review and approval (P1). Cases: docs/qa/cases/F06-review.md. P2 (Tharindu
    Bandara, PLATFORM_REVIEWER) reviews; P1 (Anjali Herath, PLATFORM_ADMIN) decides with a step-up.
    The applicants are throwaways ("QA tmp applicant"), so A1 and T1-T6 are left as they are; an
    approval makes a new institution named "QA F06 ..." - two per run, by design of the cases.  */

async function with_<T>(key: PersonaKey | Account, work: (api: APIRequestContext) => Promise<T>): Promise<T> {
  const api = await signedIn(typeof key === "string" ? account(key) : key);
  try {
    return await work(api);
  } finally {
    await api.dispose();
  }
}

/** A throwaway applicant and their submitted elders' home. */
async function applicantWith(name: string): Promise<{ applicant: Account; requestNo: string }> {
  const applicant = await throwaway("applicant");
  const requestNo = await with_(applicant, (api) => submitted(api, "ELDERS_HOME", name));
  return { applicant, requestNo };
}

/** P1's API session, stepped up. */
async function admin(): Promise<APIRequestContext> {
  const api = await signedIn(account("P1"));
  await stepUpApi(api, account("P1"));
  return api;
}

const one = (query: string, vars: Record<string, string>) => sql(query, vars)[0];

test("QA-F06-01 P2's queue shows what waits for review, never drafts, and its filters hold @phone", async ({ page }, testInfo) => {
  const { applicant, requestNo } = await applicantWith(`QA F06-01 queue ${testInfo.project.name}`);
  const draftNo = await with_(applicant, (api) => draft(api, "ELDERS_HOME", "QA F06-01 draft"));
  try {
    await with_("P2", async (api) => {
      const submittedList = JSON.stringify(await (await api.get(`${REVIEW}/?status=Submitted&pageSize=100`)).json());
      expect(submittedList).toContain(requestNo);
      const everything = JSON.stringify(await (await api.get(`${REVIEW}/?pageSize=100`)).json());
      expect(everything, "drafts are not offered for review").not.toContain(draftNo);
      expect((await api.get(reviewAt(draftNo))).status(), "nor opened").toBe(404);
      for (const [query, status] of [["status=Draft", 400], ["status=Nonsense", 400], ["page=0", 400], ["pageSize=0", 400], ["pageSize=101", 400], ["pageSize=100", 200], ["status=Rejected", 200]] as const) {
        expect((await api.get(`${REVIEW}/?${query}`)).status(), query).toBe(status);
      }
      const rejected = JSON.stringify(await (await api.get(`${REVIEW}/?status=Rejected&pageSize=100`)).json());
      expect(rejected).toContain(tenant("T5").requestNo);
      expect(rejected).not.toContain(requestNo);
    });

    await signIn(page, account("P2"));
    await page.goto("/admin/registrations");
    await expect(page.getByRole("heading", { name: "Institution applications" })).toBeVisible();
    const row = page.locator("li").filter({ hasText: requestNo });
    await expect(row.getByText("Submitted", { exact: true })).toBeVisible();
    await expect(page.locator("li").filter({ hasText: draftNo })).toHaveCount(0);
    expect(await scrollsSideways(page)).toBe(false);
    await evidence(page, "QA-F06-01", testInfo);
    await field(page, "Show").selectOption({ label: "Approved" });
    await expect(page).toHaveURL(/status=Approved/);
    await expect(page.locator("li").filter({ hasText: requestNo })).toHaveCount(0);
    await expect(page.locator("li").filter({ hasText: tenant("T1").requestNo })).toBeVisible();
  } finally {
    await close(applicant, draftNo);
    await close(applicant, requestNo);
  }
});

test("QA-F06-02 P2 reviews on the screen - documents, checklist - and can neither approve nor reject", async ({ page }, testInfo) => {
  const { applicant, requestNo } = await applicantWith("QA F06-02 reviewed");
  try {
    await signIn(page, account("P2"));
    await page.goto(`/admin/registrations/${requestNo}`);
    await expect(page.getByRole("button", { name: "Accept", exact: true })).toHaveCount(0);
    await page.getByRole("button", { name: "Start the review" }).click();
    await expect(page.getByText("Under review", { exact: true }).first()).toBeVisible();
    const documents = page.locator("li").filter({ has: page.getByRole("button", { name: "Accept", exact: true }) });
    const count = await documents.count();
    expect(count).toBe(4);
    for (let i = 0; i < count; i++) {
      const document = documents.nth(i);
      await document.getByRole("button", { name: "Accept", exact: true }).click();
      await expect(document.getByText("Accepted", { exact: true })).toBeVisible();
    }
    for (const yes of await page.getByRole("radio", { name: "Yes" }).all()) await yes.check();
    await page.getByRole("button", { name: "Save the checklist" }).click();
    await expect(page.getByText("Checklist saved.").first()).toBeVisible();
    await expect(page.getByText("A platform administrator makes the decision.")).toBeVisible();
    await expect(page.getByRole("button", { name: "Approve", exact: true })).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Do not approve" })).toHaveCount(0);
    await evidence(page, "QA-F06-02", testInfo);

    // The API agrees, even after P2 proves who they are: the permission is not theirs.
    await with_("P2", async (api) => {
      await stepUpApi(api, account("P2"));
      expect((await api.post(`${reviewAt(requestNo)}/approve`)).status()).toBe(403);
      expect((await api.post(`${reviewAt(requestNo)}/reject`, { data: { reason: "OTHER", note: "QA" } })).status()).toBe(403);
    });
    expect(statusOf(requestNo)).toBe("UnderReview");
    const view = await with_("P2", (api) => reviewView(api, requestNo));
    expect(view.application.documents.every((d) => d.verificationStatus === "Verified")).toBe(true);
  } finally {
    await close(applicant, requestNo);
  }
});

test("QA-F06-03 approval waits for every check, then P1 approves on the screen and ONE transaction sets the institution up", async ({ page }, testInfo) => {
  const name = `QA F06 Approved Home ${Date.now() % 100000}`;
  const { applicant, requestNo } = await applicantWith(name);
  const decider = await admin();
  try {
    const approve = async () => answer(await decider.post(`${reviewAt(requestNo)}/approve`));
    expect((await approve()).body?.code, "not started").toBe("invalid_transition");

    await with_("P2", async (reviewer) => {
      expect((await reviewer.post(`${reviewAt(requestNo)}/start-review`)).status()).toBe(204);
      expect(await codeOf(await reviewer.post(`${reviewAt(requestNo)}/start-review`)), "started twice").toBe("invalid_transition");
      expect((await approve()).body?.code, "nothing verified").toBe("documents_not_verified");
      const view = await reviewView(reviewer, requestNo);
      const types = view.application.documents.map((d) => d.documentType);
      for (const type of types.slice(0, 3)) expect((await reviewer.put(`${reviewAt(requestNo)}/documents/${type}/verification`, { data: { verified: true, remarks: null } })).status()).toBe(204);
      expect((await approve()).body?.code, "three of four").toBe("documents_not_verified");
      const noRemarks = await answer(await reviewer.put(`${reviewAt(requestNo)}/documents/${types[3]}/verification`, { data: { verified: false, remarks: " " } }));
      expect([noRemarks.status, noRemarks.body?.code]).toEqual([400, "remarks_required"]);
      expect((await reviewer.put(`${reviewAt(requestNo)}/documents/${types[3]}/verification`, { data: { verified: false, remarks: "QA: unreadable" } })).status()).toBe(204);
      expect((await approve()).body?.code, "one not accepted").toBe("documents_not_verified");
      expect((await reviewer.put(`${reviewAt(requestNo)}/documents/NO_SUCH/verification`, { data: { verified: true } })).status()).toBe(404);
      expect((await reviewer.put(`${reviewAt(requestNo)}/documents/${types[3]}/verification`, { data: { verified: true, remarks: null } })).status()).toBe(204);
      expect((await approve()).body?.code, "checklist empty").toBe("checklist_incomplete");
      const items = view.checklist.map((c) => c.code);
      const unknown = await answer(await reviewer.put(`${reviewAt(requestNo)}/checklist`, { data: { items: [{ code: "NO_SUCH_ITEM", passed: true }] } }));
      expect([unknown.status, unknown.body?.code]).toEqual([400, "checklist_item_unknown"]);
      expect((await reviewer.put(`${reviewAt(requestNo)}/checklist`, { data: { items: [...items.slice(0, 3).map((code) => ({ code, passed: true })), { code: items[3], passed: false, remarks: "QA: no answer on the phone" }] } })).status()).toBe(204);
      expect((await approve()).body?.code, "one item failed").toBe("checklist_incomplete");
      expect((await reviewer.put(`${reviewAt(requestNo)}/checklist`, { data: { items: [{ code: items[3], passed: true, remarks: null }] } })).status()).toBe(204);
    });
    expect(statusOf(requestNo), "no refusal changed it").toBe("UnderReview");

    // On the screen, with the step-up dialog.
    const before = lastTextId(account("P1").phone);
    const applicantTexts = lastTextId(applicant.phone);
    await signIn(page, account("P1"));
    await page.goto(`/admin/registrations/${requestNo}`);
    await expect(page.getByText("Before it can be approved")).toBeHidden();
    await page.getByRole("button", { name: "Approve", exact: true }).click();
    await page.getByRole("button", { name: "Yes, approve" }).click();
    await stepUp(page, account("P1"), before);
    await expect(page.getByText(/^Approved\. The institution is on Sethika as /)).toBeVisible();
    await evidence(page, "QA-F06-03", testInfo);

    // What the one transaction made, against T3 (an elders' home approved without a plan) as the oracle.
    const [slug, code, status] = one("SELECT CONCAT(i.Slug, '|', i.InstitutionCode, '|', i.Status) FROM org.Institutions i JOIN org.RegistrationRequests r ON r.CreatedInstitutionId = i.Id WHERE r.RequestNo = '$(no)';", { no: requestNo }).split("|");
    expect(status).toBe("Active");
    expect(slug).not.toMatch(/^\d+$/);
    const vars = { slug, t3: tenant("T3").slug!, no: requestNo };
    const roles = (s: string) => one("SELECT STRING_AGG(r.Code, ',') WITHIN GROUP (ORDER BY r.Code) FROM auth.Roles r JOIN org.Institutions i ON i.Id = r.InstitutionId WHERE i.Slug = '$(s)';", { s });
    expect(roles(slug), "the template roles, as T3 got them").toBe(roles(vars.t3));
    const grants = (s: string) => one("SELECT COUNT(*) FROM auth.RolePermissions rp JOIN auth.Roles r ON r.Id = rp.RoleId JOIN org.Institutions i ON i.Id = r.InstitutionId WHERE i.Slug = '$(s)';", { s });
    expect(grants(slug), "with every grant").toBe(grants(vars.t3));
    const modules = (s: string) => one("SELECT STRING_AGG(CAST(m.ModuleId AS varchar(10)), ',') WITHIN GROUP (ORDER BY m.ModuleId) FROM org.InstitutionModules m JOIN org.Institutions i ON i.Id = m.InstitutionId WHERE i.Slug = '$(s)' AND m.IsEnabled = 1;", { s });
    expect(modules(slug), "the type's modules, as T3 got them").toBe(modules(vars.t3));
    expect(one("SELECT COUNT(*) FROM org.InstitutionDocuments d JOIN org.Institutions i ON i.Id = d.InstitutionId WHERE i.Slug = '$(slug)';", vars), "the four verified documents").toBe("4");
    expect(one("SELECT CONCAT(COUNT(*), '|', MAX(CAST(m.IsOwner AS int))) FROM org.InstitutionMembers m JOIN org.Institutions i ON i.Id = m.InstitutionId JOIN auth.Users u ON u.Id = m.UserId WHERE i.Slug = '$(slug)' AND RIGHT(u.PhoneNumber, 9) = '$(last9)';", { ...vars, last9: applicant.phone.slice(1) })).toBe("1|1");
    expect(one("SELECT r.Code FROM auth.UserRoles ur JOIN auth.Roles r ON r.Id = ur.RoleId JOIN org.Institutions i ON i.Id = ur.InstitutionId JOIN auth.Users u ON u.Id = ur.UserId WHERE i.Slug = '$(slug)' AND ur.IsActive = 1 AND RIGHT(u.PhoneNumber, 9) = '$(last9)';", { ...vars, last9: applicant.phone.slice(1) })).toBe("OWNER");
    expect(one("SELECT COUNT(*) FROM org.InstitutionStatusHistory h JOIN org.Institutions i ON i.Id = h.InstitutionId WHERE i.Slug = '$(slug)' AND h.ToStatus = 'Active';", vars)).toBe("1");
    expect(one("SELECT COUNT(*) FROM audit.AuditLogs WHERE Action = 'Approve' AND EntityName = 'RegistrationRequest' AND EntityId = '$(no)';", vars)).toBe("1");
    expect(one("SELECT COUNT(*) FROM core.OutboxMessages WHERE EventType = 'InstitutionApproved' AND AggregateId = '$(no)';", vars)).toBe("1");
    const texts = await textArrives(applicant.phone, applicantTexts, `ONBOARD_APPROVED:${requestNo}:`);
    expect(texts).toHaveLength(1);
    expect(texts[0]).toContain(requestNo);
    // The applicant now signs in into the institution, as its owner.
    const session = (await mobileLogin(applicant)).session!;
    expect(session.activeInstitution).toBe(slug);
    expect(code).toMatch(/\w/);
  } finally {
    await decider.dispose();
  }
});

test("QA-F06-04 approving twice, one after the other or both at once, makes one institution", async () => {
  const n = String(Date.now() % 100000);
  const name = `QA F06 Twice Home ${n}`;
  const { applicant, requestNo } = await applicantWith(name);
  await with_("P2", (api) => reviewed(api, requestNo));
  const decider = await admin();
  try {
    const both = await Promise.all([decider.post(`${reviewAt(requestNo)}/approve`), decider.post(`${reviewAt(requestNo)}/approve`)]);
    const answers = await Promise.all(both.map(async (r) => [r.status(), await codeOf(r)] as const));
    expect(answers.map((a) => a[0]).sort()).toEqual([200, 409]);
    expect(answers.find((a) => a[0] === 409)![1]).toBe("invalid_transition");
    expect(await codeOf(await decider.post(`${reviewAt(requestNo)}/approve`)), "a third time").toBe("invalid_transition");
    expect(await codeOf(await decider.post(`${reviewAt(requestNo)}/reject`, { data: { reason: "OTHER", note: "QA" } })), "rejected after").toBe("invalid_transition");
    expect(one("SELECT COUNT(*) FROM org.Institutions WHERE Name = N'QA F06 Twice Home $(n)';", { n })).toBe("1");
    expect(one("SELECT COUNT(*) FROM core.OutboxMessages WHERE EventType = 'InstitutionApproved' AND AggregateId = '$(no)';", { no: requestNo })).toBe("1");
    // Signed in now, the applicant is in their institution; from their own account, withdrawing is a wrong move either way.
    expect(await with_(applicant, async (api) => (await api.post(`${at(requestNo)}/withdraw`)).status()), "withdrawn after").not.toBe(204);
    expect(statusOf(requestNo)).toBe("Approved");
  } finally {
    await decider.dispose();
  }
});

test("QA-F06-05 the clarification loop from the reviewer's side: ask, answers come back, a replaced document needs checking again", async ({ page }, testInfo) => {
  const { applicant, requestNo } = await applicantWith("QA F06-05 questions");
  try {
    await with_("P2", async (reviewer) => {
      expect(await codeOf(await reviewer.post(`${reviewAt(requestNo)}/questions`, { data: { questions: ["QA: before review?"] } })), "not started").toBe("invalid_transition");
      await reviewed(reviewer, requestNo);
      for (const questions of [[], ["   "], null]) {
        const r = await answer(await reviewer.post(`${reviewAt(requestNo)}/questions`, { data: { questions } }));
        expect([r.status, r.body?.code], JSON.stringify(questions)).toEqual([400, "question_required"]);
      }
      const before = lastTextId(applicant.phone);
      expect((await reviewer.post(`${reviewAt(requestNo)}/questions`, { data: { questions: ["QA: is the NIC copy the front side?"] } })).status()).toBe(204);
      expect(await textArrives(applicant.phone, before, `ONBOARD_INFO_REQUESTED:${requestNo}:`)).toHaveLength(1);
    });
    const decider = await admin();
    try {
      expect(await codeOf(await decider.post(`${reviewAt(requestNo)}/approve`)), "while waiting for answers").toBe("invalid_transition");
    } finally {
      await decider.dispose();
    }

    // The applicant replaces the NIC copy and answers.
    await with_(applicant, async (api) => {
      expect((await api.put(`${at(requestNo)}/documents/APPLICANT_NIC`, upload("APPLICANT_NIC", { name: "nic-front.pdf" }))).status()).toBe(204);
      expect((await api.post(`${at(requestNo)}/answers`, { data: { answers: ["Yes, the front. A new copy is uploaded."] } })).status()).toBe(204);
    });
    await with_("P2", async (reviewer) => {
      const view = await reviewView(reviewer, requestNo);
      expect(view.application.status).toBe("UnderReview");
      expect(view.application.questions[0].response).toBe("Yes, the front. A new copy is uploaded.");
      expect(view.application.documents.find((d) => d.documentType === "APPLICANT_NIC")!.verificationStatus, "the new copy is checked again").toBe("Pending");
      // A second round is its own text.
      const before = lastTextId(applicant.phone);
      expect((await reviewer.post(`${reviewAt(requestNo)}/questions`, { data: { questions: ["QA: and the back?"] } })).status()).toBe(204);
      expect(await textArrives(applicant.phone, before, `ONBOARD_INFO_REQUESTED:${requestNo}:`)).toHaveLength(1);
    });
    const decider2 = await admin();
    try {
      await with_(applicant, async (api) => expect((await api.post(`${at(requestNo)}/answers`, { data: { answers: ["Uploaded as well."] } })).status()).toBe(204));
      expect(await codeOf(await decider2.post(`${reviewAt(requestNo)}/approve`)), "the replaced copy is not verified").toBe("documents_not_verified");
    } finally {
      await decider2.dispose();
    }

    await signIn(page, account("P2"));
    await page.goto(`/admin/registrations/${requestNo}`);
    await expect(page.getByText("Yes, the front. A new copy is uploaded.")).toBeVisible();
    await expect(page.getByText("Uploaded as well.")).toBeVisible();
    await evidence(page, "QA-F06-05", testInfo);
    expect(one("SELECT COUNT(*) FROM org.RegistrationRequestStatusHistory h JOIN org.RegistrationRequests r ON r.Id = h.RequestId WHERE r.RequestNo = '$(no)' AND h.ToStatus = 'InfoRequested';", { no: requestNo })).toBe("2");
  } finally {
    await close(applicant, requestNo);
  }
});

test("QA-F06-06 not approving needs a known reason, is final, and the applicant reads why", async ({ page }) => {
  const { applicant, requestNo } = await applicantWith("QA F06-06 rejected");
  await with_("P2", async (api) => expect((await api.post(`${reviewAt(requestNo)}/start-review`)).status()).toBe(204));
  const decider = await admin();
  try {
    const unknown = await answer(await decider.post(`${reviewAt(requestNo)}/reject`, { data: { reason: "NO_SUCH_REASON", note: "x" } }));
    expect([unknown.status, unknown.body?.code]).toEqual([400, "reject_reason_unknown"]);
    for (const body of [{ reason: null, note: "x" }, {}]) {
      const r = await answer(await decider.post(`${reviewAt(requestNo)}/reject`, { data: body }));
      expect([r.status, r.body?.code], JSON.stringify(body)).toEqual([400, "invalid_details"]);
    }
    const before = lastTextId(applicant.phone);
    expect((await decider.post(`${reviewAt(requestNo)}/reject`, { data: { reason: "NOT_ELIGIBLE", note: "QA: only registered homes can join." } })).status()).toBe(204);
    expect(await codeOf(await decider.post(`${reviewAt(requestNo)}/reject`, { data: { reason: "OTHER", note: "again" } }))).toBe("invalid_transition");
    expect(await codeOf(await decider.post(`${reviewAt(requestNo)}/approve`))).toBe("invalid_transition");
    expect(await textArrives(applicant.phone, before, `ONBOARD_REJECTED:${requestNo}:`)).toHaveLength(1);
  } finally {
    await decider.dispose();
  }
  await with_(applicant, async (api) => {
    const view = (await (await api.get(at(requestNo))).json()) as { status: string; rejectReason: string; decisionNote: string };
    expect([view.status, view.rejectReason, view.decisionNote]).toEqual(["Rejected", "NOT_ELIGIBLE", "QA: only registered homes can join."]);
    expect(await codeOf(await api.post(`${at(requestNo)}/withdraw`))).toBe("invalid_transition");
  });
  expect(one("SELECT COUNT(*) FROM org.Institutions i JOIN org.RegistrationRequests r ON r.CreatedInstitutionId = i.Id WHERE r.RequestNo = '$(no)';", { no: requestNo })).toBe("0");

  // A1 reads T5's refusal on the screen.
  await signIn(page, account("A1"));
  await page.goto(`/apply/${tenant("T5").requestNo}`);
  await expect(page.getByText("Your application was not approved.")).toBeVisible();
  await expect(page.getByText(`Reason: ${en.Apply.rejectReasons.NOT_VERIFIABLE}`)).toBeVisible();
  await expect(page.getByText("Reviewer's note: QA: the registration number could not be found with the department.")).toBeVisible();
});

test("QA-F06-07 nobody reviews or decides their own application", async () => {
  // Someone applies, then joins the platform staff (in SQL, as the e2e helpers do: a reviewer who
  // was once an applicant is the case the rule is for).
  const { applicant, requestNo } = await applicantWith("QA F06-07 own");
  grantPlatformRole(applicant, "PLATFORM_ADMIN");
  try {
    await with_(applicant, async (api) => {
      const started = await answer(await api.post(`${reviewAt(requestNo)}/start-review`));
      expect([started.status, started.body?.code]).toEqual([403, "own_application"]);
    });
    await with_("P2", (api) => reviewed(api, requestNo));
    await with_(applicant, async (api) => {
      await stepUpApi(api, applicant);
      const approved = await answer(await api.post(`${reviewAt(requestNo)}/approve`));
      expect([approved.status, approved.body?.code]).toEqual([403, "own_application"]);
      const rejected = await answer(await api.post(`${reviewAt(requestNo)}/reject`, { data: { reason: "OTHER", note: "mine" } }));
      expect([rejected.status, rejected.body?.code]).toEqual([403, "own_application"]);
    });
    expect(statusOf(requestNo)).toBe("UnderReview");
  } finally {
    await close(applicant, requestNo);
  }
});

test("QA-F06-08 everyone but the platform is refused the review API, whatever they hold", async ({ page }) => {
  const requestNo = tenant("T6").requestNo;
  const calls: [string, (api: APIRequestContext) => Promise<number>][] = [
    ["list", async (api) => (await api.get(`${REVIEW}/`)).status()],
    ["detail", async (api) => (await api.get(reviewAt(requestNo))).status()],
    ["document", async (api) => (await api.get(`${reviewAt(requestNo)}/documents/APPLICANT_NIC/file`)).status()],
    ["start-review", async (api) => (await api.post(`${reviewAt(requestNo)}/start-review`)).status()],
    ["question", async (api) => (await api.post(`${reviewAt(requestNo)}/questions`, { data: { questions: ["x"] } })).status()],
    ["approve", async (api) => (await api.post(`${reviewAt(requestNo)}/approve`)).status()],
    ["reject", async (api) => (await api.post(`${reviewAt(requestNo)}/reject`, { data: { reason: "OTHER", note: "x" } })).status()],
  ];
  for (const key of ["D1", "A1", "O1", "M1", "S1", "C1", "L1", "O2", "O3"] as const) {
    await with_(key, async (api) => {
      for (const [what, call] of calls) expect(await call(api), `${key} ${what}`).toBe(403);
    });
  }
  const nobody = await anonymous();
  try {
    for (const [what, call] of calls) expect(await call(nobody), `anonymous ${what}`).toBe(401);
  } finally {
    await nobody.dispose();
  }
  // A forged token is no one.
  const forged = await bearer("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ4In0.c2lnbmF0dXJl");
  try {
    expect(await calls[0][1](forged)).toBe(401);
  } finally {
    await forged.dispose();
  }
  expect(statusOf(requestNo)).toBe("InfoRequested");

  await signIn(page, account("D1"));
  await page.goto("/si/admin/registrations");
  await expect(page.getByText(si.Review.notAllowed)).toBeVisible();
  await expect(page.getByText(requestNo)).toHaveCount(0);
});

test("QA-F06-09 the reviewer downloads an applicant's document as an attachment, never sniffed", async () => {
  await with_("P2", async (api) => {
    const t6 = tenant("T6").requestNo;
    const file = await api.get(`${reviewAt(t6)}/documents/APPLICANT_NIC/file`);
    expect(file.status()).toBe(200);
    expect(file.headers()["content-type"]).toBe("application/pdf");
    expect(file.headers()["content-disposition"]).toMatch(/^attachment/);
    expect(file.headers()["x-content-type-options"]).toBe("nosniff");
    expect((await file.body()).subarray(0, 5).toString()).toBe("%PDF-");
    expect((await api.get(`${reviewAt(t6)}/documents/NO_SUCH/file`)).status()).toBe(404);
    expect((await api.get(`${reviewAt("REQ-2026-999999")}/documents/APPLICANT_NIC/file`)).status()).toBe(404);
    // The applicant's own route does not serve the reviewer.
    expect((await api.get(`${APPS}/${t6}/documents/APPLICANT_NIC/file`)).status()).not.toBe(200);
  });
});
