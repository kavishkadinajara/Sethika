import { IntlMessageFormat } from "intl-messageformat";

import en from "./messages/en.json";
import si from "./messages/si.json";
import ta from "./messages/ta.json";

/*  Framework-free half of i18n, so it can be unit-tested. The messages are ICU, like the web's
    (next-intl formats with the same intl-messageformat), and the shared keys are copied from
    web/messages so a donor reads the same words on both.                                      */

export const LANGUAGES = ["en", "si", "ta"] as const;
export type Language = (typeof LANGUAGES)[number];

export type Messages = typeof en;

const catalogues: Record<Language, Messages> = { en, si, ta };

/** Every dotted path to a string in the English catalogue, e.g. "Login.title". */
type Leaves<T, Prefix extends string = ""> = {
  [K in keyof T & string]: T[K] extends string ? `${Prefix}${K}` : Leaves<T[K], `${Prefix}${K}.`>;
}[keyof T & string];

export type MessageKey = Leaves<Messages>;

export type Translate = (key: MessageKey, values?: Record<string, string | number>) => string;

export function isLanguage(value: unknown): value is Language {
  return typeof value === "string" && (LANGUAGES as readonly string[]).includes(value);
}

/** The first of the phone's languages Sethika speaks, else English. */
export function resolveLanguage(preferred: readonly (string | null | undefined)[]): Language {
  for (const code of preferred) {
    const base = code?.toLowerCase().split(/[-_]/)[0];
    if (isLanguage(base)) return base;
  }
  return "en";
}

function lookup(messages: Messages, key: string): string | undefined {
  let node: unknown = messages;
  for (const part of key.split(".")) {
    if (typeof node !== "object" || node === null) return undefined;
    node = (node as Record<string, unknown>)[part];
  }
  return typeof node === "string" ? node : undefined;
}

export function createTranslator(language: Language): Translate {
  const cache = new Map<string, IntlMessageFormat>();
  return (key, values) => {
    // A key missing from si/ta falls back to English rather than showing a raw key.
    const message = lookup(catalogues[language], key) ?? lookup(en, key);
    if (message === undefined) return key;
    let format = cache.get(key);
    if (!format) {
      format = new IntlMessageFormat(message, language);
      cache.set(key, format);
    }
    return String(format.format(values));
  };
}

/** Whether `key` names a message, for codes that come from the API (`Errors.<code>`). */
export function hasMessage(key: string): key is MessageKey {
  return lookup(en, key) !== undefined;
}
