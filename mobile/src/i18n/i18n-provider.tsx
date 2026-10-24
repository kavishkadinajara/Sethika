import { getLocales } from "expo-localization";
import * as SecureStore from "expo-secure-store";
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";

import { createTranslator, isLanguage, resolveLanguage, type Language, type Translate } from "./translate";

/*  The language a screen is shown in: the one the user picked on this phone, else the phone's
    own (first Sethika language in its list), else English. After sign-in the account's
    PreferredLanguage wins, as on the web (SignedInLanguage in app/(app)/_layout.tsx).
    Not a secret, but SecureStore is already here and the value is tiny.                    */

const KEY = "sethika.language";

type I18n = {
  language: Language;
  t: Translate;
  setLanguage: (language: Language) => void;
};

const deviceLanguage = (): Language => resolveLanguage(getLocales().map((locale) => locale.languageCode));

const I18nContext = createContext<I18n>({
  language: "en",
  t: createTranslator("en"),
  setLanguage: () => {},
});

export function I18nProvider({ children }: { children: ReactNode }) {
  const [language, setLanguageState] = useState<Language>(deviceLanguage);

  useEffect(() => {
    SecureStore.getItemAsync(KEY)
      .then((saved) => {
        if (isLanguage(saved)) setLanguageState(saved);
      })
      .catch(() => {});
  }, []);

  const setLanguage = useCallback((next: Language) => {
    setLanguageState(next);
    SecureStore.setItemAsync(KEY, next).catch(() => {});
  }, []);

  const value = useMemo(() => ({ language, t: createTranslator(language), setLanguage }), [language, setLanguage]);

  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>;
}

export function useI18n(): I18n {
  return useContext(I18nContext);
}
