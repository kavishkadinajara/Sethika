import { useTranslations } from "next-intl";
import { EmptyState } from "@/components/bookings/EmptyState";
import { Link } from "@/i18n/navigation";

/** No active institution at this address: the page's own words, with the HTTP 404 a search engine needs (plan 06, G9). */
export default function InstitutionNotFound() {
  const t = useTranslations("Discover");

  return (
    <div className="grid justify-items-center gap-4">
      <EmptyState>{t("notFound")}</EmptyState>
      <Link href="/find" className="text-sm text-brand underline">
        {t("directoryTitle")}
      </Link>
    </div>
  );
}
