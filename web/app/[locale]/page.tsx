import { useTranslations } from "next-intl";
import { Can } from "@/components/permissions/Can";
import { PermissionDebugPanel } from "@/components/permissions/PermissionDebugPanel";


export default function Home() {
  const t = useTranslations();

  return (
    <main className="mx-auto w-full max-w-3xl px-4 py-12">
      <header className="mb-10">
        <h1 className="text-3xl font-semibold text-estate">{t("Brand.name")}</h1>
        <p className="mt-2 text-sm text-ink/70">{t("Home.intro")}</p>
      </header>

      <section className="space-y-4">
        <h2 className="text-lg font-medium text-estate">{t("Home.calendarHeading")}</h2>

        <Can
          permission="BOOKINGS.CALENDAR.VIEW"
          fallback={
            <p className="rounded border border-ink/10 bg-white p-4 text-sm text-ink/60">
              {t("Home.calendarDenied")}
            </p>
          }
        >
          <div className="rounded border border-ink/10 bg-white p-4">
            <p className="text-sm">{t("Home.calendarPlaceholder")}</p>

            <Can permission="BOOKINGS.CALENDAR.APPROVE">
              <button
                type="button"
                className="mt-3 rounded bg-gold px-3 py-1.5 text-sm font-medium text-ink"
              >
                {t("Home.approveBooking")}
              </button>
            </Can>
          </div>
        </Can>
      </section>

      <PermissionDebugPanel />
    </main>
  );
}
