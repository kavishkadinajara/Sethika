import type { APIRequestContext } from "@playwright/test";
import { apiAs, type Account } from "./accounts";
import type { Institution } from "./institutions";
import { sql } from "./sql";

/*  Religious services (Phase E), set up through the API for the steps a scenario does not test on
    screen: a service type, a leader, a donor's request, an assignment. The owner's token is an
    institution session (one institution), the donor's a personal one, as signing in gives them. */

async function ok(response: Awaited<ReturnType<APIRequestContext["post"]>>, what: string) {
  if (!response.ok()) throw new Error(`${what}: ${response.status()} ${await response.text()}`);
  return response;
}

async function asOwner<T>(baseURL: string, institution: Institution, work: (api: APIRequestContext) => Promise<T>): Promise<T> {
  const api = await apiAs(baseURL, institution.owner);
  try {
    return await work(api);
  } finally {
    await api.dispose();
  }
}

/** A service type made by the owner; its public id. Two hours, one leader, here or at home. */
export function serviceTypeOf(baseURL: string, institution: Institution, name = "Pirith"): Promise<string> {
  return asOwner(baseURL, institution, async (api) => {
    const response = await ok(
      await api.post("/api/institution/service-types", {
        data: {
          name,
          nameSi: null,
          nameTa: null,
          description: null,
          durationMinutes: 120,
          bufferMinutes: 0,
          leadersRequired: 1,
          locationMode: "Either",
          minAdvanceDays: 1,
          maxAdvanceDays: 120,
        },
      }),
      "Create service type",
    );
    return ((await response.json()) as { publicId: string }).publicId;
  });
}

/** A leader on the owner's roster, linked to <account> when one is given; their public id. */
export function leaderOf(baseURL: string, institution: Institution, name: string, account?: Account): Promise<string> {
  return asOwner(baseURL, institution, async (api) => {
    const response = await ok(
      await api.post("/api/institution/leaders", {
        data: { displayName: name, nameSi: null, nameTa: null, title: "Ven.", phone: account?.phone ?? null, language: "en", linkAccount: account !== undefined },
      }),
      "Create leader",
    );
    return ((await response.json()) as { publicId: string }).publicId;
  });
}

/** A request by <donor> for <date> at 10:00 Colombo, at the institution; its number. */
export async function requestBy(baseURL: string, donor: Account, serviceType: string, date: string): Promise<string> {
  const api = await apiAs(baseURL, donor);
  try {
    const response = await ok(
      await api.post("/api/appointments", {
        data: { serviceType, date, startTime: "10:00", location: "AtInstitution", address: null, occasion: null, note: null },
      }),
      "Request a service",
    );
    return ((await response.json()) as { appointmentNo: string }).appointmentNo;
  } finally {
    await api.dispose();
  }
}

/** Assigns leaders (public ids) to an appointment, as the owner. */
export function assign(baseURL: string, institution: Institution, appointmentNo: string, leaders: string[]): Promise<void> {
  return asOwner(baseURL, institution, async (api) => {
    await ok(await api.post(`/api/institution/appointments/${encodeURIComponent(appointmentNo)}/leaders`, { data: { leaders } }), "Assign leaders");
  });
}

/** Gives <account> the institution's copy of a template role (LEADER, STAFF...), as the integration tests do. */
export function grantInstitutionRole(account: Account, institution: Institution, roleCode: string) {
  const last9 = account.phone.replace(/\D/g, "").slice(-9);
  sql(
    `DECLARE @user uniqueidentifier = (SELECT Id FROM auth.Users WHERE RIGHT(PhoneNumber, 9) = '$(last9)');
     DECLARE @institution int = (SELECT Id FROM org.Institutions WHERE Slug = '$(slug)');
     INSERT auth.UserRoles (UserId, RoleId, InstitutionId, IsActive)
     SELECT @user, r.Id, r.InstitutionId, 1 FROM auth.Roles r WHERE r.InstitutionId = @institution AND r.Code = '$(role)';
     UPDATE auth.Users SET PermissionsVersion = PermissionsVersion + 1 WHERE Id = @user;`,
    { last9, slug: institution.slug, role: roleCode },
  );
}
