import { describe, expect, it } from "vitest";
import { outranksAccount } from "./ranks";
import type { PlatformRole } from "./types";

// The caller is a moderator: they manage support, not administrators or other moderators.
const roles: PlatformRole[] = [
  { code: "PLATFORM_ADMIN", name: "Administrator", rank: 1, canManage: false },
  { code: "PLATFORM_MODERATOR", name: "Moderator", rank: 2, canManage: false },
  { code: "PLATFORM_SUPPORT", name: "Support", rank: 3, canManage: true },
];

describe("outranksAccount (the API's RankNotJunior rule)", () => {
  it("lets anyone with the permission suspend an account that holds no platform role", () => {
    expect(outranksAccount({ platformRoles: [] }, roles)).toBe(true);
  });

  it("lets the caller suspend someone whose every platform role is junior to theirs", () => {
    expect(outranksAccount({ platformRoles: ["PLATFORM_SUPPORT"] }, roles)).toBe(true);
  });

  it("refuses an equal or a senior", () => {
    expect(outranksAccount({ platformRoles: ["PLATFORM_MODERATOR"] }, roles)).toBe(false);
    expect(outranksAccount({ platformRoles: ["PLATFORM_ADMIN"] }, roles)).toBe(false);
  });

  it("goes by the most senior role, so one junior role among them is not enough", () => {
    expect(outranksAccount({ platformRoles: ["PLATFORM_SUPPORT", "PLATFORM_MODERATOR"] }, roles)).toBe(false);
  });

  it("counts a role it does not know as senior, as before the roles have loaded", () => {
    expect(outranksAccount({ platformRoles: ["PLATFORM_SUPPORT"] }, [])).toBe(false);
    expect(outranksAccount({ platformRoles: ["PLATFORM_RETIRED"] }, roles)).toBe(false);
  });
});
