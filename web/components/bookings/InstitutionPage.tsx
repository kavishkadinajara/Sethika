"use client";

import { useLocale, useTranslations } from "next-intl";
import { useSearchParams } from "next/navigation";
import { useCallback, useEffect, useRef, useState } from "react";
import { FormError } from "@/components/auth/FormError";
import { ServicesOffered } from "@/components/services/ServicesOffered";
import { textLink } from "@/components/auth/links";
import { Card } from "@/components/ui/card";
import { Spinner } from "@/components/ui/spinner";
import { getAvailability, getInstitution, type BookingFailure } from "@/lib/bookings/api";
import { addDays, addMonths, colomboToday, maskDays, monthOf, weekdayDate } from "@/lib/bookings/calendar";
import { inLocale, localName } from "@/lib/bookings/names";
import type { Availability, DayAvailability, PublicInstitution, PublicMealSlot } from "@/lib/bookings/types";
import { cn, focusRing } from "@/lib/utils";
import { Bowl } from "./Bowl";
import { BookingSheet } from "./BookingSheet";
import { BowlCalendar, type DayView } from "./BowlCalendar";
import { EmptyState } from "./EmptyState";
import { useBookingError } from "./useBookingError";
import { useDates } from "./useDates";

/**
 * An institution's own page (plan 03, C10): who they are, the meals they offer, and a month of
 * each meal's days as bowls - empty, filling, full. Choosing an open day opens the booking form.
 * Anyone can look; booking asks for a sign-in, and comes back here to the same meal and day.
 *
 * `initial` is the institution as the server read it (plan 06, G9): rendered at once, in the
 * server's HTML too, and not fetched again. Without it the page loads the institution itself.
 */
export function InstitutionPage({ slug, initial = null }: { slug: string; initial?: PublicInstitution | null }) {
  const t = useTranslations("Discover");
  const locale = useLocale();
  const errorMessage = useBookingError();
  const dates = useDates();
  const search = useSearchParams();

  const [institution, setInstitution] = useState<PublicInstitution | null>(initial);
  const [failure, setFailure] = useState<BookingFailure | null>(null);
  const [slotId, setSlotId] = useState<string | null>(() => (initial ? slotFor(initial, search.get("slot")) : search.get("slot")));
  const [month, setMonth] = useState(() => monthOf(validDate(search.get("date")) ?? colomboToday()));
  const [months, setMonths] = useState<Record<string, Availability>>({});
  const [monthFailed, setMonthFailed] = useState(false);
  const [date, setDate] = useState<string | null>(validDate(search.get("date")));
  // The day as it was when chosen: the sheet keeps it while the month refreshes under it, so a
  // booking's confirmation is not lost when its own booking fills the day.
  const [chosen, setChosen] = useState<DayAvailability | null>(null);
  // Opening on a month with no open day for the meal moves on to the next one, once.
  const movedOn = useRef(search.get("date") !== null);

  const load = useCallback(async () => {
    setFailure(null);
    const result = await getInstitution(slug);
    if (!result.ok) {
      setFailure(result.failure);
      return;
    }
    setInstitution(result.value);
    setSlotId((current) => slotFor(result.value, current));
  }, [slug]);

  const loadMonth = useCallback(
    async (wanted: string) => {
      setMonthFailed(false);
      const result = await getAvailability(slug, wanted);
      if (result.ok) setMonths((loaded) => ({ ...loaded, [wanted]: result.value }));
      else setMonthFailed(true);
    },
    [slug],
  );

  useEffect(() => {
    // Loading the page's data is what this component is for, unless the server already did.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    if (!initial) void load();
  }, [load, initial]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    if (institution && !months[month]) void loadMonth(month);
  }, [institution, month, months, loadMonth]);

  const slot = institution?.slots.find((s) => s.publicId === slotId) ?? null;
  const days = months[month]?.slots.find((s) => s.mealSlot === slotId)?.days ?? null;

  // A day named in the address (a donor back from signing in) opens its sheet once it is loaded.
  useEffect(() => {
    if (!date || chosen || !days) return;
    const found = days.find((d) => d.date === date);
    // eslint-disable-next-line react-hooks/set-state-in-effect
    if (found?.bookable) setChosen(found);
  }, [date, chosen, days]);

  useEffect(() => {
    if (movedOn.current || !slot || !days) return;
    movedOn.current = true;
    // eslint-disable-next-line react-hooks/set-state-in-effect
    if (!days.some((d) => d.bookable)) setMonth(addMonths(month, 1));
  }, [slot, days, month]);

  if (failure) {
    return failure.status === 404 ? (
      <EmptyState>{t("notFound")}</EmptyState>
    ) : (
      <FormError>
        {errorMessage(failure)}{" "}
        <button type="button" onClick={() => void load()} className={cn(textLink, focusRing, "min-h-0")}>
          {t("retry")}
        </button>
      </FormError>
    );
  }

  if (!institution) {
    return (
      <div className="flex justify-center py-16 text-brand">
        <Spinner />
      </div>
    );
  }

  const today = colomboToday();
  const lastMonth = monthOf(addDays(today, slot?.maxAdvanceDays ?? 365));
  const choose = (day: string | null) => {
    setDate(day);
    setChosen(null);
  };

  /** Fetches the month again: someone may have taken a day while the donor looked at it. */
  const refresh = () => void loadMonth(month);

  return (
    <div className="grid gap-8">
      <Profile institution={institution} />

      {institution.slots.length === 0 ? (
        <EmptyState>{t("noSlots")}</EmptyState>
      ) : (
        <>
          <SlotChooser
            slots={institution.slots}
            selected={slotId}
            onSelect={(id) => {
              setSlotId(id);
              choose(null);
            }}
          />

          {slot && (
            <Card accent className="p-4 sm:p-6">
              <h2 className="mb-1 text-xl font-semibold text-brand">{t("chooseDay")}</h2>
              <p className="mb-4 text-sm text-muted">{t("chooseDayHint", { meal: localName(locale, slot) })}</p>

              <BowlCalendar
                month={month}
                onMonthChange={(next) => {
                  setMonth(next);
                  choose(null);
                }}
                canGoBack={month > monthOf(today)}
                canGoForward={month < lastMonth}
                selected={date}
                onSelect={choose}
                loading={!days && !monthFailed}
                day={(day) => dayView(day, slot, days)}
              >
                {monthFailed && (
                  <FormError>
                    {t("monthFailed")}{" "}
                    <button type="button" onClick={refresh} className={cn(textLink, focusRing, "min-h-0")}>
                      {t("retry")}
                    </button>
                  </FormError>
                )}
                <Legend shared={slot.bookingMode === "Shared"} />
              </BowlCalendar>
            </Card>
          )}

          {slot && chosen && (
            <BookingSheet
              institution={institution}
              slot={slot}
              day={chosen}
              onClose={() => choose(null)}
              onBooked={refresh}
              onTaken={() => {
                choose(null);
                refresh();
              }}
            />
          )}
        </>
      )}

      <ServicesOffered institution={institution} />
    </div>
  );

  function dayView(day: string, meal: PublicMealSlot, loaded: DayAvailability[] | null): DayView {
    const long = dates.full(day);
    const found = loaded?.find((d) => d.date === day);
    if (!found) return { label: long, disabled: true };

    if (found.bookable) {
      const taken = meal.bookingMode === "Shared" ? (meal.maxSponsors - found.sponsors) / meal.maxSponsors : 0;
      return {
        content: <Bowl fill={taken} className="text-brand" />,
        label: `${long}. ${meal.bookingMode === "Shared" ? t("placesLeft", { count: found.sponsors }) : t("open")}`,
        disabled: false,
      };
    }

    const reason = t(`reason.${found.reason ?? "slot_inactive"}`);
    return {
      content: found.reason === "slot_full" ? <Bowl fill={1} className="text-muted" /> : null,
      label: `${long}. ${reason}`,
      disabled: true,
    };
  }
}

function Profile({ institution }: { institution: PublicInstitution }) {
  const t = useTranslations("Discover");
  const locale = useLocale();

  return (
    <header className="grid gap-3">
      <p className="text-sm font-medium text-muted">
        {inLocale(locale, institution.typeName, institution.typeNameSi, institution.typeNameTa)}
      </p>
      <h1 className="text-3xl font-semibold leading-tight text-brand">{localName(locale, institution)}</h1>
      <p className="text-fg">
        {[institution.addressLine1, institution.addressLine2, institution.city].filter(Boolean).join(", ")} ·{" "}
        {inLocale(locale, institution.district, institution.districtSi, institution.districtTa)}
      </p>
      {institution.description && <p className="max-w-prose text-muted">{institution.description}</p>}
      {institution.dietaryNotes && (
        <p className="max-w-prose rounded-cup border border-hairline bg-subtle px-4 py-3 text-sm text-fg">
          <span className="font-semibold">{t("dietary")}</span> {institution.dietaryNotes}
        </p>
      )}
    </header>
  );
}

/**
 * The cost's digits. The card renders on the server too (plan 06, G9), and Node knows Sinhala's
 * currency format while Chromium does not ("රු." against "LKR"): the page would not hydrate. Plain
 * English digits read the same in both, and the message carries the currency in each language.
 */
const costFormat = new Intl.NumberFormat("en", { maximumFractionDigits: 2 });

/** The meals on offer as a choice of cards; each says what sponsoring it means. */
function SlotChooser({ slots, selected, onSelect }: { slots: PublicMealSlot[]; selected: string | null; onSelect: (id: string) => void }) {
  const t = useTranslations("Discover");
  const locale = useLocale();
  const dates = useDates();

  return (
    <section className="grid gap-3">
      <h2 id="meals" className="text-xl font-semibold text-brand">
        {t("chooseMeal")}
      </h2>
      <div role="radiogroup" aria-labelledby="meals" className="grid gap-3 sm:grid-cols-2">
        {slots.map((slot) => {
          const checked = slot.publicId === selected;
          const days = maskDays(slot.daysOfWeekMask);
          return (
            <button
              key={slot.publicId}
              type="button"
              role="radio"
              aria-checked={checked}
              onClick={() => onSelect(slot.publicId)}
              className={cn(
                "grid gap-1.5 rounded-bowl border-[1.5px] bg-surface p-4 text-left transition-colors duration-150",
                focusRing,
                checked ? "border-brand bg-brand/6 shadow-lift" : "border-hairline hover:border-brand/50",
              )}
            >
              <span className="flex items-start justify-between gap-3">
                <span className="text-lg font-semibold leading-snug text-brand">{localName(locale, slot)}</span>
                <span
                  aria-hidden
                  className={cn(
                    "mt-1 grid size-5 shrink-0 place-items-center rounded-full border-2",
                    checked ? "border-brand" : "border-line",
                  )}
                >
                  {checked && <span className="size-2.5 rounded-full bg-brand" />}
                </span>
              </span>
              <span className="text-base tabular-nums text-fg">
                {slot.startTime}–{slot.endTime}
                {" · "}
                {days.length === 7
                  ? t("everyDay")
                  : days.map((d) => dates.weekday(weekdayDate(d), "short")).join(" ")}
              </span>
              <span className="text-sm text-muted">
                {slot.bookingMode === "Shared" ? t("shared", { count: slot.maxSponsors }) : t("exclusive")}
                {" · "}
                {t("people", { min: slot.minHeadcount, max: slot.maxHeadcount })}
              </span>
              {slot.estimatedCostPerHead !== null && (
                <span className="text-sm text-muted">
                  {t("costPerHead", { cost: costFormat.format(slot.estimatedCostPerHead) })}
                </span>
              )}
            </button>
          );
        })}
      </div>
    </section>
  );
}

function Legend({ shared }: { shared: boolean }) {
  const t = useTranslations("Discover.legend");
  return (
    <ul className="flex flex-wrap gap-x-5 gap-y-2 text-sm text-muted">
      <li className="flex items-center gap-2">
        <Bowl fill={0} className="text-brand" /> {t("open")}
      </li>
      {shared && (
        <li className="flex items-center gap-2">
          <Bowl fill={0.5} className="text-brand" /> {t("filling")}
        </li>
      )}
      <li className="flex items-center gap-2">
        <Bowl fill={1} className="text-muted" /> {t("full")}
      </li>
      <li className="flex items-center gap-2">
        <span aria-hidden className="w-7 text-center font-medium">
          7
        </span>
        {t("notOffered")}
      </li>
    </ul>
  );
}

/** The meal to show: the one named, if the institution has it, else its first. */
function slotFor(institution: PublicInstitution, wanted: string | null): string | null {
  return institution.slots.some((s) => s.publicId === wanted) ? wanted : (institution.slots[0]?.publicId ?? null);
}

function validDate(value: string | null): string | null {
  return value && /^\d{4}-\d{2}-\d{2}$/.test(value) ? value : null;
}
