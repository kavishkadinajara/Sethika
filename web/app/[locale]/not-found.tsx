import { useTranslations } from "next-intl";
import { Link } from "@/i18n/navigation";

export default function NotFound() {
  const t = useTranslations("NotFound");

  return (
    <main className="mx-auto w-full max-w-md flex-1 px-4 py-24">
      <h1 className="text-2xl font-semibold text-estate">{t("title")}</h1>
      <Link href="/" className="mt-4 inline-block text-sm text-estate underline">
        {t("home")}
      </Link>
    </main>
  );
}
