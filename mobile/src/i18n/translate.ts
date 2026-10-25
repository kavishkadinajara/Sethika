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
  [K in keyof T & string]: T[K] extends string
    ? `${Prefix}${K}`
    : T[K] extends readonly string[]
      ? never
      : Leaves<T[K], `${Prefix}${K}.`>;
}[keyof T & string];

/** Every dotted path to a list of strings, e.g. "Booking.dates.months" (copied from the web as lists). */
type Lists<T, Prefix extends string = ""> = {
  [K in keyof T & string]: T[K] extends string
    ? never
    : T[K] extends readonly string[]
      ? `${Prefix}${K}`
      : Lists<T[K], `${Prefix}${K}.`>;
}[keyof T & string];

export type MessageKey = Leaves<Messages>;
export type ListKey = Lists<Messages>;

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

function find(messages: Messages, key: string): unknown {
  let node: unknown = messages;
  for (const part of key.split(".")) {
    if (typeof node !== "object" || node === null) return undefined;
    node = (node as Record<string, unknown>)[part];
  }
  return node;
}

function lookup(messages: Messages, key: string): string | undefined {
  const node = find(messages, key);
  return typeof node === "string" ? node : undefined;
}

/** A list of names (months, weekdays) in `language`, else the English one. Not ICU-formatted. */
export function listOf(language: Language, key: ListKey): readonly string[] {
  const own = find(catalogues[language], key);
  return Array.isArray(own) ? (own as string[]) : (find(en, key) as string[]);
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
