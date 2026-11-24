"use client";

import { useLocale, useTranslations } from "next-intl";
import { useCallback, useEffect, useState, type FormEvent } from "react";
import { FormError } from "@/components/auth/FormError";
import { textLink } from "@/components/auth/links";
import { EmptyState } from "@/components/bookings/EmptyState";
import { useWide } from "@/components/bookings/useWide";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { Input, inputClasses } from "@/components/ui/input";
import { Sheet, SheetBody, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import type { BookingFailure } from "@/lib/bookings/api";
import { localName } from "@/lib/bookings/names";
import { createServiceType, listServiceTypes, setServiceTypeActive, updateServiceType } from "@/lib/services/api";
import type { ServiceLocation, ServiceTypeView } from "@/lib/services/types";
import { usePermissions } from "@/lib/permissions/usePermissions";
import { cn, focusRing } from "@/lib/utils";
import { useDuration } from "./useDuration";
import { useServiceError } from "./useServiceError";

const LOCATIONS: ServiceLocation[] = ["AtInstitution", "AtDonorPlace", "Either"];
const DURATIONS = Array.from({ length: 48 }, (_, i) => (i + 1) * 15); // 15 min to 12 h
const BUFFERS = Array.from({ length: 13 }, (_, i) => i * 15); // 0 to 3 h

/**
 * The religious services an institution offers (Phase E, promise 1): what each is called in three
 * languages, how long it takes, how many leaders it needs, where it is held, and how far ahead a
 * donor may ask. Nothing is seeded per faith (V4): the institution types its own.
 */
export function ServiceTypesScreen() {
  const t = useTranslations("Services.types");
  const locale = useLocale();
  const duration = useDuration();
  const errorMessage = useServiceError();
  const { can } = usePermissions();
  const canCreate = can("SERVICES.TYPES.CREATE");
  const canEdit = can("SERVICES.TYPES.EDIT");

  const [types, setTypes] = useState<ServiceTypeView[] | null>(null);
  const [failure, setFailure] = useState<BookingFailure | null>(null);
  const [editing, setEditing] = useState<ServiceTypeView | "new" | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  const load = useCallback(async () => {
    setFailure(null);
    const result = await listServiceTypes();
    if (result.ok) setTypes(result.value);
    else setFailure(result.failure);
  }, []);

  useEffect(() => {
    // Loading the screen's data is what this component is for.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load();
  }, [load]);

  async function toggle(type: ServiceTypeView) {
    setBusy(type.publicId);
    const result = await setServiceTypeActive(type.publicId, !type.isActive);
    setBusy(null);
    if (!result.ok) setFailure(result.failure);
    void load();
  }

  if (failure && !types) {
    return (
      <FormError>
        {errorMessage(failure)}{" "}
        <button type="button" onClick={() => void load()} className={cn(textLink, focusRing, "min-h-0")}>
          {t("retry")}
        </button>
      </FormError>
    );
  }

  if (!types) {
    return (
      <div className="flex justify-center py-16 text-brand">
        <Spinner />
      </div>
    );
  }

  return (
    <section className="grid gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-xl font-semibold text-brand">{t("heading")}</h2>
        {canCreate && <Button onClick={() => setEditing("new")}>{t("add")}</Button>}
      </div>

      <FormError>{failure && errorMessage(failure)}</FormError>

      {types.length === 0 ? (
        <EmptyState>{t("none")}</EmptyState>
      ) : (
        <ul className="grid gap-3 lg:grid-cols-2 2xl:grid-cols-3">
          {types.map((type) => (
            <li key={type.publicId}>
              <article className={cn("grid h-full content-start gap-1.5 rounded-bowl border border-hairline bg-surface p-4 shadow-lift", !type.isActive && "opacity-70")}>
                <div className="flex flex-wrap items-start justify-between gap-2">
                  <h3 className="min-w-0 text-lg font-semibold leading-snug text-brand">{localName(locale, type)}</h3>
                  {!type.isActive && <span className="rounded-full border border-hairline bg-subtle px-3 py-1 text-sm text-muted">{t("inactive")}</span>}
                </div>
                <p className="text-base text-fg">
                  {duration(type.durationMinutes)} · {t("leaders", { count: type.leadersRequired })}
                </p>
                <p className="text-sm text-muted">
                  {t(`where.${type.locationMode}`)}
                  {type.locationMode !== "AtInstitution" && type.bufferMinutes > 0 && <> · {t("travel", { duration: duration(type.bufferMinutes) })}</>}
                </p>
                <p className="text-sm text-muted">{t("window", { min: type.minAdvanceDays, max: type.maxAdvanceDays })}</p>
                {type.description && <p className="text-sm text-muted">{type.description}</p>}
                {canEdit && (
                  <div className="flex flex-wrap gap-2 pt-1">
                    <Button variant="outline" onClick={() => setEditing(type)}>
                      {t("edit")}
                    </Button>
                    <Button variant="ghost" loading={busy === type.publicId} onClick={() => void toggle(type)}>
                      {type.isActive ? t("deactivate") : t("activate")}
                    </Button>
                  </div>
                )}
              </article>
            </li>
          ))}
        </ul>
      )}

      {editing && (
        <ServiceTypeSheet
          type={editing === "new" ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            void load();
          }}
        />
      )}
    </section>
  );
}

type Form = {
  name: string;
  nameSi: string;
  nameTa: string;
  description: string;
  durationMinutes: number;
  bufferMinutes: number;
  leadersRequired: string;
  locationMode: ServiceLocation;
  minAdvanceDays: string;
  maxAdvanceDays: string;
};

/** Fields the API can name; each has its own message under Services.types.form.errors. */
type FieldName =
  | "name" | "nameSi" | "nameTa" | "description" | "durationMinutes" | "bufferMinutes" | "leadersRequired" | "locationMode"
  | "minAdvanceDays" | "maxAdvanceDays";

/** A service type, new or edited; an edit sends back the version it started from, so the second of two editors is told to reload. */
function ServiceTypeSheet({ type, onClose, onSaved }: { type: ServiceTypeView | null; onClose: () => void; onSaved: () => void }) {
  const t = useTranslations("Services.types.form");
  const places = useTranslations("Services.types.where");
  const duration = useDuration();
  const wide = useWide();
  const errorMessage = useServiceError();
  const [form, setForm] = useState<Form>(() => ({
    name: type?.name ?? "",
    nameSi: type?.nameSi ?? "",
    nameTa: type?.nameTa ?? "",
    description: type?.description ?? "",
    durationMinutes: type?.durationMinutes ?? 60,
    bufferMinutes: type?.bufferMinutes ?? 0,
    leadersRequired: String(type?.leadersRequired ?? 1),
    locationMode: type?.locationMode ?? "AtInstitution",
    minAdvanceDays: String(type?.minAdvanceDays ?? 1),
    maxAdvanceDays: String(type?.maxAdvanceDays ?? 180),
  }));
  const [invalid, setInvalid] = useState<Set<string>>(new Set());
  const [failure, setFailure] = useState<BookingFailure | null>(null);
  const [saving, setSaving] = useState(false);

  function set<K extends keyof Form>(key: K, value: Form[K]) {
    setForm((current) => ({ ...current, [key]: value }));
    setInvalid((current) => {
      if (!current.has(key)) return current;
      const next = new Set(current);
      next.delete(key);
      return next;
    });
  }

  const error = (field: FieldName) => (invalid.has(field) ? t(`errors.${field}`) : null);
  const whole = (value: string) => (/^\d+$/.test(value.trim()) ? Number(value) : null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setFailure(null);

    const local = new Set<string>();
    if (form.name.trim() === "") local.add("name");
    const leaders = whole(form.leadersRequired);
    if (leaders === null || leaders < 1 || leaders > 20) local.add("leadersRequired");
    const min = whole(form.minAdvanceDays);
    const max = whole(form.maxAdvanceDays);
    if (min === null) local.add("minAdvanceDays");
    if (max === null || (min !== null && max < min)) local.add("maxAdvanceDays");
    if (local.size > 0) {
      setInvalid(local);
      return;
    }

    setSaving(true);
    const input = {
      name: form.name.trim(),
      nameSi: form.nameSi.trim() || null,
      nameTa: form.nameTa.trim() || null,
      description: form.description.trim() || null,
      durationMinutes: form.durationMinutes,
      bufferMinutes: form.locationMode === "AtInstitution" ? 0 : form.bufferMinutes,
      leadersRequired: leaders,
      locationMode: form.locationMode,
      minAdvanceDays: min,
      maxAdvanceDays: max,
      version: type?.version,
    };
    const result = type ? await updateServiceType(type.publicId, input) : await createServiceType(input);
    setSaving(false);

    if (result.ok) {
      onSaved();
      return;
    }
    if (result.failure.code === "invalid_details" && result.failure.fields.length > 0) {
      setInvalid(new Set(result.failure.fields));
      return;
    }
    if (result.failure.code === "service_name_taken") setInvalid(new Set(["name"]));
    setFailure(result.failure);
  }

  return (
    <Sheet open onOpenChange={(open) => !open && onClose()}>
      <SheetContent side={wide ? "right" : "bottom"} className={wide ? "max-w-lg" : undefined}>
        <form onSubmit={submit} noValidate className="contents">
          <SheetHeader>
            <SheetTitle>{type ? t("editTitle") : t("newTitle")}</SheetTitle>
            <SheetDescription>{t("description")}</SheetDescription>
          </SheetHeader>

          <SheetBody className="grid gap-5">
            <Field label={t("name")} description={t("nameHint")} error={error("name") ?? (failure?.code === "service_name_taken" ? t("nameTaken") : null)} required>
              <Input value={form.name} onChange={(e) => set("name", e.target.value)} maxLength={100} autoComplete="off" />
            </Field>
            <div className="grid gap-5 sm:grid-cols-2">
              <Field label={t("nameSi")} error={error("nameSi")}>
                <Input lang="si" value={form.nameSi} onChange={(e) => set("nameSi", e.target.value)} maxLength={100} autoComplete="off" />
              </Field>
              <Field label={t("nameTa")} error={error("nameTa")}>
                <Input lang="ta" value={form.nameTa} onChange={(e) => set("nameTa", e.target.value)} maxLength={100} autoComplete="off" />
              </Field>
            </div>
            <Field label={t("descriptionLabel")} description={t("descriptionHint")} error={error("description")}>
              <textarea value={form.description} onChange={(e) => set("description", e.target.value)} maxLength={300} rows={2} className={cn(inputClasses, "resize-y")} />
            </Field>

            <div className="grid grid-cols-2 gap-4">
              <Field label={t("duration")} error={error("durationMinutes")}>
                <select value={form.durationMinutes} onChange={(e) => set("durationMinutes", Number(e.target.value))} className={inputClasses}>
                  {DURATIONS.map((minutes) => (
                    <option key={minutes} value={minutes}>
                      {duration(minutes)}
                    </option>
                  ))}
                </select>
              </Field>
              <Field label={t("leadersRequired")} description={t("leadersHint")} error={error("leadersRequired")}>
                <Input inputMode="numeric" value={form.leadersRequired} onChange={(e) => set("leadersRequired", e.target.value)} />
              </Field>
            </div>

            <fieldset className="grid gap-2">
              <legend className="mb-1 text-base font-medium text-fg">{t("where")}</legend>
              {LOCATIONS.map((mode) => (
                <label
                  key={mode}
                  className={cn(
                    "flex min-h-12 cursor-pointer items-center gap-3 rounded-cup border-[1.5px] bg-surface px-4 py-2",
                    "has-focus-visible:outline-2 has-focus-visible:outline-offset-2 has-focus-visible:outline-brand",
                    form.locationMode === mode ? "border-brand bg-brand/6" : "border-hairline",
                  )}
                >
                  <input type="radio" name="locationMode" checked={form.locationMode === mode} onChange={() => set("locationMode", mode)} className="size-5 accent-[var(--color-brand)]" />
                  <span className="font-medium text-fg">{places(mode)}</span>
                </label>
              ))}
            </fieldset>

            {form.locationMode !== "AtInstitution" && (
              <Field label={t("buffer")} description={t("bufferHint")} error={error("bufferMinutes")}>
                <select value={form.bufferMinutes} onChange={(e) => set("bufferMinutes", Number(e.target.value))} className={cn(inputClasses, "w-48")}>
                  {BUFFERS.map((minutes) => (
                    <option key={minutes} value={minutes}>
                      {minutes === 0 ? t("noBuffer") : duration(minutes)}
                    </option>
                  ))}
                </select>
              </Field>
            )}

            <div className="grid grid-cols-2 gap-4">
              <Field label={t("minAdvance")} description={t("minAdvanceHint")} error={error("minAdvanceDays")}>
                <Input inputMode="numeric" value={form.minAdvanceDays} onChange={(e) => set("minAdvanceDays", e.target.value)} />
              </Field>
              <Field label={t("maxAdvance")} description={t("maxAdvanceHint")} error={error("maxAdvanceDays")}>
                <Input inputMode="numeric" value={form.maxAdvanceDays} onChange={(e) => set("maxAdvanceDays", e.target.value)} />
              </Field>
            </div>

            <FormError>{failure && failure.code !== "service_name_taken" && errorMessage(failure)}</FormError>
            {invalid.size > 0 && <p className="text-sm text-danger" aria-live="polite">{t("fixFields")}</p>}
          </SheetBody>

          <SheetFooter>
            <Button variant="ghost" onClick={onClose}>
              {t("cancel")}
            </Button>
            <Button type="submit" loading={saving}>
              {type ? t("save") : t("create")}
            </Button>
          </SheetFooter>
        </form>
      </SheetContent>
    </Sheet>
  );
}
