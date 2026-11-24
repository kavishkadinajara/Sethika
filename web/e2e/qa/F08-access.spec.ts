import type { APIRequestContext } from "@playwright/test";
import si from "../../messages/si.json";
import { signIn, type Account } from "../support/accounts";
import { sql } from "../support/sql";
import { expect, scrollsSideways, test } from "../support/test";
import {
  codeOf,
  freePhone,
  signedIn,
  stepUpApi,
  throwaway,
  userRow,
} from "./support/api";
import {
  answer,
  account,
  evidence,
  tenant,
  type PersonaKey,
} from "./support/world";

/*  F08 - Roles, members, invitations, effective permissions (P1). Cases: docs/qa/cases/F08-access.md.
    T1's people: O1 Owner, M1 Manager, S1 Staff, C1 Treasurer (custom, Manager level, deny override
    on GIVING.DONATIONS.DELETE), L1 Leader. Changes are made to throwaways and to roles made by
    the case; the TREASURER edit in QA-F08-05 is put back before the case ends.
    The plan's "plan staff limit" (QA-F08-08) is N/A: no code reads org.Plans.MaxStaffUsers (OBS-02). */

type Who = PersonaKey | Account;

async function with_<T>(
  key: Who,
  work: (api: APIRequestContext) => Promise<T>,
  step = false,
): Promise<T> {
  const who = typeof key === "string" ? account(key) : key;
  const api = await signedIn(who);
  try {
    if (step) await stepUpApi(api, who);
    return await work(api);
  } finally {
    await api.dispose();
  }
}

const one = (query: string, vars: Record<string, string> = {}) =>
  sql(query, vars)[0];
const t1Roles = () =>
  one(
    "SELECT COUNT(*) FROM auth.Roles r JOIN org.Institutions i ON i.Id = r.InstitutionId WHERE i.Slug = '$(s)';",
    { s: tenant("T1").slug! },
  );
const idOf = (key: PersonaKey | Account) =>
  userRow((typeof key === "string" ? account(key) : key).phone)!.id;

type Role = {
  code: string;
  level: string;
  version: string;
  permissions: string[];
  canManage: boolean;
};

test("QA-F08-01 O1 sees T1's roles and people on the access screens, in Sinhala, at every width @phone", async ({
  page,
}, testInfo) => {
  await signIn(page, account("O1"), "si");
  await page.goto("/si/access/members");
  for (const name of [
    "Dilani Karunaratne",
    "Kasun Rathnayake",
    "Lakshman Weerasinghe",
    "Ven. Sumana",
  ])
    await expect(page.getByText(name).first()).toBeVisible();
  for (const outsider of [
    "Selvam Kanagaratnam",
    "Sriyani Abeysekera",
    "Nimal Perera",
  ])
    await expect(page.getByText(outsider)).toHaveCount(0);
  expect(await scrollsSideways(page)).toBe(false);
  await evidence(page, "QA-F08-01", testInfo, "members");
  await page.goto("/si/access/roles");
  const list = page.getByRole("navigation", { name: si.Access.roles.list });
  // System roles in Sinhala; the custom one keeps the name O1 gave it.
  for (const name of [
    "හිමිකරු",
    "කළමනාකරු",
    "කාර්ය මණ්ඩලය",
    "ආගමික නායකයා",
    "Treasurer",
  ])
    await expect(
      list.getByRole("button", { name: new RegExp(`^${name} `) }).first(),
    ).toBeVisible();
  expect(await scrollsSideways(page)).toBe(false);
  await evidence(page, "QA-F08-01", testInfo, "roles");
});

test("QA-F08-02 anti-escalation: a role is made only below one's rank, inside its level, and of permissions one holds", async () => {
  const before = t1Roles();
  await with_("M1", async (api) => {
    const create = async (
      code: string,
      level: string,
      permissions: string[],
      name = "QA role",
    ) =>
      answer(
        await api.post("/api/institution/roles", {
          data: { code, name, level, permissions },
        }),
      );
    const table: [string, string, string[], number, string, string[]?][] = [
      // code, level, permissions -> status, code, offending permissions
      ["QA_PEER", "MANAGER", ["GIVING.DONATIONS.VIEW"], 403, "rank_not_junior"],
      [
        "QA_OWNERISH",
        "OWNER",
        ["GIVING.DONATIONS.VIEW"],
        403,
        "rank_not_junior",
      ],
      [
        "QA_CEILING",
        "STAFF",
        ["GIVING.REPORTS.VIEW"],
        403,
        "outside_ceiling",
        ["GIVING.REPORTS.VIEW"],
      ],
      [
        "QA_NOTHELD",
        "STAFF",
        ["SERVICES.MYSCHEDULE.VIEW"],
        403,
        "not_held",
        ["SERVICES.MYSCHEDULE.VIEW"],
      ],
      [
        "QA_PLATFORM",
        "STAFF",
        ["PLATFORM.USERS.VIEW"],
        403,
        "outside_ceiling",
        ["PLATFORM.USERS.VIEW"],
      ],
      [
        "QA_UNKNOWN",
        "STAFF",
        ["NO.SUCH.THING"],
        400,
        "unknown_permission",
        ["NO.SUCH.THING"],
      ],
      ["QA_LEVEL", "CAPTAIN", ["GIVING.DONATIONS.VIEW"], 400, "unknown_level"],
      [
        "qa bad code",
        "STAFF",
        ["GIVING.DONATIONS.VIEW"],
        400,
        "invalid_role_code",
      ],
      ["TREASURER", "STAFF", ["GIVING.DONATIONS.VIEW"], 409, "role_exists"],
    ];
    for (const [code, level, permissions, status, error, offending] of table) {
      const r = await create(code, level, permissions);
      expect(
        [r.status, r.body?.code],
        `${code} ${level} ${permissions}`,
      ).toEqual([status, error]);
      if (offending) expect(r.body?.permissions, code).toEqual(offending);
    }
    expect(
      (await create("QA_NONAME", "STAFF", ["GIVING.DONATIONS.VIEW"], " ")).body
        ?.code,
    ).toBe("invalid_role_name");
  });
  expect(t1Roles(), "no refused role was made").toBe(before);

  // The control row: a junior role of permissions M1 holds is made, and takes nothing else with it.
  const code = `QA_HELPER_${Date.now() % 100000}`;
  await with_("M1", async (api) => {
    const made = await answer(
      await api.post("/api/institution/roles", {
        data: {
          code,
          name: "QA Kitchen helper",
          level: "STAFF",
          permissions: ["BOOKINGS.CALENDAR.VIEW", "GIVING.DONATIONS.VIEW"],
        },
      }),
    );
    expect(made.status).toBe(201);
    expect((made.body as unknown as Role).permissions).toEqual([
      "BOOKINGS.CALENDAR.VIEW",
      "GIVING.DONATIONS.VIEW",
    ]);
    // And editing it is held to the same rules.
    const role = (
      (await (await api.get("/api/institution/roles")).json()) as Role[]
    ).find((r) => r.code === code)!;
    const widened = await answer(
      await api.put(`/api/institution/roles/${code}/permissions`, {
        data: {
          permissions: [...role.permissions, "SERVICES.MYSCHEDULE.VIEW"],
          version: role.version,
        },
      }),
    );
    expect(widened.body?.code, "needs a step-up first").toBe("stepup_required");
  });
  await with_(
    "M1",
    async (api) => {
      const role = (
        (await (await api.get("/api/institution/roles")).json()) as Role[]
      ).find((r) => r.code === code)!;
      const widened = await answer(
        await api.put(`/api/institution/roles/${code}/permissions`, {
          data: {
            permissions: [...role.permissions, "SERVICES.MYSCHEDULE.VIEW"],
            version: role.version,
          },
        }),
      );
      expect([widened.status, widened.body?.code]).toEqual([403, "not_held"]);
    },
    true,
  );
  // Institution roles never hold a platform permission, whoever asks.
  await with_(
    "O1",
    async (api) => {
      const r = await answer(
        await api.post("/api/institution/roles", {
          data: {
            code: "QA_PLATFORM_O",
            name: "QA",
            level: "MANAGER",
            permissions: ["ONBOARDING.REQUESTS.APPROVE"],
          },
        }),
      );
      expect(r.status).toBe(403);
    },
    true,
  );
});

test("QA-F08-03 rank: nobody hands out, or takes away, a role at or above their own, nor their own roles", async () => {
  const helper = await throwaway("rank");
  await with_(
    "O1",
    async (api) =>
      expect(
        (
          await api.post("/api/institution/invitations", {
            data: { phone: helper.phone, role: "STAFF" },
          })
        ).status(),
      ).toBe(201),
    true,
  );
  await with_(helper, async (api) => {
    const [invitation] = (await (
      await api.get("/api/me/invitations")
    ).json()) as { id: string }[];
    expect(
      (await api.post(`/api/me/invitations/${invitation.id}/accept`)).status(),
    ).toBe(204);
  });

  await with_(
    "M1",
    async (api) => {
      const assign = async (role: string, user: string) =>
        answer(
          await api.post(`/api/institution/roles/${role}/members`, {
            data: { user },
          }),
        );
      const revoke = async (role: string, user: string) =>
        answer(
          await api.delete(`/api/institution/roles/${role}/members/${user}`),
        );
      const table: [
        string,
        () => Promise<{ status: number; body: Record<string, unknown> | null }>,
        number,
        string,
      ][] = [
        [
          "M1 gives MANAGER to the helper",
          () => assign("MANAGER", idOf(helper)),
          403,
          "rank_not_junior",
        ],
        [
          "M1 gives TREASURER (Manager level)",
          () => assign("TREASURER", idOf(helper)),
          403,
          "rank_not_junior",
        ],
        [
          "M1 takes O1's OWNER",
          () => revoke("OWNER", idOf("O1")),
          403,
          "rank_not_junior",
        ],
        [
          "M1 takes C1's TREASURER",
          () => revoke("TREASURER", idOf("C1")),
          403,
          "rank_not_junior",
        ],
        [
          "M1 takes their own MANAGER",
          () => revoke("MANAGER", idOf("M1")),
          403,
          "own_roles",
        ],
        [
          "M1 gives LEADER to themselves",
          () => assign("LEADER", idOf("M1")),
          403,
          "own_roles",
        ],
        [
          "M1 gives STAFF to D1 (not a member)",
          () => assign("STAFF", idOf("D1")),
          404,
          "member_not_found",
        ],
        [
          "M1 takes a role the helper does not hold",
          () => revoke("LEADER", idOf(helper)),
          404,
          "assignment_not_found",
        ],
        [
          "M1 uses a role T1 does not have",
          () => assign("NO_SUCH", idOf(helper)),
          404,
          "role_not_found",
        ],
      ];
      for (const [what, call, status, code] of table) {
        const r = await call();
        expect([r.status, r.body?.code], what).toEqual([status, code]);
      }
      const invite = await answer(
        await api.post("/api/institution/invitations", {
          data: {
            phone: (await throwaway("rank-invite")).phone,
            role: "MANAGER",
          },
        }),
      );
      expect(
        [invite.status, invite.body?.code],
        "M1 invites someone as MANAGER",
      ).toEqual([403, "rank_not_junior"]);
      // Junior to M1: allowed.
      expect(
        (await revoke("STAFF", idOf(helper))).status,
        "M1 takes STAFF from the helper",
      ).toBe(204);
    },
    true,
  );
  await with_(
    "O1",
    async (api) => {
      const own = await answer(
        await api.delete(`/api/institution/roles/OWNER/members/${idOf("O1")}`),
      );
      expect([own.status, own.body?.code], "O1 takes their own OWNER").toEqual([
        403,
        "own_roles",
      ]);
    },
    true,
  );
  expect(
    one(
      "SELECT COUNT(*) FROM auth.UserRoles ur JOIN auth.Roles r ON r.Id = ur.RoleId WHERE ur.UserId = '$(id)' AND ur.IsActive = 1 AND r.Code = 'OWNER';",
      { id: idOf("O1") },
    ),
  ).toBe("1");
});

test("QA-F08-04 effective permissions, as a decision table: role grant x module enabled x deny override - deny wins", async () => {
  const mine = async (key: PersonaKey) =>
    with_(
      key,
      async (api) =>
        (
          (await (await api.get("/api/me/permissions")).json()) as {
            permissions: string[];
          }
        ).permissions,
    );

  // Row 1: granted by the role, module on, no override -> allowed (O1 may void, list services).
  expect(await mine("O1")).toEqual(
    expect.arrayContaining(["GIVING.DONATIONS.DELETE", "SERVICES.TYPES.VIEW"]),
  );
  await with_("O1", async (api) =>
    expect((await api.get("/api/institution/service-types")).status()).toBe(
      200,
    ),
  );

  // Row 2: granted by the role, deny override -> refused. C1's TREASURER role holds DELETE.
  const treasurer = await with_("O1", async (api) =>
    ((await (await api.get("/api/institution/roles")).json()) as Role[]).find(
      (r) => r.code === "TREASURER",
    )!,
  );
  expect(treasurer.permissions).toContain("GIVING.DONATIONS.DELETE");
  const c1 = await mine("C1");
  expect(c1).not.toContain("GIVING.DONATIONS.DELETE");
  expect(c1).toContain("GIVING.DONATIONS.VIEW");
  await with_(
    "C1",
    async (api) => {
      const r = await answer(
        await api.post("/api/institution/donations/RCP-2026-999999/void", {
          data: { reason: "QA" },
        }),
      );
      expect(r.status, "deny wins, even after a step-up").toBe(403);
      expect(r.body?.code).not.toBe("stepup_required");
      expect(
        (await api.get("/api/institution/donations")).status(),
        "what is not denied still works",
      ).toBe(200);
    },
    true,
  );

  // Row 3: granted by the role, module off -> refused. T3 (elders' home) has no services module.
  const o3 = await mine("O3");
  expect(o3.filter((p) => p.startsWith("SERVICES."))).toEqual([]);
  const [o3Role] = sql(
    "SELECT COUNT(*) FROM auth.RolePermissions rp JOIN auth.Roles r ON r.Id = rp.RoleId JOIN auth.Permissions p ON p.Id = rp.PermissionId JOIN org.Institutions i ON i.Id = r.InstitutionId WHERE i.Slug = '$(s)' AND r.Code = 'OWNER' AND p.Code LIKE 'SERVICES.%';",
    { s: tenant("T3").slug! },
  );
  await with_("O3", async (api) =>
    expect(
      (await api.get("/api/institution/service-types")).status(),
      `T3's OWNER role holds ${o3Role} SERVICES grants`,
    ).toBe(403),
  );

  // Row 4: not granted -> refused. S1 (Staff) holds no ACCESS permission.
  expect((await mine("S1")).filter((p) => p.startsWith("ACCESS."))).toEqual([]);
  await with_("S1", async (api) =>
    expect((await api.get("/api/institution/members")).status()).toBe(403),
  );

  // The answer itself: an internal institution Id is not meant to leave the API (CLAUDE.md conventions).
  const raw = await with_("C1", async (api) =>
    (await api.get("/api/me/permissions")).json(),
  );
  expect(
    raw,
    "no internal Id in /api/me/permissions (DEF-005)",
  ).not.toHaveProperty("institutionId");
});

test("QA-F08-05 a role change reaches a live session at its next request, both ways; a stale edit is refused", async () => {
  const report = async (api: APIRequestContext) =>
    (await api.get("/api/institution/reports/monthly")).status();
  const c1 = await signedIn(account("C1"));
  const o1 = await signedIn(account("O1"));
  try {
    await stepUpApi(o1, account("O1"));
    expect(await report(c1)).toBe(200);
    const role = (
      (await (await o1.get("/api/institution/roles")).json()) as Role[]
    ).find((r) => r.code === "TREASURER")!;
    const without = role.permissions.filter((p) => p !== "GIVING.REPORTS.VIEW");
    const cut = await answer(
      await o1.put("/api/institution/roles/TREASURER/permissions", {
        data: { permissions: without, version: role.version },
      }),
    );
    try {
      expect(cut.status).toBe(200);
      expect(await report(c1), "C1's live token, no sign-in again").toBe(403);
      // The version O1 read before the change is stale now.
      const stale = await answer(
        await o1.put("/api/institution/roles/TREASURER/permissions", {
          data: { permissions: role.permissions, version: role.version },
        }),
      );
      expect([stale.status, stale.body?.code]).toEqual([
        409,
        "changed_meanwhile",
      ]);
      const noVersion = await answer(
        await o1.put("/api/institution/roles/TREASURER/permissions", {
          data: { permissions: role.permissions },
        }),
      );
      expect([noVersion.status, noVersion.body?.code]).toEqual([
        400,
        "invalid_version",
      ]);
    } finally {
      const now = (
        (await (await o1.get("/api/institution/roles")).json()) as Role[]
      ).find((r) => r.code === "TREASURER")!;
      const back = await o1.put(
        "/api/institution/roles/TREASURER/permissions",
        { data: { permissions: role.permissions, version: now.version } },
      );
      expect(back.status(), "TREASURER put back").toBe(200);
    }
    expect(await report(c1), "and back, at the next request").toBe(200);
    expect(
      one(
        "SELECT COUNT(*) FROM auth.RolePermissionsHistory h JOIN auth.Roles r ON r.Id = h.RoleId JOIN org.Institutions i ON i.Id = r.InstitutionId WHERE i.Slug = '$(s)' AND r.Code = 'TREASURER';",
        { s: tenant("T1").slug! },
      ) !== "0",
      "the change is in the temporal history",
    ).toBe(true);
  } finally {
    await c1.dispose();
    await o1.dispose();
  }
});

test("QA-F08-06 an invitation's life: one open per person, only theirs to answer, and closed for good once answered, cancelled or expired", async () => {
  const person = await throwaway("invitee");
  // One session for O1 through the case: sign-in is rate limited per account, as it should be.
  const owner = await signedIn(account("O1"));
  await stepUpApi(owner, account("O1"));
  try {
    const invite = async (data: unknown) =>
      answer(await owner.post("/api/institution/invitations", { data }));
    const theirs = async () =>
      with_(
        person,
        async (api) =>
          (await (await api.get("/api/me/invitations")).json()) as {
            id: string;
            slug: string;
          }[],
      );

    const refused: [unknown, number, string][] = [
      [{ phone: account("M1").phone, role: "STAFF" }, 409, "already_member"],
      [{ phone: freePhone("07702"), role: "STAFF" }, 409, "account_not_found"],
      // A landline is a real number, so it is looked up - and no account can have one (sign-up refuses them).
      [{ phone: "0812234567", role: "STAFF" }, 409, "account_not_found"],
      [{ phone: "12345", role: "STAFF" }, 400, "invalid_phone_number"],
      [{ phone: person.phone, role: "NO_SUCH" }, 404, "role_not_found"],
      [{ phone: person.phone, role: "LEADER" }, 403, "not_held"],
    ];
    for (const [data, status, code] of refused) {
      const r = await invite(data);
      expect([r.status, r.body?.code], JSON.stringify(data)).toEqual([
        status,
        code,
      ]);
    }

    // Open, then the same again (twice at once too): one open invitation.
    const both = await Promise.all(
      [0, 1].map(async () =>
        (
          await owner.post("/api/institution/invitations", {
            data: { phone: person.phone, role: "STAFF" },
          })
        ).status(),
      ),
    );
    expect(both.sort(), "two at once").toEqual([201, 409]);
    expect(
      (await invite({ phone: person.phone, role: "STAFF" })).body?.code,
    ).toBe("invitation_pending");
    const [first] = await theirs();
    expect(first.slug).toBe(tenant("T1").slug);

    // Only the invitee can answer it.
    await with_("D1", async (api) => {
      const r = await answer(
        await api.post(`/api/me/invitations/${first.id}/accept`),
      );
      expect([r.status, r.body?.code], "D1 accepts someone else's").toEqual([
        404,
        "invitation_not_found",
      ]);
    });
    // Declined is closed.
    await with_(person, async (api) => {
      expect(
        (await api.post(`/api/me/invitations/${first.id}/decline`)).status(),
      ).toBe(204);
      expect(
        await codeOf(await api.post(`/api/me/invitations/${first.id}/accept`)),
      ).toBe("invitation_closed");
    });
    // Cancelled by O1 is closed.
    expect((await invite({ phone: person.phone, role: "STAFF" })).status).toBe(
      201,
    );
    const [second] = await theirs();
    expect(
      (
        await owner.post(`/api/institution/invitations/${second.id}/cancel`)
      ).status(),
    ).toBe(204);
    await with_(person, async (api) =>
      expect(
        await codeOf(await api.post(`/api/me/invitations/${second.id}/accept`)),
      ).toBe("invitation_closed"),
    );
    // Expired (its clock moved back in SQL, as a case "waits" past an expiry) is closed, and does not block a new one.
    expect((await invite({ phone: person.phone, role: "STAFF" })).status).toBe(
      201,
    );
    const [third] = await theirs();
    sql(
      "UPDATE org.StaffInvitations SET ExpiresAt = DATEADD(MINUTE, -1, SYSUTCDATETIME()) WHERE PublicId = '$(id)';",
      { id: third.id },
    );
    await with_(person, async (api) =>
      expect(
        await codeOf(await api.post(`/api/me/invitations/${third.id}/accept`)),
      ).toBe("invitation_expired"),
    );
    expect(
      (await invite({ phone: person.phone, role: "STAFF" })).status,
      "an expired one does not block",
    ).toBe(201);
    const [fourth] = await theirs();
    await with_(person, async (api) =>
      expect(
        (await api.post(`/api/me/invitations/${fourth.id}/accept`)).status(),
      ).toBe(204),
    );
    // A member now, with STAFF's permissions and no more.
    const staff = await with_(
      person,
      async (api) =>
        (
          (await (await api.get("/api/me/permissions")).json()) as {
            permissions: string[];
          }
        ).permissions,
    );
    expect(staff).toContain("BOOKINGS.CALENDAR.VIEW");
    expect(staff.filter((p) => p.startsWith("ACCESS."))).toEqual([]);
    expect(
      one(
        "SELECT COUNT(*) FROM org.StaffInvitations i JOIN auth.Users u ON u.Id = i.InviteeUserId WHERE RIGHT(u.PhoneNumber, 9) = '$(l)' AND i.Status = 'Pending';",
        { l: person.phone.slice(1) },
      ),
    ).toBe("0");
  } finally {
    await owner.dispose();
  }
});

test("QA-F08-07 an invitation into an institution suspended after it was sent is not shown, cannot be accepted, and stays open", async () => {
  const t4 = tenant("T4");
  const person = await throwaway("t4-invitee");
  // T4 is suspended in the QA world. To have an invitation from before the suspension, T4 is let
  // back for the moment of inviting and suspended again (in SQL: the product has no path, OBS-02).
  const setStatus = (status: string) =>
    sql(
      "UPDATE org.Institutions SET Status = '$(st)', PermissionsVersion = PermissionsVersion + 1 WHERE Slug = '$(s)';",
      { st: status, s: t4.slug! },
    );
  setStatus("Active");
  try {
    const r = await with_(
      "O4",
      async (api) =>
        answer(
          await api.post("/api/institution/invitations", {
            data: { phone: person.phone, role: "STAFF" },
          }),
        ),
      true,
    );
    expect(r.status).toBe(201);
  } finally {
    setStatus("Suspended");
  }
  // The invitee is not shown it while T4 is suspended; the id (from the row) is still refused.
  expect(
    await with_(
      person,
      async (api) =>
        (await (await api.get("/api/me/invitations")).json()) as unknown[],
    ),
  ).toEqual([]);
  const invitation = {
    id: one(
      "SELECT LOWER(CAST(i.PublicId AS varchar(36))) FROM org.StaffInvitations i JOIN auth.Users u ON u.Id = i.InviteeUserId WHERE RIGHT(u.PhoneNumber, 9) = '$(l)';",
      { l: person.phone.slice(1) },
    ),
  };
  await with_(person, async (api) => {
    const r = await answer(
      await api.post(`/api/me/invitations/${invitation.id}/accept`),
    );
    expect([r.status, r.body?.code]).toEqual([409, "institution_not_active"]);
  });
  expect(
    one("SELECT Status FROM org.StaffInvitations WHERE PublicId = '$(id)';", {
      id: invitation.id,
    }),
  ).toBe("Pending");
  expect(
    one("SELECT COUNT(*) FROM auth.UserRoles WHERE UserId = '$(u)';", {
      u: idOf(person),
    }),
    "no role in T4, only DONOR",
  ).toBe("1");
});

test("QA-F08-09 role x action on the access routes: each T1 persona gets exactly what the templates give", async () => {
  const reads = [
    "/api/institution/roles",
    "/api/institution/role-levels",
    "/api/institution/permission-catalogue",
    "/api/institution/members",
    "/api/institution/invitations",
  ];
  const expected: Record<string, number> = {
    O1: 200,
    M1: 200,
    S1: 403,
    C1: 403,
    L1: 403,
  };
  for (const [key, status] of Object.entries(expected) as [
    PersonaKey,
    number,
  ][]) {
    await with_(key, async (api) => {
      for (const path of reads)
        expect((await api.get(path)).status(), `${key} GET ${path}`).toBe(
          status,
        );
      if (status === 403) {
        await stepUpApi(api, account(key));
        expect(
          (
            await api.post("/api/institution/roles", {
              data: {
                code: "QA_X",
                name: "x",
                level: "STAFF",
                permissions: [],
              },
            })
          ).status(),
          `${key} create role`,
        ).toBe(403);
        expect(
          (
            await api.post("/api/institution/invitations", {
              data: { phone: account("D1").phone, role: "STAFF" },
            })
          ).status(),
          `${key} invite`,
        ).toBe(403);
        expect(
          (
            await api.delete(
              `/api/institution/roles/STAFF/members/${idOf("S1")}`,
            )
          ).status(),
          `${key} revoke`,
        ).toBe(403);
      }
    });
  }
  expect(
    one(
      "SELECT COUNT(*) FROM auth.UserRoles ur JOIN auth.Roles r ON r.Id = ur.RoleId WHERE ur.UserId = '$(id)' AND ur.IsActive = 1 AND r.Code = 'STAFF';",
      { id: idOf("S1") },
    ),
  ).toBe("1");
});

test("QA-F08-10 another tenant's owner cannot reach T1's roles, people or invitations by their ids", async () => {
  const before = one(
    "SELECT COUNT(*) FROM auth.UserRoles ur JOIN org.Institutions i ON i.Id = ur.InstitutionId WHERE i.Slug = '$(s)' AND ur.IsActive = 1;",
    { s: tenant("T1").slug! },
  );
  const pending = await throwaway("t1-pending");
  await with_(
    "O1",
    async (api) =>
      expect(
        (
          await api.post("/api/institution/invitations", {
            data: { phone: pending.phone, role: "STAFF" },
          })
        ).status(),
      ).toBe(201),
    true,
  );
  const [t1Invitation] = await with_(
    pending,
    async (api) =>
      (await (await api.get("/api/me/invitations")).json()) as { id: string }[],
  );

  await with_(
    "O2",
    async (api) => {
      const members = JSON.stringify(
        await (await api.get("/api/institution/members")).json(),
      );
      for (const name of [
        "Ven. Ananda",
        "Dilani Karunaratne",
        "Kasun Rathnayake",
      ])
        expect(members).not.toContain(name);
      const roles = JSON.stringify(
        await (await api.get("/api/institution/roles")).json(),
      );
      expect(roles).not.toContain("TREASURER");
      const table: [
        string,
        () => Promise<{ status: number; body: Record<string, unknown> | null }>,
        number,
      ][] = [
        [
          "take M1's MANAGER",
          async () =>
            answer(
              await api.delete(
                `/api/institution/roles/MANAGER/members/${idOf("M1")}`,
              ),
            ),
          404,
        ],
        [
          "give M1 STAFF",
          async () =>
            answer(
              await api.post("/api/institution/roles/STAFF/members", {
                data: { user: idOf("M1") },
              }),
            ),
          404,
        ],
        [
          "edit TREASURER",
          async () =>
            answer(
              await api.put("/api/institution/roles/TREASURER/permissions", {
                data: { permissions: [], version: "AAAAAAAAAAA=" },
              }),
            ),
          404,
        ],
        [
          "cancel T1's invitation",
          async () =>
            answer(
              await api.post(
                `/api/institution/invitations/${t1Invitation.id}/cancel`,
              ),
            ),
          404,
        ],
      ];
      for (const [what, call, status] of table)
        expect((await call()).status, `O2 ${what}`).toBe(status);
    },
    true,
  );
  expect(
    one(
      "SELECT COUNT(*) FROM auth.UserRoles ur JOIN org.Institutions i ON i.Id = ur.InstitutionId WHERE i.Slug = '$(s)' AND ur.IsActive = 1;",
      { s: tenant("T1").slug! },
    ),
  ).toBe(before);
  expect(
    one("SELECT Status FROM org.StaffInvitations WHERE PublicId = '$(id)';", {
      id: t1Invitation.id,
    }),
  ).toBe("Pending");
  await with_(
    "O1",
    async (api) =>
      expect(
        (
          await api.post(
            `/api/institution/invitations/${t1Invitation.id}/cancel`,
          )
        ).status(),
      ).toBe(204),
    true,
  );
});
