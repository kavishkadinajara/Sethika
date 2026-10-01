import { useTranslations } from "next-intl";

/** Placeholder until the sign-in form lands with slice 5.7; it already exercises A10/A11. */
// No `italic` class on the tagline: the Fraunces face is already italic, and the class would
// make the browser slant Noto Sinhala/Tamil artificially.
export default function LoginPage() {
  const t = useTranslations();

  return (
    <main className="mx-auto flex w-full max-w-md flex-1 flex-col justify-center px-4 py-12">
      <p className="text-3xl font-semibold text-estate">{t("Brand.name")}</p>
      <p className="mt-2 font-tagline text-lg text-ink/80">{t("Brand.tagline")}</p>

      <h1 className="mt-10 text-xl font-medium text-estate">{t("Login.title")}</h1>
      <p className="mt-2 text-sm text-ink/70">{t("Login.comingSoon")}</p>
    </main>
  );
}
