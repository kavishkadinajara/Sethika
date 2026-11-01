import type { MetadataRoute } from "next";
import { institutionPath, sitemapEntries } from "@/lib/bookings/seo";
import { listPublicInstitutions } from "@/lib/bookings/server";
import { siteOrigin } from "@/lib/site";

// Read on each request: an institution appears here the moment it is listed, and goes when it is not.
export const dynamic = "force-dynamic";

/** Plan 06, G9: the public pages, the home page, the search and every listed institution, in each language. */
export default async function sitemap(): Promise<MetadataRoute.Sitemap> {
  const institutions = await listPublicInstitutions();
  return sitemapEntries(["/", "/find", ...institutions.map((i) => institutionPath(i.slug))], siteOrigin());
}
