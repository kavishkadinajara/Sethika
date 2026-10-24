import { createTranslator, hasMessage, resolveLanguage } from "./translate";
import en from "./messages/en.json";
import si from "./messages/si.json";
import ta from "./messages/ta.json";

function keys(node: object, prefix = ""): string[] {
  return Object.entries(node).flatMap(([key, value]) =>
    typeof value === "string" ? [`${prefix}${key}`] : keys(value as object, `${prefix}${key}.`),
  );
}

describe("resolveLanguage", () => {
  it("takes the first of the phone's languages that Sethika speaks", () => {
    expect(resolveLanguage(["fr-FR", "ta-LK", "si-LK"])).toBe("ta");
    expect(resolveLanguage(["si_LK"])).toBe("si");
  });

  it("falls back to English", () => {
    expect(resolveLanguage(["fr", null, undefined])).toBe("en");
    expect(resolveLanguage([])).toBe("en");
  });
});

describe("createTranslator", () => {
  it("formats ICU arguments and plurals like the web", () => {
    const t = createTranslator("en");
    expect(t("Errors.otp_incorrect", { count: 1 })).toBe("That code is not right. 1 try left.");
    expect(t("Errors.otp_incorrect", { count: 3 })).toBe("That code is not right. 3 tries left.");
    expect(t("App.profile.greeting", { name: "Nimal" })).toBe("Hello, Nimal");
  });

  it("speaks Sinhala and Tamil", () => {
    expect(createTranslator("si")("Login.title")).toBe(si.Login.title);
    expect(createTranslator("ta")("Login.title")).toBe(ta.Login.title);
  });

  it("knows which API error codes have a message", () => {
    expect(hasMessage("Errors.invalid_credentials")).toBe(true);
    expect(hasMessage("Errors.no_such_code")).toBe(false);
  });
});

describe("catalogues", () => {
  it("have the same keys in every language", () => {
    expect(keys(si).sort()).toEqual(keys(en).sort());
    expect(keys(ta).sort()).toEqual(keys(en).sort());
  });
});
