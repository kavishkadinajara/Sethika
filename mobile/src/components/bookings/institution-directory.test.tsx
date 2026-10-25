import { router } from "expo-router";

import { createApiStub } from "@/test-utils/api-stub";
import { institution } from "@/test-utils/fixtures";
import { json, press, render, type } from "@/test-utils/render";

import { InstitutionDirectory } from "./institution-directory";

/*  Explore's list against GET /api/institutions (anonymous), stubbed at the app's boundary. */

const mockApi = createApiStub();

jest.mock("@/api/http", () => ({ getJson: (path: string) => mockApi.getJson(path) }));
jest.mock("@/auth/session-provider", () => ({ useSession: () => ({ status: "anonymous" }), useSessionClient: () => mockApi.client }));
jest.mock("expo-router", () => ({ router: { push: jest.fn(), navigate: jest.fn(), dismissTo: jest.fn() } }));

const summary = (slug: string, name: string, district: string, districtSi: string | null = null) => {
  const { slots: _slots, addressLine1: _a, addressLine2: _b, description: _c, dietaryNotes: _d, latitude: _e, longitude: _f, ...rest } = institution({
    slug,
    name,
    district,
    districtSi,
  });
  return rest;
};

beforeEach(() => {
  mockApi.sent.length = 0;
  jest.mocked(router.push).mockClear();
});

it("lists each institution with its type and district, and opens one by its slug", async () => {
  mockApi.serve({
    "GET /api/institutions": () =>
      json(200, [summary("galle-childrens-home", "Galle Children's Home", "Galle"), summary("test-elders-home", "Test Elders' Home", "Kandy", "මහනුවර")]),
  });
  const screen = await render(<InstitutionDirectory />);

  screen.getText("Galle Children's Home");
  screen.getText("Peradeniya · Kandy");
  expect(mockApi.sent).toEqual([{ method: "GET", path: "/api/institutions", body: null, signedIn: false }]);

  await press(screen.getText("Test Elders' Home"));
  expect(router.push).toHaveBeenCalledWith({ pathname: "/i/[slug]", params: { slug: "test-elders-home" } });
});

it("filters by any name it shows, in any language", async () => {
  mockApi.serve({
    "GET /api/institutions": () =>
      json(200, [summary("galle-childrens-home", "Galle Children's Home", "Galle"), summary("test-elders-home", "Test Elders' Home", "Kandy", "මහනුවර")]),
  });
  const screen = await render(<InstitutionDirectory />);

  await type(screen.getLabel("Search"), "මහනුවර");
  expect(screen.queryText("Galle Children's Home")).toBeNull();
  screen.getText("Test Elders' Home");

  await type(screen.getLabel("Search"), "colombo");
  screen.getText("Nothing matches that search.");
});

it("says so when nothing is listed yet", async () => {
  mockApi.serve({ "GET /api/institutions": () => json(200, []) });
  const screen = await render(<InstitutionDirectory />);
  screen.getText("No places are listed yet. Soon, they will be.");
});

it("offers a retry when the list cannot load", async () => {
  mockApi.serve({ "GET /api/institutions": () => json(503, {}) });
  const screen = await render(<InstitutionDirectory />);
  screen.getText("Could not load this.");

  mockApi.serve({ "GET /api/institutions": () => json(200, [summary("test-elders-home", "Test Elders' Home", "Kandy")]) });
  await press(screen.getText("Try again"));
  screen.getText("Test Elders' Home");
});
