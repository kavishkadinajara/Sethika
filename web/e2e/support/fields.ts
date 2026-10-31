import type { Locator, Page } from "@playwright/test";

const escape = (text: string) => text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");

/** The control labelled exactly <label>, whether or not it is marked "(required)". */
export function field(scope: Page | Locator, label: string): Locator {
  return scope.getByLabel(new RegExp(`^${escape(label)}( \\(required\\))?$`));
}
