import type { Metadata } from "next";
import { hasLocale, useTranslations } from "next-intl";
import { getTranslations } from "next-intl/server";
import { notFound } from "next/navigation";
import type { ReactNode } from "react";
import { OtpFieldDemo, PhoneFieldDemo, SheetDemo, ToastDemo } from "@/components/design/demos";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from "@/components/ui/card";
import { Field } from "@/components/ui/field";
import { CloseIcon } from "@/components/ui/icons";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { LanguageSwitcher } from "@/components/ui/language-switcher";
import { OtpInput } from "@/components/ui/otp-input";
import { Spinner } from "@/components/ui/spinner";
import { routing } from "@/i18n/routing";

export async function generateMetadata({ params }: PageProps<"/[locale]/design">): Promise<Metadata> {
  const { locale } = await params;
  if (!hasLocale(routing.locales, locale)) notFound();

  const t = await getTranslations({ locale, namespace: "Design" });

  return {
    title: t("title"),
    // Review page only: never in search results.
    robots: { index: false, follow: false },
  };
}

const variants = ["primary", "accent", "outline", "ghost", "danger"] as const;

/** Every primitive in every state, for design review in en, si and ta. */
export default function DesignPage() {
  const t = useTranslations("Design");

  return (
    <main className="mx-auto w-full max-w-4xl px-4 py-10 sm:py-14">
      <header className="mb-10 grid gap-4">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <h1 className="text-3xl font-semibold leading-tight text-estate">{t("title")}</h1>
          <LanguageSwitcher />
        </div>
        <p className="text-muted">{t("intro")}</p>
        <p className="text-sm text-muted">{t("hint")}</p>
      </header>

      <div className="grid gap-12">
        <Section title={t("sections.buttons")}>
          <div className="grid gap-6">
            {variants.map((variant) => (
              <Row key={variant} label={t(`variants.${variant}`)}>
                <Button variant={variant}>{t("sample.action")}</Button>
                <Button variant={variant} disabled>
                  {t("states.disabled")}
                </Button>
                <Button variant={variant} loading>
                  {t("states.loading")}
                </Button>
              </Row>
            ))}
            <Row label={t("variants.large")}>
              <Button size="lg">{t("sample.action")}</Button>
              <Button size="lg" variant="accent">
                {t("sample.action")}
              </Button>
            </Row>
            <Row label={t("variants.icon")}>
              <Button size="icon" variant="outline" aria-label={t("sample.cancel")}>
                <CloseIcon />
              </Button>
              <Button size="icon" variant="ghost" aria-label={t("sample.cancel")}>
                <CloseIcon />
              </Button>
            </Row>
            <Row label={t("states.long")}>
              <div className="w-full max-w-xs">
                <Button className="w-full">{t("sample.longAction")}</Button>
              </div>
            </Row>
          </div>
        </Section>

        <Section title={t("sections.inputs")}>
          <div className="grid gap-5 sm:grid-cols-2">
            <Example label={t("states.empty")}>
              <Input aria-label={t("sample.phoneLabel")} placeholder={t("sample.phonePlaceholder")} />
            </Example>
            <Example label={t("states.filled")}>
              <Input aria-label={t("sample.phoneLabel")} defaultValue="0771234567" />
            </Example>
            <Example label={t("states.disabled")}>
              <Input aria-label={t("sample.phoneLabel")} defaultValue="0771234567" disabled />
            </Example>
            <Example label={t("states.invalid")}>
              <Input aria-label={t("sample.phoneLabel")} defaultValue="12345" aria-invalid />
            </Example>
          </div>
        </Section>

        <Section title={t("sections.fields")}>
          <div className="grid gap-8 sm:grid-cols-2">
            <Example label={t("states.default")}>
              <div className="grid gap-1.5">
                <Label htmlFor="design-name">{t("sample.nameLabel")}</Label>
                <Input id="design-name" autoComplete="name" />
              </div>
            </Example>
            <Example label={t("states.invalid")}>
              <Field
                label={t("sample.phoneLabel")}
                description={t("sample.phoneDescription")}
                error={t("sample.phoneError")}
                required
              >
                <Input type="tel" defaultValue="12345" />
              </Field>
            </Example>
            <Example label={t("states.disabled")}>
              <Field label={t("sample.phoneLabel")} description={t("sample.phoneDescription")}>
                <Input type="tel" defaultValue="0771234567" disabled />
              </Field>
            </Example>
            <Example label={t("states.default")}>
              <PhoneFieldDemo />
            </Example>
          </div>
        </Section>

        <Section title={t("sections.otp")}>
          <div className="grid gap-8 sm:grid-cols-2">
            <Example label={t("states.empty")}>
              <OtpInput aria-label={t("sample.otpLabel")} />
            </Example>
            <Example label={t("states.partial")}>
              <OtpInput aria-label={t("sample.otpLabel")} defaultValue="482" />
            </Example>
            <Example label={t("states.complete")}>
              <OtpInput aria-label={t("sample.otpLabel")} defaultValue="482913" />
            </Example>
            <Example label={t("states.invalid")}>
              <OtpInput aria-label={t("sample.otpLabel")} defaultValue="482914" aria-invalid />
            </Example>
            <Example label={t("states.disabled")}>
              <OtpInput aria-label={t("sample.otpLabel")} defaultValue="482913" disabled />
            </Example>
            <Example label={t("states.default")}>
              <OtpFieldDemo />
            </Example>
          </div>
        </Section>

        <Section title={t("sections.cards")}>
          <div className="grid gap-5 sm:grid-cols-2">
            <Card>
              <CardHeader>
                <CardTitle>{t("sample.cardTitle")}</CardTitle>
                <CardDescription>{t("sample.cardDescription")}</CardDescription>
              </CardHeader>
              <CardContent>
                <p>{t("sample.cardBody")}</p>
              </CardContent>
              <CardFooter>
                <Button>{t("sample.action")}</Button>
                <Button variant="ghost">{t("sample.cancel")}</Button>
              </CardFooter>
            </Card>
            <Card accent>
              <CardHeader>
                <CardTitle>{t("sample.cardAccentTitle")}</CardTitle>
                <CardDescription>{t("sample.cardDescription")}</CardDescription>
              </CardHeader>
              <CardContent>
                <p>{t("sample.cardAccentBody")}</p>
              </CardContent>
              <CardFooter>
                <Button variant="accent">{t("sample.action")}</Button>
              </CardFooter>
            </Card>
          </div>
        </Section>

        <Section title={t("sections.sheets")}>
          <SheetDemo />
        </Section>

        <Section title={t("sections.toasts")}>
          <ToastDemo />
        </Section>

        <Section title={t("sections.spinners")}>
          <Row label={t("states.default")}>
            <span className="text-estate">
              <Spinner size="sm" />
            </span>
            <span className="text-estate">
              <Spinner />
            </span>
            <span className="text-estate">
              <Spinner size="lg" />
            </span>
          </Row>
          <div className="mt-4 inline-flex items-center gap-3 rounded-2xl bg-estate px-5 py-4 text-ivory">
            <Spinner />
            <span>{t("sample.onEstate")}</span>
          </div>
        </Section>

        <Section title={t("sections.language")}>
          <LanguageSwitcher />
        </Section>
      </div>
    </main>
  );
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="grid gap-5">
      <h2 className="border-b border-hairline pb-2 text-xl font-semibold text-estate">{title}</h2>
      {children}
    </section>
  );
}

function Row({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="grid gap-2">
      <p className="text-sm font-medium text-muted">{label}</p>
      <div className="flex flex-wrap items-center gap-3">{children}</div>
    </div>
  );
}

function Example({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="grid content-start gap-2">
      <p className="text-sm font-medium text-muted">{label}</p>
      {children}
    </div>
  );
}
