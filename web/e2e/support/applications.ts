import type { APIRequestContext } from "@playwright/test";

/*  For scenarios that start after the form - a reviewer's questions, a decision - the
    application is made through the applicant API, the same calls the screens make, so each such
    test does not spend a minute typing the form again. The form itself has its own tests.   */

const BASE = "/api/onboarding/applications";

type Lookups = {
  institutionTypes: { code: string }[];
  districts: { code: string }[];
  documents: { institutionType: string; code: string; isMandatory: boolean }[];
  declaration: { contentSha256: string } | null;
};

async function ok(response: Awaited<ReturnType<APIRequestContext["get"]>>, what: string) {
  if (!response.ok()) throw new Error(`${what}: ${response.status()} ${await response.text()}`);
  return response;
}

/** A registration number no other test is using: two open applications cannot share one. */
export function uniqueRegistrationNo(): string {
  return `E2E/${Date.now()}/${Math.floor(Math.random() * 1_000_000)}`;
}

/** Details that pass every rule, with a name the test can recognise. */
export function sampleDetails(institutionType: string, district: string, name: string) {
  return {
    institutionType,
    proposedName: name,
    officialRegistrationNo: uniqueRegistrationNo(),
    registeringAuthority: "Department of Buddhist Affairs",
    registrationDate: "2015-06-01",
    establishedYear: 1950,
    addressLine1: "12 Temple Road",
    city: "Galle",
    district,
    latitude: 6.0329,
    longitude: 80.2168,
    officePhone: "0912234567",
    applicantFullName: "E2E Applicant",
    applicantNic: "199012345678",
    applicantDesignation: "Secretary",
    applicantPhone: "0771234567",
  };
}

/** A new application, submitted with every mandatory document, by the signed-in <api>. */
export async function submittedApplication(api: APIRequestContext, name = "E2E Sri Sumana Temple"): Promise<string> {
  const lookups = (await (await ok(await api.get(`${BASE}/lookups?language=en`), "Lookups")).json()) as Lookups;
  const type = lookups.institutionTypes[0].code;

  const created = await ok(await api.post(`${BASE}/`, { data: sampleDetails(type, lookups.districts[0].code, name) }), "Create");
  const { requestNo } = (await created.json()) as { requestNo: string };
  const at = `${BASE}/${encodeURIComponent(requestNo)}`;

  await ok(
    await api.put(`${at}/office-bearers`, {
      data: { officeBearers: [{ fullName: "E2E Bearer", nic: "198512345678", designation: "Chairman", phone: "0772345678", email: null, isSignatory: true }] },
    }),
    "Office bearers",
  );

  for (const document of lookups.documents.filter((d) => d.institutionType === type && d.isMandatory)) {
    await ok(
      await api.put(`${at}/documents/${document.code}`, {
        multipart: {
          file: { name: `${document.code.toLowerCase()}.pdf`, mimeType: "application/pdf", buffer: Buffer.from("%PDF-1.4\n%%EOF\n") },
          referenceNo: "E2E-REF-1",
          issuedDate: "2015-06-01",
          expiryDate: "2030-12-31",
        },
      }),
      `Upload ${document.code}`,
    );
  }

  await ok(await api.post(`${at}/submit`, { data: { declarationSha256: lookups.declaration!.contentSha256 } }), "Submit");
  return requestNo;
}
