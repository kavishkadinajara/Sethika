"use client";

import dynamic from "next/dynamic";
import { useFormatter, useLocale, useTranslations } from "next-intl";
import { useSearchParams } from "next/navigation";
import { useCallback, useEffect, useId, useMemo, useState } from "react";
import { FormError } from "@/components/auth/FormError";
import { textLink } from "@/components/auth/links";
import { BowlCalendar, type DayView } from "@/components/bookings/BowlCalendar";
import { EmptyState } from "@/components/bookings/EmptyState";
import { useDates } from "@/components/bookings/useDates";
import { useMediaQuery } from "@/components/bookings/useWide";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { inputClasses } from "@/components/ui/input";
import { Spinner } from "@/components/ui/spinner";
import { Link } from "@/i18n/navigation";
import { getSearchLookups, searchMonth, searchPlaces } from "@/lib/bookings/api";
import { addMonths, colomboToday, monthOf } from "@/lib/bookings/calendar";
import {
  DEFAULT_RADIUS,
  inSriLanka,
  monthApiQuery,
  parseFindQuery,
  radii,
  roundCoordinate,
  searchApiQuery,
  timesOfDay,
  toFindSearch,
  type FindQuery,
  type Radius,
} from "@/lib/bookings/find-query";
import { inLocale, localName } from "@/lib/bookings/names";
import { localePath } from "@/lib/bookings/seo";
import type { SearchAnswer, SearchLookups, SearchMonth, SearchPlace } from "@/lib/bookings/types";
import { cn, focusRing } from "@/lib/utils";

// Leaflet reads the window as it loads, so the map is drawn in the browser only.
const FindMap = dynamic(() => import("./FindMap"), {
  ssr: false,
  loading: () => <div className="h-104 rounded-bowl border border-hairline bg-subtle lg:h-144" />,
});

type LocationProblem = "denied" | "outside" | "unavailable";

/**
 * Plan 06, B.4: find a place to give. A day, a time of day, a kind of place and where to look -
 * near me, the map's centre, a district or anywhere (G3) - all in the address (G11). A month shows
 * how many places are open each day (G6); choosing a day lists them, with their open meals, and
 * marks them on the map. Every meal links to its place's page on that day, where booking opens.
 */
export function FindScreen() {
  const t = useTranslations("Find");
  const locale = useLocale();
  const dates = useDates();
  const params = useSearchParams();
  const large = useMediaQuery("(min-width: 64rem)");

  const today = colomboToday();
  const text = params.toString();
  const query = useMemo(() => parseFindQuery(new URLSearchParams(text), today), [text, today]);

  /**
   * A filter or a day is a step the back button undoes; switching the phone's tab is not. Only the
   * browser's address changes (Next keeps `useSearchParams` in step; the page's server part reads
   * none of it), and each change starts from the address as it is now, not as this render saw it:
   * a tap that lands before React has drawn the last one must not undo it.
   */
  const set = useCallback(
    (change: Partial<FindQuery>, replace = false) => {
      const now = parseFindQuery(new URLSearchParams(window.location.search), today);
      const href = `${window.location.pathname}${toFindSearch({ ...now, ...change }, today)}`;
      if (replace) window.history.replaceState(null, "", href);
      else window.history.pushState(null, "", href);
    },
    [today],
  );

  const [lookups, setLookups] = useState<SearchLookups | null>(null);
  const [answer, setAnswer] = useState<{ key: string; value: SearchAnswer } | null>(null);
  const [failed, setFailed] = useState<{ key: string; invalid: boolean } | null>(null);
  const [months, setMonths] = useState<Record<string, SearchMonth>>({});
  const [attempt, setAttempt] = useState(0);
  const [locating, setLocating] = useState(false);
  const [location, setLocation] = useState<LocationProblem | null>(null);
  const [choosingDistrict, setChoosingDistrict] = useState(false);

  // The month on the calendar follows the chosen day, also when the back button changes it.
  const [month, setMonth] = useState(() => monthOf(query.date));
  const [followed, setFollowed] = useState(query.date);
  if (query.date !== followed) {
    setFollowed(query.date);
    setMonth(monthOf(query.date));
  }

  const searchKey = searchApiQuery(query);
  const monthKey = monthApiQuery(query, month);

  useEffect(() => {
    let cancelled = false;
    void getSearchLookups().then((result) => !cancelled && result.ok && setLookups(result.value));
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    // A later search's answer must not be overwritten by an earlier one arriving late.
    let cancelled = false;
    void searchPlaces(searchKey).then((result) => {
      if (cancelled) return;
      if (result.ok) {
        setAnswer({ key: searchKey, value: result.value });
        setFailed(null);
      } else {
        setFailed({ key: searchKey, invalid: result.failure.status === 400 });
      }
    });
    return () => {
      cancelled = true;
    };
  }, [searchKey, attempt]);

  useEffect(() => {
    if (months[monthKey]) return;
    let cancelled = false;
    void searchMonth(monthKey).then((result) => !cancelled && result.ok && setMonths((loaded) => ({ ...loaded, [monthKey]: result.value })));
    return () => {
      cancelled = true;
    };
  }, [monthKey, months, attempt]);

  const current = answer?.key === searchKey ? answer.value : null;
  const failure = failed?.key === searchKey ? failed : null;
  const counts = months[monthKey];

  const nameOf = useCallback((place: SearchPlace) => localName(locale, place), [locale]);
  const hrefOf = useCallback((place: SearchPlace) => localePath(locale, `/i/${encodeURIComponent(place.slug)}`), [locale]);
  const searchHere = useCallback(
    (latitude: number, longitude: number) =>
      set({ where: { kind: "near", latitude, longitude, radiusKm: query.where.kind === "near" ? query.where.radiusKm : DEFAULT_RADIUS } }),
    [set, query.where],
  );

  function locate() {
    if (!("geolocation" in navigator)) {
      setLocation("unavailable");
      return;
    }
    setLocating(true);
    setLocation(null);
    navigator.geolocation.getCurrentPosition(
      (position) => {
        setLocating(false);
        // Rounded before it goes anywhere, and never stored (G3).
        const latitude = roundCoordinate(position.coords.latitude);
        const longitude = roundCoordinate(position.coords.longitude);
        if (!inSriLanka(latitude, longitude)) {
          setLocation("outside");
          return;
        }
        setChoosingDistrict(false);
        set({ where: { kind: "near", latitude, longitude, radiusKm: query.where.kind === "near" ? query.where.radiusKm : DEFAULT_RADIUS } });
      },
      (error) => {
        setLocating(false);
        setLocation(error.code === error.PERMISSION_DENIED ? "denied" : "unavailable");
      },
      { enableHighAccuracy: false, timeout: 10_000, maximumAge: 300_000 },
    );
  }

  function dayView(date: string): DayView {
    const long = dates.full(date);
    const found = counts?.days.find((d) => d.date === date);
    if (!found) return { label: long, disabled: true };
    const open = found.institutions;
    return {
      content: open > 0 ? <span className="text-sm font-semibold tabular-nums text-brand">{open}</span> : null,
      label: `${long}. ${t("calendar.open", { count: open })}`,
      disabled: date < today || open === 0,
    };
  }

  const where = query.where;
  const mode = choosingDistrict || where.kind === "district" ? "district" : where.kind;
  const day = dates.full(query.date);

  return (
    <div className="grid gap-8">
      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)] lg:items-start">
        <Card accent className="grid gap-6 p-4 sm:p-6">
          <Chips
            label={t("where.label")}
            value={mode}
            options={[
              { value: "near", label: locating ? t("where.locating") : t("where.near") },
              { value: "district", label: t("where.district") },
              { value: "anywhere", label: t("where.anywhere") },
            ]}
            onChange={(value) => {
              setLocation(null);
              if (value === "near") locate();
              else if (value === "district") setChoosingDistrict(true);
              else {
                setChoosingDistrict(false);
                set({ where: { kind: "anywhere" } });
              }
            }}
          />
          {location && (
            <p role="alert" className="text-sm text-danger">
              {t(`where.${location}`)}
            </p>
          )}

          {mode === "district" && (
            <Select
              label={t("where.districtLabel")}
              value={where.kind === "district" ? where.district : ""}
              placeholder={t("where.chooseDistrict")}
              options={(lookups?.districts ?? []).map((d) => ({ value: d.code, label: inLocale(locale, d.name, d.nameSi, d.nameTa) }))}
              onChange={(district) => district && set({ where: { kind: "district", district } })}
            />
          )}

          {where.kind === "near" && (
            <Chips
              label={t("where.radius")}
              value={String(where.radiusKm)}
              options={radii.map((km) => ({ value: String(km), label: t("where.km", { km }) }))}
              onChange={(km) => set({ where: { ...where, radiusKm: Number(km) as Radius } })}
            />
          )}

          <Chips
            label={t("time.label")}
            value={query.time}
            options={timesOfDay.map((time) => ({ value: time, label: t(`time.${time}`) }))}
            onChange={(time) => set({ time })}
          />

          <Select
            label={t("type.label")}
            value={query.type ?? ""}
            placeholder={t("type.all")}
            placeholderSelectable
            options={(lookups?.types ?? []).map((type) => ({ value: type.code, label: localName(locale, type) }))}
            onChange={(type) => set({ type: type || null })}
          />
        </Card>

        <Card className="p-4 sm:p-6">
          <h2 className="mb-1 text-xl font-semibold text-brand">{t("calendar.title")}</h2>
          <p className="mb-4 text-sm text-muted">{t("calendar.hint")}</p>
          <BowlCalendar
            month={month}
            onMonthChange={setMonth}
            canGoBack={month > monthOf(today)}
            canGoForward={month < addMonths(monthOf(today), 12)}
            selected={query.date}
            onSelect={(date) => set({ date })}
            loading={!counts}
            day={dayView}
          />
        </Card>
      </div>

      <section aria-labelledby="results" className="grid gap-4">
        <div className="grid gap-1">
          <h2 id="results" className="text-2xl font-semibold text-brand">
            {t("results.title", { date: day })}
          </h2>
          {current && (
            <p role="status" className="text-sm text-muted">
              {t("results.count", { count: current.results.length })}
              {current.more && ` ${where.kind === "near" ? t("results.moreNearest") : t("results.moreByName")}`}
            </p>
          )}
        </div>

        <div role="tablist" aria-label={t("tabs.label")} className="grid grid-cols-2 gap-2 lg:hidden">
          {(["list", "map"] as const).map((view) => (
            <button
              key={view}
              type="button"
              role="tab"
              aria-selected={query.view === view}
              onClick={() => set({ view }, true)}
              className={cn(
                "min-h-11 rounded-full border px-4 text-base font-medium transition-colors",
                query.view === view ? "border-primary bg-primary text-on-primary" : "border-hairline bg-surface text-fg hover:border-line",
                focusRing,
              )}
            >
              {t(`tabs.${view}`)}
            </button>
          ))}
        </div>

        {failure ? (
          <FormError>
            {failure.invalid ? t("results.invalid") : t("results.failed")}{" "}
            {failure.invalid ? (
              <Link href="/find" className={cn(textLink, focusRing, "min-h-0")}>
                {t("results.startAgain")}
              </Link>
            ) : (
              <button type="button" onClick={() => setAttempt((n) => n + 1)} className={cn(textLink, focusRing, "min-h-0")}>
                {t("results.retry")}
              </button>
            )}
          </FormError>
        ) : (
          <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)] lg:items-start">
            <div className={cn(query.view === "map" && "hidden lg:block")}>
              {!current ? (
                <div className="flex justify-center py-16 text-brand">
                  <Spinner />
                </div>
              ) : current.results.length === 0 ? (
                <EmptyState>{t("results.none", { date: day })}</EmptyState>
              ) : (
                <ol className="grid gap-3">
                  {current.results.map((place, index) => (
                    <li key={place.slug}>
                      <PlaceCard place={place} number={index + 1} date={query.date} />
                    </li>
                  ))}
                </ol>
              )}
            </div>

            {(large || query.view === "map") && (
              <div className="lg:sticky lg:top-4">
                <FindMap places={current?.results ?? []} where={where} nameOf={nameOf} hrefOf={hrefOf} onSearchHere={searchHere} />
              </div>
            )}
          </div>
        )}
      </section>
    </div>
  );
}

/**
 * A place found, numbered as its marker on the map, with each meal open that day. Its name opens its
 * page; "Book" opens it on that meal and day, with the booking form already open.
 */
function PlaceCard({ place, number, date }: { place: SearchPlace; number: number; date: string }) {
  const t = useTranslations("Find");
  const locale = useLocale();
  const format = useFormatter();
  const name = localName(locale, place);
  const facts = [
    inLocale(locale, place.typeName, place.typeNameSi, place.typeNameTa),
    `${place.city}, ${inLocale(locale, place.district, place.districtSi, place.districtTa)}`,
    place.distanceKm !== null && t("results.distance", { km: format.number(place.distanceKm, { maximumFractionDigits: 1 }) }),
  ].filter(Boolean);

  return (
    <article className="grid gap-3 rounded-bowl border border-hairline bg-surface p-4 shadow-lift">
      <div className="flex items-start gap-3">
        <span aria-hidden className="grid size-8 shrink-0 place-items-center rounded-full bg-primary text-sm font-semibold text-on-primary">
          {number}
        </span>
        <div className="grid min-w-0 gap-1">
          <h3 className="text-lg font-semibold leading-snug text-brand">
            <Link href={`/i/${encodeURIComponent(place.slug)}`} className={cn("hover:underline", focusRing)}>
              {name}
            </Link>
          </h3>
          <p className="text-sm text-muted">{facts.join(" · ")}</p>
        </div>
      </div>
      <ul className="grid gap-2">
        {place.slots.map((slot) => {
          const meal = localName(locale, slot);
          return (
            <li key={slot.mealSlot} className="flex flex-wrap items-center justify-between gap-x-3 gap-y-2 rounded-cup bg-subtle px-3 py-2">
              <span className="min-w-0">
                <span className="font-medium text-fg">{meal}</span>{" "}
                <span className="tabular-nums text-muted">
                  {slot.startTime}–{slot.endTime}
                </span>
                <span className="block text-sm text-muted">{t("results.left", { count: slot.sponsors })}</span>
              </span>
              <Button asChild variant="outline">
                <Link
                  href={`/i/${encodeURIComponent(place.slug)}?slot=${slot.mealSlot}&date=${date}`}
                  aria-label={t("results.bookLabel", { meal, place: name })}
                >
                  {t("results.book")}
                </Link>
              </Button>
            </li>
          );
        })}
      </ul>
    </article>
  );
}

/** One choice as a row of chips; the chosen one is pressed. */
function Chips<V extends string>({
  label,
  options,
  value,
  onChange,
}: {
  label: string;
  options: { value: V; label: string }[];
  value: V;
  onChange: (value: V) => void;
}) {
  const id = useId();
  return (
    <div role="group" aria-labelledby={id} className="grid gap-2">
      <span id={id} className="text-sm font-medium text-muted">
        {label}
      </span>
      <div className="flex flex-wrap gap-2">
        {options.map((option) => {
          const on = option.value === value;
          return (
            <button
              key={option.value}
              type="button"
              aria-pressed={on}
              onClick={() => onChange(option.value)}
              className={cn(
                "min-h-11 rounded-full border px-4 text-base font-medium transition-colors",
                on ? "border-primary bg-primary text-on-primary" : "border-hairline bg-surface text-fg hover:border-line",
                focusRing,
              )}
            >
              {option.label}
            </button>
          );
        })}
      </div>
    </div>
  );
}

/** A labelled native select. The placeholder is a real choice ("every kind") or only a prompt. */
function Select({
  label,
  value,
  placeholder,
  placeholderSelectable = false,
  options,
  onChange,
}: {
  label: string;
  value: string;
  placeholder: string;
  placeholderSelectable?: boolean;
  options: { value: string; label: string }[];
  onChange: (value: string) => void;
}) {
  const id = useId();
  return (
    <div className="grid min-w-0 gap-2">
      <label htmlFor={id} className="text-sm font-medium text-muted">
        {label}
      </label>
      <select id={id} value={value} onChange={(event) => onChange(event.target.value)} className={cn(inputClasses, "min-w-0 pr-8")}>
        <option value="" disabled={!placeholderSelectable}>
          {placeholder}
        </option>
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    </div>
  );
}
