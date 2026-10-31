import type { routing } from "@/i18n/routing";
import type messages from "./messages/en.json";

// English is the reference catalogue: a key missing from it is a type error at the call site.
declare module "next-intl" {
  interface AppConfig {
    Locale: (typeof routing.locales)[number];
    Messages: typeof messages;
  }
}
