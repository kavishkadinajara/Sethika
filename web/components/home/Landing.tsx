import { useTranslations } from "next-intl";
import type { ComponentType, SVGProps } from "react";
import { Bowl } from "@/components/bookings/Bowl";
import { Button } from "@/components/ui/button";
import { CalendarIcon, GlobeIcon, GivingIcon, SearchIcon, ShieldIcon, BowlIcon } from "@/components/ui/icons";
import { Link } from "@/i18n/navigation";
import { cn } from "@/lib/utils";

/*  A fortnight as the calendar draws it (the bowl gauge, DESIGN.md): a picture of the idea, not
    data, so the same on every load. 1 is a day fully sponsored, 0 a day still waiting.        */
const fortnight = [1, 1, 0.5, 0, 1, 0.75, 0, 0.25, 1, 0, 0.5, 1, 1, 0];

const places = ["temples", "kovils", "mosques", "churches", "elders", "children"] as const;

const steps: { key: "find" | "book" | "remind"; icon: ComponentType<SVGProps<SVGSVGElement>> }[] = [
  { key: "find", icon: SearchIcon },
  { key: "book", icon: CalendarIcon },
  { key: "remind", icon: BowlIcon },
];

const promises: { key: "verified" | "languages" | "neutral"; icon: ComponentType<SVGProps<SVGSVGElement>> }[] = [
  { key: "verified", icon: ShieldIcon },
  { key: "languages", icon: GlobeIcon },
  { key: "neutral", icon: GivingIcon },
];

/**
 * The public home page (plan 04): what Sethika is, in the visitor's language, and the two ways in
 * (find a place, create an account), plus the way in for an institution. Server-rendered and
 * prerendered, with no client JavaScript of its own; faith-neutral, so no religion's symbols.
 */
export function Landing() {
  const t = useTranslations("Dashboard.landing");

  return (
    <div className="grid gap-16 pb-8 sm:gap-24">
      <section className="grid items-center gap-10 pt-2 lg:grid-cols-[1.1fr_0.9fr] lg:gap-14 lg:pt-10">
        <div className="grid animate-rise gap-6 motion-reduce:animate-none">
          <p className="font-tagline text-xl text-brand italic">{t("eyebrow")}</p>
          <h1 className="text-4xl leading-tight font-semibold tracking-tight text-fg sm:text-5xl lg:text-6xl">{t("title")}</h1>
          <p className="max-w-xl text-lg text-muted">{t("lead")}</p>
          <div className="flex flex-wrap gap-3">
            <Button asChild size="lg">
              <Link href="/institutions">
                <SearchIcon className="size-5" />
                {t("findCta")}
              </Link>
            </Button>
            <Button asChild size="lg" variant="outline">
              <Link href="/signup">{t("joinCta")}</Link>
            </Button>
          </div>
          <ul className="flex flex-wrap gap-2">
            {places.map((place) => (
              <li key={place} className="rounded-full border border-hairline bg-surface px-3.5 py-1.5 text-sm text-muted">
                {t(`places.${place}`)}
              </li>
            ))}
          </ul>
        </div>

        <figure
          className={cn(
            "relative animate-rise overflow-hidden rounded-bowl p-6 text-ivory shadow-lift [animation-delay:80ms] motion-reduce:animate-none sm:p-8",
            // A brand panel (DESIGN.md): Estate Green in both themes, a tonal gradient of its own green.
            "bg-[linear-gradient(150deg,#1f7356_0%,var(--color-estate)_45%,#0f3d2e_100%)]",
            "before:absolute before:-top-px before:left-6 before:h-[3px] before:w-14 before:rounded-b-full before:bg-accent sm:before:left-8",
          )}
        >
          {/* The mark, large and faint, as the panel's ground. */}
          <svg aria-hidden viewBox="0 0 28 19" className="pointer-events-none absolute -right-10 -bottom-12 w-72 text-white/6" fill="currentColor">
            <path d="M2 5h24a12 11 0 0 1-24 0z" />
          </svg>
          <figcaption className="relative grid gap-1">
            <span className="text-xl font-semibold">{t("preview.title")}</span>
            <span className="text-base text-ivory/80">{t("preview.legend")}</span>
          </figcaption>
          <ol aria-hidden className="relative mt-6 grid grid-cols-7 gap-x-2 gap-y-4 sm:gap-x-3">
            {fortnight.map((fill, index) => (
              <li key={index} className="grid justify-items-center gap-1.5 rounded-2xl bg-white/6 py-2.5">
                <span className="text-sm text-ivory/75 tabular-nums">{index + 1}</span>
                <Bowl fill={fill} className={cn("h-5 w-8", fill >= 1 ? "text-gold" : "text-ivory")} />
              </li>
            ))}
          </ol>
          <div className="relative mt-6 flex flex-wrap gap-x-5 gap-y-2 text-sm text-ivory/85">
            <span className="inline-flex items-center gap-2">
              <Bowl fill={0} className="h-4 w-6 text-ivory" />
              {t("preview.open")}
            </span>
            <span className="inline-flex items-center gap-2">
              <Bowl fill={1} className="h-4 w-6 text-gold" />
              {t("preview.full")}
            </span>
          </div>
        </figure>
      </section>

      <section aria-labelledby="how" className="grid gap-8">
        <h2 id="how" className="text-3xl font-semibold tracking-tight text-brand">
          {t("how")}
        </h2>
        <ol className="grid gap-4 md:grid-cols-3">
          {steps.map(({ key, icon: Icon }, index) => (
            <li key={key} className="relative grid content-start gap-3 rounded-bowl border border-hairline bg-surface p-6 shadow-lift">
              <div className="flex items-center justify-between">
                <span className="grid size-12 place-items-center rounded-2xl bg-primary/10 text-brand">
                  <Icon className="size-6" />
                </span>
                <span aria-hidden className="font-tagline text-4xl text-hairline italic">
                  {index + 1}
                </span>
              </div>
              <h3 className="text-xl font-semibold text-fg">{t(`steps.${key}.title`)}</h3>
              <p className="text-base text-muted">{t(`steps.${key}.description`)}</p>
            </li>
          ))}
        </ol>
      </section>

      <section aria-labelledby="promise" className="grid gap-8">
        <h2 id="promise" className="text-3xl font-semibold tracking-tight text-brand">
          {t("promise.title")}
        </h2>
        <ul className="grid gap-x-8 gap-y-6 md:grid-cols-3">
          {promises.map(({ key, icon: Icon }) => (
            <li key={key} className="flex gap-4">
              <span className="grid size-11 shrink-0 place-items-center rounded-full border border-hairline bg-surface text-brand">
                <Icon className="size-5" />
              </span>
              <div className="grid gap-1">
                <h3 className="text-lg font-semibold text-fg">{t(`promise.${key}.title`)}</h3>
                <p className="text-base text-muted">{t(`promise.${key}.description`)}</p>
              </div>
            </li>
          ))}
        </ul>
      </section>

      <section className="grid gap-6 rounded-bowl border border-hairline bg-subtle p-6 sm:p-10 lg:grid-cols-[1fr_auto] lg:items-center">
        <div className="grid gap-2">
          <h2 className="text-2xl font-semibold text-fg sm:text-3xl">{t("forInstitutions")}</h2>
          <p className="max-w-2xl text-lg text-muted">{t("forInstitutionsLead")}</p>
        </div>
        <Button asChild size="lg" className="justify-self-start">
          <Link href="/apply">{t("registerCta")}</Link>
        </Button>
      </section>
    </div>
  );
}
