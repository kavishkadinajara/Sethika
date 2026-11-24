import { execFileSync } from "node:child_process";
import { existsSync, writeFileSync } from "node:fs";
import path from "node:path";
import { expect, request, test, type APIRequestContext } from "@playwright/test";
import qa from "../../../../scripts/qa/qa-env.json";
import { createAccount, lastTextId, latestCode, password } from "../../support/accounts";
import { clearRateLimits } from "../../support/redis";
import { sql } from "../../support/sql";
import { worldFile, type Persona, type PersonaKey, type Tenant, type TenantKey, type World } from "../support/world";

/*  QA-PLAN section 5: the tenants and people of a QA cycle, made the way real ones are made -
    the platform administrator by the bootstrap command, everyone else by phone OTP sign-up,
    institutions by an application that a platform reviewer reviews and the administrator
    approves (step-up and all), staff by invitation and acceptance, leaders and donor-book
    entries by the owner's and manager's own calls. These are the calls the screens make; the
    screens themselves are what the F-specs test.

    Three things have no product path, so they are done in SQL and each is named in the
    printed table and in docs/qa/cases/README.md (they are findings, not shortcuts):
      - T1's Plus plan: plan selection is deferred (DECISIONS, onboarding "not built"), so the
        application's RequestedPlanId is set before approval, and approval enables the modules;
      - T4's suspension: suspending an institution is deferred (same list);
      - L1's LEADER role: no invitation can carry it (DEF-001), so it is granted as the e2e helpers do;
      - C1's deny override on GIVING.DONATIONS.DELETE: auth.UserPermissionOverrides has no writer.

    All data is fictional. Every QA account's password is the e2e password (support/accounts.ts),
    which keeps signIn / apiAs usable as they are; it exists only on local test databases.   */

const apiURL = `http://localhost:${qa.apiPort}`;
const repo = path.join(__dirname, "..", "..", "..", "..");

type Spec = Omit<Persona, "phone"> & { n: number };
const phoneOf = (n: number) => `07701000${String(n).padStart(2, "0")}`;

const people: Spec[] = [
  { key: "P1", n: 1, name: "Anjali Herath", language: "en", hasAccount: true, role: "PLATFORM_ADMIN (bootstrap)" },
  { key: "P2", n: 2, name: "Tharindu Bandara", language: "en", hasAccount: true, role: "PLATFORM_REVIEWER" },
  { key: "O1", n: 11, name: "Ven. Ananda", language: "si", hasAccount: true, role: "T1 Owner", tenant: "T1" },
  { key: "M1", n: 12, name: "Dilani Karunaratne", language: "si", hasAccount: true, role: "T1 Manager (+ donor)", tenant: "T1" },
  { key: "S1", n: 13, name: "Kasun Rathnayake", language: "si", hasAccount: true, role: "T1 Staff", tenant: "T1" },
  { key: "C1", n: 14, name: "Lakshman Weerasinghe", language: "en", hasAccount: true, role: "T1 Treasurer (custom) + deny DELETE", tenant: "T1" },
  { key: "L1", n: 15, name: "Ven. Sumana", language: "si", hasAccount: true, role: "T1 Religious leader (linked)", tenant: "T1" },
  { key: "L2", n: 16, name: "Ven. Dhamma", language: "si", hasAccount: false, role: "T1 leader, no account", tenant: "T1" },
  { key: "O2", n: 21, name: "Selvam Kanagaratnam", language: "ta", hasAccount: true, role: "T2 Owner", tenant: "T2" },
  { key: "O3", n: 31, name: "Sriyani Abeysekera", language: "en", hasAccount: true, role: "T3 Owner", tenant: "T3" },
  { key: "O4", n: 41, name: "Rohan Fonseka", language: "en", hasAccount: true, role: "T4 Owner (suspended tenant)", tenant: "T4" },
  { key: "A1", n: 51, name: "Imran Farook", language: "en", hasAccount: true, role: "Applicant T5 (rejected) / T6 (clarification)" },
  { key: "D1", n: 61, name: "Nimal Perera", language: "si", hasAccount: true, role: "Donor" },
  { key: "D2", n: 62, name: "Fathima Rizvi", language: "ta", hasAccount: true, role: "Donor (mobile)" },
  { key: "D3", n: 63, name: "Ruwan Silva", language: "en", hasAccount: false, role: "New donor, signs up in F01" },
  { key: "D4", n: 64, name: "Kamal Jayasinghe", language: "en", hasAccount: true, role: "Donor, suspended mid-test (F02)" },
  { key: "D5", n: 65, name: "Malini Fernando", language: "en", hasAccount: true, role: "Donor, co-sponsor" },
  { key: "D6", n: 66, name: "Saman Dias", language: "si", hasAccount: false, role: "T1 donor-book entry, no account", tenant: "T1" },
  { key: "V1", n: 0, name: "Visitor", language: "en", hasAccount: false, role: "Not signed in" },
];

type TenantSpec = {
  key: TenantKey;
  name: string;
  type: string;
  applicant: PersonaKey;
  district: string;
  city: string;
  at: [number, number];
  authority: string;
};

const tenants: TenantSpec[] = [
  { key: "T1", name: "Sri Bodhi Rukkharama", type: "TEMPLE", applicant: "O1", district: "KDY", city: "Kandy", at: [7.2906, 80.6337], authority: "Department of Buddhist Affairs" },
  { key: "T2", name: "Sivan Kovil Jaffna", type: "KOVIL", applicant: "O2", district: "JAF", city: "Jaffna", at: [9.6615, 80.0255], authority: "Department of Hindu Religious and Cultural Affairs" },
  { key: "T3", name: "Sahana Elders' Home", type: "ELDERS_HOME", applicant: "O3", district: "CMB", city: "Colombo", at: [6.9271, 79.8612], authority: "National Secretariat for Elders" },
  { key: "T4", name: "St. Anne's Children's Home", type: "CHILDRENS_HOME", applicant: "O4", district: "GLL", city: "Galle", at: [6.0535, 80.221], authority: "Department of Probation and Child Care Services" },
  { key: "T5", name: "Al-Noor Mosque", type: "MOSQUE", applicant: "A1", district: "PTM", city: "Puttalam", at: [8.0362, 79.8283], authority: "Department of Muslim Religious and Cultural Affairs" },
  { key: "T6", name: "St. Mary's Church Negombo", type: "CHURCH", applicant: "A1", district: "GMP", city: "Negombo", at: [7.2083, 79.8358], authority: "Department of Christian Religious Affairs" },
];

const personas = {} as Record<PersonaKey, Persona>;
for (const p of people) personas[p.key] = { key: p.key, name: p.name, phone: p.n ? phoneOf(p.n) : "", language: p.language, hasAccount: p.hasAccount, role: p.role, tenant: p.tenant };

async function ok(response: Awaited<ReturnType<APIRequestContext["post"]>>, what: string) {
  if (!response.ok()) throw new Error(`${what}: ${response.status()} ${await response.text()}`);
  return response;
}

/** An API session as <key>, signed in with the password, straight to the API. */
async function as(key: PersonaKey): Promise<APIRequestContext> {
  await clearRateLimits();
  const login = await request.newContext({ baseURL: apiURL });
  const response = await ok(await login.post("/api/auth/login", { data: { login: personas[key].phone, password } }), `Login ${key}`);
  const { accessToken } = (await response.json()) as { accessToken: string };
  await login.dispose();
  return request.newContext({ baseURL: apiURL, extraHTTPHeaders: { Authorization: `Bearer ${accessToken}` } });
}

/** Proves who <key> is with a texted code on this session, as the step-up dialog does. */
async function stepUp(api: APIRequestContext, key: PersonaKey) {
  const before = lastTextId(personas[key].phone);
  await ok(await api.post("/api/auth/stepup/request"), `Step-up request ${key}`);
  const { code } = await latestCode(personas[key].phone, before);
  await ok(await api.post("/api/auth/stepup/verify", { data: { code } }), `Step-up verify ${key}`);
}

/** Runs a change; when the API asks for a step-up first, gives one and runs it again. */
async function withStepUp(api: APIRequestContext, key: PersonaKey, what: string, change: () => ReturnType<APIRequestContext["post"]>) {
  let response = await change();
  if (response.status() === 403 && (await response.text()).includes("stepup_required")) {
    await stepUp(api, key);
    response = await change();
  }
  return ok(response, what);
}

const APPS = "/api/onboarding/applications";
const REVIEW = "/api/admin/registrations";

type Lookups = { documents: { institutionType: string; code: string; isMandatory: boolean }[]; declaration: { contentSha256: string } | null };

async function submitApplication(t: TenantSpec, index: number): Promise<string> {
  const applicant = personas[t.applicant];
  const api = await as(t.applicant);
  try {
    const lookups = (await (await ok(await api.get(`${APPS}/lookups?language=en`), "Lookups")).json()) as Lookups;
    const created = await ok(
      await api.post(`${APPS}/`, {
        data: {
          institutionType: t.type,
          proposedName: t.name,
          officialRegistrationNo: `QA/${t.key}/${2010 + index}/${100 + index}`,
          registeringAuthority: t.authority,
          registrationDate: "2012-03-15",
          establishedYear: 1960 + index * 5,
          addressLine1: `${10 + index} Temple Road`,
          city: t.city,
          district: t.district,
          latitude: t.at[0],
          longitude: t.at[1],
          officePhone: `081223${String(4500 + index).padStart(4, "0")}`,
          applicantFullName: applicant.name,
          applicantNic: `19${70 + index}1234567${index}`,
          applicantDesignation: t.type === "TEMPLE" ? "Chief Incumbent" : "Secretary",
          applicantPhone: applicant.phone,
        },
      }),
      `Create application ${t.key}`,
    );
    const { requestNo } = (await created.json()) as { requestNo: string };
    const at = `${APPS}/${encodeURIComponent(requestNo)}`;
    await ok(
      await api.put(`${at}/office-bearers`, {
        data: { officeBearers: [{ fullName: `${applicant.name}`, nic: `19${60 + index}7654321${index}`, designation: "Chairman", phone: applicant.phone, email: null, isSignatory: true }] },
      }),
      `Office bearers ${t.key}`,
    );
    for (const document of lookups.documents.filter((d) => d.institutionType === t.type && d.isMandatory)) {
      await ok(
        await api.put(`${at}/documents/${document.code}`, {
          multipart: {
            file: { name: `${document.code.toLowerCase()}.pdf`, mimeType: "application/pdf", buffer: Buffer.from(`%PDF-1.4\n% QA ${t.key} ${document.code}\n%%EOF\n`) },
            referenceNo: `QA-${t.key}-${document.code}`,
            issuedDate: "2012-03-15",
            expiryDate: "2030-12-31",
          },
        }),
        `Upload ${t.key} ${document.code}`,
      );
    }
    await ok(await api.post(`${at}/submit`, { data: { declarationSha256: lookups.declaration!.contentSha256 } }), `Submit ${t.key}`);
    return requestNo;
  } finally {
    await api.dispose();
  }
}

/** P2 opens the review, verifies every document and passes the checklist. */
async function review(reviewer: APIRequestContext, requestNo: string) {
  const at = `${REVIEW}/${encodeURIComponent(requestNo)}`;
  await ok(await reviewer.post(`${at}/start-review`), `Start review ${requestNo}`);
  const view = (await (await ok(await reviewer.get(at), "Review view")).json()) as {
    application: { documents: { documentType: string }[] };
    checklist: { code: string }[];
  };
  for (const document of view.application.documents) {
    await ok(await reviewer.put(`${at}/documents/${document.documentType}/verification`, { data: { verified: true, remarks: null } }), "Verify document");
  }
  await ok(await reviewer.put(`${at}/checklist`, { data: { items: view.checklist.map((c) => ({ code: c.code, passed: true, remarks: null })) } }), "Checklist");
}

function gitCommit(): string {
  try {
    return execFileSync("git", ["rev-parse", "--short", "HEAD"], { cwd: repo, encoding: "utf8" }).trim();
  } catch {
    return "unknown";
  }
}

function printTable(world: World) {
  const rows = Object.values(world.personas).map((p) => `${p.key.padEnd(3)} ${p.name.padEnd(22)} ${(p.phone || "-").padEnd(11)} ${p.language}  ${p.hasAccount ? "account " : "no acct "} ${p.role}`);
  const sites = Object.values(world.tenants).map((t) => `${t.key}  ${t.name.padEnd(28)} ${t.type.padEnd(15)} ${t.status.padEnd(14)} ${t.requestNo}  ${t.slug ?? "-"}`);
  console.log(`\nQA world on ${qa.database}, built ${world.builtAt} at ${world.commit}\n\n${sites.join("\n")}\n\n${rows.join("\n")}\n`);
  console.log("Set in SQL, no product path: T1 Plus plan (RequestedPlanId before approval), T4 suspension, C1 deny override on GIVING.DONATIONS.DELETE.\n");
}

test("QA world: T1-T6 and every persona, through the product", async () => {
  const [database] = sql("SELECT DB_NAME();");
  expect(database, "the personas are only ever built on the QA database").toBe("Sethika_QA");
  const [users] = sql("SELECT COUNT(*) FROM auth.Users;");
  if (users !== "0") {
    if (existsSync(worldFile)) {
      console.log(`${qa.database} already has its personas (${users} accounts); run qa.ps1 reset for a new cycle.`);
      return;
    }
    throw new Error(`${qa.database} has ${users} accounts but no ${worldFile}: run qa.ps1 reset first.`);
  }

  // P1: the bootstrap command, as the first administrator of a new deployment is made (plan Q5).
  const bin = path.join(repo, "src", "Sethika.Api", "bin", "Debug", "net10.0");
  const bootstrap = execFileSync("dotnet", [path.join(bin, "Sethika.Api.dll"), "bootstrap"], {
    cwd: bin,
    encoding: "utf8",
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: "Development",
      ConnectionStrings__Sql: `Server=${qa.sqlServer};Database=${qa.database};Integrated Security=True;Encrypt=True;TrustServerCertificate=True`,
      ConnectionStrings__Redis: `${qa.redisHost}:${qa.redisPort},defaultDatabase=${qa.redisDb}`,
      Sms__Provider: "Console",
      Push__Provider: "Console",
      Bootstrap__PhoneNumber: `+94${personas.P1.phone.slice(1)}`,
      Bootstrap__Password: password,
      Bootstrap__FullName: personas.P1.name,
    },
  });
  expect(bootstrap).toContain("Created the platform administrator.");

  // Everyone else with an account signs up with a texted code, in their own language.
  for (const p of people.filter((p) => p.hasAccount && p.key !== "P1")) {
    await createAccount(apiURL, p.name, p.language, personas[p.key].phone);
  }

  const admin = await as("P1");
  try {
    // P2 becomes a platform reviewer: the administrator finds the account and grants the role.
    const found = (await (await ok(await admin.get(`/api/admin/users?search=${encodeURIComponent(personas.P2.name)}`), "Find P2")).json()) as { items: { id: string }[] };
    expect(found.items).toHaveLength(1);
    await withStepUp(admin, "P1", "Grant PLATFORM_REVIEWER", () => admin.post(`/api/admin/users/${found.items[0].id}/platform-roles`, { data: { role: "PLATFORM_REVIEWER" } }));
  } finally {
    await admin.dispose();
  }

  // T1-T5 apply; P2 reviews; P1 decides. T6 applies after T5's rejection, as the same applicant.
  const requests = {} as Record<TenantKey, string>;
  for (const [i, t] of tenants.slice(0, 5).entries()) requests[t.key] = await submitApplication(t, i + 1);

  const reviewer = await as("P2");
  try {
    for (const key of ["T1", "T2", "T3", "T4", "T5"] as const) await review(reviewer, requests[key]);
  } finally {
    await reviewer.dispose();
  }

  // No product path: plan selection is deferred, so T1's application asks for Plus here.
  sql("UPDATE org.RegistrationRequests SET RequestedPlanId = (SELECT Id FROM org.Plans WHERE Code = 'PLUS') WHERE RequestNo = '$(no)';", { no: requests.T1 });

  const slugs = {} as Record<TenantKey, string | null>;
  const decider = await as("P1");
  try {
    for (const key of ["T1", "T2", "T3", "T4"] as const) {
      const approved = await withStepUp(decider, "P1", `Approve ${key}`, () => decider.post(`${REVIEW}/${encodeURIComponent(requests[key])}/approve`));
      slugs[key] = ((await approved.json()) as { slug: string }).slug;
    }
    await withStepUp(decider, "P1", "Reject T5", () =>
      decider.post(`${REVIEW}/${encodeURIComponent(requests.T5)}/reject`, { data: { reason: "NOT_VERIFIABLE", note: "QA: the registration number could not be found with the department." } }),
    );
  } finally {
    await decider.dispose();
  }
  slugs.T5 = null;

  requests.T6 = await submitApplication(tenants[5], 6);
  const clarifier = await as("P2");
  try {
    const at = `${REVIEW}/${encodeURIComponent(requests.T6)}`;
    await ok(await clarifier.post(`${at}/start-review`), "Start review T6");
    await ok(await clarifier.post(`${at}/questions`, { data: { questions: ["QA: please upload a clearer copy of the trust deed's first page."] } }), "Ask T6");
  } finally {
    await clarifier.dispose();
  }
  slugs.T6 = null;

  // No product path: suspending an institution is deferred. P1 is recorded as the one who did it.
  sql(
    `DECLARE @i int = (SELECT Id FROM org.Institutions WHERE Slug = '$(slug)');
     DECLARE @p uniqueidentifier = (SELECT Id FROM auth.Users WHERE RIGHT(PhoneNumber, 9) = '$(p1)');
     UPDATE org.Institutions SET Status = 'Suspended', PermissionsVersion = PermissionsVersion + 1 WHERE Id = @i;
     INSERT org.InstitutionStatusHistory (InstitutionId, FromStatus, ToStatus, Reason, ChangedBy) VALUES (@i, 'Active', 'Suspended', N'QA setup: no product path (deferred)', @p);`,
    { slug: slugs.T4!, p1: personas.P1.phone.slice(1) },
  );

  // T1's people: a custom Treasurer role, then invitations that each person accepts.
  const owner = await as("O1");
  try {
    await withStepUp(owner, "O1", "Create TREASURER", () =>
      owner.post("/api/institution/roles", {
        data: {
          code: "TREASURER",
          name: "Treasurer",
          level: "MANAGER", // STAFF's ceiling has no reports, export or void (outside_ceiling)
          permissions: ["GIVING.DONATIONS.VIEW", "GIVING.DONATIONS.CREATE", "GIVING.DONATIONS.DELETE", "GIVING.DONATIONS.EXPORT", "GIVING.REPORTS.VIEW", "GIVING.REPORTS.EXPORT", "GIVING.REPORTS.PRINT", "GIVING.PAYMENTS.VIEW"],
        },
      }),
    );
    for (const [key, role] of [["M1", "MANAGER"], ["S1", "STAFF"], ["C1", "TREASURER"]] as const) {
      await withStepUp(owner, "O1", `Invite ${key}`, () => owner.post("/api/institution/invitations", { data: { phone: personas[key].phone, role } }));
    }
  } finally {
    await owner.dispose();
  }
  for (const key of ["M1", "S1", "C1"] as const) {
    const api = await as(key);
    try {
      const mine = (await (await ok(await api.get("/api/me/invitations"), `Invitations of ${key}`)).json()) as { id: string; slug: string }[];
      const invitation = mine.find((i) => i.slug === slugs.T1);
      expect(invitation, `${key}'s invitation to T1`).toBeTruthy();
      await ok(await api.post(`/api/me/invitations/${invitation!.id}/accept`), `Accept ${key}`);
    } finally {
      await api.dispose();
    }
  }

  // No product path: user-level overrides have no writer. C1 may not void, whatever the role says.
  sql(
    `DECLARE @c uniqueidentifier = (SELECT Id FROM auth.Users WHERE RIGHT(PhoneNumber, 9) = '$(c1)');
     DECLARE @o uniqueidentifier = (SELECT Id FROM auth.Users WHERE RIGHT(PhoneNumber, 9) = '$(o1)');
     INSERT auth.UserPermissionOverrides (UserId, InstitutionId, PermissionId, Effect, Reason, CreatedBy)
     SELECT @c, i.Id, p.Id, 'Deny', N'QA: treasurer records but never voids (maker-checker)', @o
     FROM org.Institutions i CROSS JOIN auth.Permissions p WHERE i.Slug = '$(slug)' AND p.Code = 'GIVING.DONATIONS.DELETE';
     UPDATE auth.Users SET PermissionsVersion = PermissionsVersion + 1 WHERE Id = @c;`,
    { c1: personas.C1.phone.slice(1), o1: personas.O1.phone.slice(1), slug: slugs.T1! },
  );

  // T1's leaders (L1 linked to an account, which gives it T1's LEADER role - DEF-001; L2 without one) and D6 in the donor book.
  const ownerAgain = await as("O1");
  try {
    await ok(await ownerAgain.post("/api/institution/leaders", { data: { displayName: personas.L1.name, nameSi: null, nameTa: null, title: "Ven.", phone: personas.L1.phone, language: "si", linkAccount: true } }), "Leader L1");
    await ok(await ownerAgain.post("/api/institution/leaders", { data: { displayName: personas.L2.name, nameSi: null, nameTa: null, title: "Ven.", phone: personas.L2.phone, language: "si", linkAccount: false } }), "Leader L2");
  } finally {
    await ownerAgain.dispose();
  }
  const leader = await as("L1");
  try {
    await ok(await leader.get("/api/institution/schedule"), "L1's schedule, through the link alone");
  } finally {
    await leader.dispose();
  }
  const manager = await as("M1");
  try {
    await ok(await manager.post("/api/institution/donors", { data: { fullName: personas.D6.name, phone: personas.D6.phone, notes: "QA: standing sponsor without an account", language: "si", textConsent: true } }), "Donor book D6");
  } finally {
    await manager.dispose();
  }

  const status = (key: TenantKey) =>
    slugs[key]
      ? sql("SELECT Status FROM org.Institutions WHERE Slug = '$(slug)';", { slug: slugs[key]! })[0]
      : sql("SELECT Status FROM org.RegistrationRequests WHERE RequestNo = '$(no)';", { no: requests[key] })[0];
  const built = {} as Record<TenantKey, Tenant>;
  for (const t of tenants) built[t.key] = { key: t.key, name: t.name, type: t.type, requestNo: requests[t.key], slug: slugs[t.key], status: status(t.key) };

  // What the plan expects each tenant to be, checked before anyone relies on it.
  expect(Object.values(built).map((t) => `${t.key}:${t.status}`)).toEqual(["T1:Active", "T2:Active", "T3:Active", "T4:Suspended", "T5:Rejected", "T6:InfoRequested"]);

  const world: World = { builtAt: new Date().toISOString(), commit: gitCommit(), personas, tenants: built };
  writeFileSync(worldFile, JSON.stringify(world, null, 2));
  printTable(world);
});
