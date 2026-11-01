import type { MetadataRoute } from "next";
import { robotsRules } from "@/lib/bookings/seo";
import { siteOrigin } from "@/lib/site";

/** Plan 06, G9: crawl the public pages, keep out of the signed-in ones, and find the sitemap. */
export default function robots(): MetadataRoute.Robots {
  return robotsRules(siteOrigin());
}
