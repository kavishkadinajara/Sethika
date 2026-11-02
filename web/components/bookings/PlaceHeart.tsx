"use client";

import { useTranslations } from "next-intl";
import { useEffect, useState } from "react";
import { Button } from "@/components/ui/button";
import { HeartIcon } from "@/components/ui/icons";
import { useToast } from "@/components/ui/toast";
import { keepPlace, listPlaces } from "@/lib/bookings/api";
import { useSession } from "@/lib/auth/useSession";
import { cn } from "@/lib/utils";

/**
 * The heart on an institution's page (plan 07, H12): a signed-in person keeps the place among "My
 * places" on their home, or lets it go. Shown only in a personal session; a visitor sees no heart.
 */
export function PlaceHeart({ slug, name }: { slug: string; name: string }) {
  const t = useTranslations("Discover.heart");
  const session = useSession();
  const toast = useToast();
  const personal = session.status === "authenticated" && !session.activeInstitution;
  const [kept, setKept] = useState<boolean | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!personal) return;
    let cancelled = false;
    void listPlaces().then((result) => {
      if (!cancelled && result.ok) setKept(result.value.some((p) => p.slug === slug));
    });
    return () => {
      cancelled = true;
    };
  }, [personal, slug]);

  if (!personal || kept === null) return null;

  async function toggle() {
    const next = !kept;
    setBusy(true);
    setKept(next);
    const result = await keepPlace(slug, next);
    setBusy(false);
    if (result.ok) {
      toast({ variant: "success", title: next ? t("kept", { name }) : t("letGo", { name }) });
    } else {
      setKept(!next);
      toast({ variant: "error", title: t("failed") });
    }
  }

  return (
    <Button
      variant="outline"
      aria-pressed={kept}
      disabled={busy}
      onClick={() => void toggle()}
      className={cn("shrink-0", kept && "border-accent bg-accent/12 text-fg hover:bg-accent/20")}
    >
      <HeartIcon filled={kept} className={cn("size-5", kept ? "text-accent-strong" : "text-brand")} />
      {kept ? t("on") : t("off")}
    </Button>
  );
}
