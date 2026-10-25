import { formatLkr, inLocale } from "./names";

describe("inLocale", () => {
  it("falls back to English when the language has no name", () => {
    expect(inLocale("si", "Lunch", "දිවා ආහාරය", null)).toBe("දිවා ආහාරය");
    expect(inLocale("ta", "Lunch", "දිවා ආහාරය", null)).toBe("Lunch");
    expect(inLocale("ta", "Lunch", null, "  ")).toBe("Lunch");
    expect(inLocale("en", "Lunch", "දිවා ආහාරය", "மதிய உணவு")).toBe("Lunch");
  });
});

describe("formatLkr", () => {
  it("groups thousands and shows cents only when there are some", () => {
    expect(formatLkr(450)).toBe("LKR 450");
    expect(formatLkr(11250)).toBe("LKR 11,250");
    expect(formatLkr(1250.5)).toBe("LKR 1,250.50");
    expect(formatLkr(1234567.891)).toBe("LKR 1,234,567.89");
  });
});
