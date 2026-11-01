# Plan 06 — Phase B: public pages, cross-institution search, Redis GEO and the map

Branch: `rcsi-on` for now (RCSI first, then this phase on the same branch), cut on 2026-11-01 from
`phase-e-donor` at 10f870c (Phase E on `develop`).

Status: **G1-G11 agreed by the user 2026-11-01. B.1 built and committed by the user (7954b8c,
together with RCSI and the Phase E report draft). B.2 committed by the user (46dbc4f). B.3 built, not committed. B.4-B.5 not started.** Last updated 2026-11-01. Update this line and the §7 boxes whenever
either changes; a fresh session trusts them.

Decision numbers here are **G1, G2, …** (Geo / discovery). Plan 03 uses E1-E35 and Phase E V1-V39.

## 0. What the proposal promises (the report must match this list)

Proposal v3, "Cross-Institution Availability Search" and Phase B:

1. **Public institution pages**, server-rendered so that search engines can read them.
2. A donor searches **across every registered institution at once** by **date, meal slot,
   institution type and distance**: "Which institutions near Galle have a free lunch slot on 14
   March?"
3. Distance filtering is **powered by Redis geo search**.
4. The answer is shown **on a list, one calendar and an interactive map** (Leaflet with
   OpenStreetMap).

## 1. What already exists

- `org.Institutions` has `Latitude`, `Longitude` and a persisted `geography` column `Location`, put
  there for "DB-side radius search; Redis GEO is the fast path", with the spatial index
  `SIX_Institutions_Location` (the first draft of this plan missed it: no schema change is needed).
  `DistrictId` refers to `core.Districts`, which has names in three languages but **no coordinates**.
- `GET /api/institutions` (plain list of `Active` and `IsPubliclyListed`), `GET /api/institutions/{slug}`
  and `GET /api/institutions/{slug}/availability?month=` (one institution's month, plan 03, C10).
  All three are anonymous and rate-limited per client.
- `MealSlotRules.Openings` judges whether a slot can be booked on a day. The calendar and the
  booking engine both use it, so they cannot disagree (`AvailabilityCalendar`).
- `booking.MealSlots` has **no meal type**: a slot is a name and a start and end time.
- `booking.AvailabilitySearches` exists for demand signals (date, type, district, radius, result
  count).
- `/i/[slug]` renders in the browser: the page's HTML has no institution name for a search engine.
- No Redis GEO code and no map yet. Leaflet is in the stated stack (CLAUDE.md) but not installed.
- The local Redis is **3.2.100** (Windows build): it has `GEOADD` and `GEORADIUS`, not `GEOSEARCH` (6.2).
- Only one code path changes whether an institution belongs in the index: the listing toggle
  (`SlotManagementHandler.SetListedAsync`). Approval creates an unlisted institution, and nothing
  yet suspends an institution or edits its address.

## 2. Decisions

| # | Decision | Why |
|---|---|---|
| G1 | A search takes a **date** (required, Colombo), a **time of day** (any, morning, midday, evening), an optional **institution type**, and **where**: near a point with a radius (5, 10, 25 or 50 km), within a district, or anywhere. The answer is the institutions with at least one slot that can be booked that day, each with those slots and the sponsor places left, and the distance when a point was given. Nearest first, else by name; at most 100. | The proposal's question, word for word. One date because a donor searches for a meaningful date; the month view (G6) helps find one. |
| G2 | The time of day comes from the slot's **start time**: morning before 10:30, midday from 10:30 until 15:00, evening from 15:00. | Slots have no meal type, and adding one would mean asking every institution to classify slots it already has. Dana times are fixed by custom (morning dana, the noon meal before midday, evening), so the start time already says which meal it is. The boundaries are a UI default in one place, not a rule in the database. |
| G3 | **Where** comes from the browser's location ("near me"), the centre of the map ("search this area"), or a district from the list. No geocoding of place names. | A geocoder is an external service with its own terms, and district centre coordinates would be invented reference data (CLAUDE.md). "Near Galle" is the Galle district, or the map moved to Galle. The browser's coordinates are sent rounded to 3 decimals (about 100 m) and are never stored. |
| G4 | Redis GEO key `geo:institutions` (db 3, like every Sethika key; `GeoIndex:Key` in configuration, so tests use their own): members are institution ids, positions their coordinates. **`GEORADIUS ... ASC`** gives the candidates in a circle, nearest first (not `GEOSEARCH`: the local Redis is 3.2, and managed Redis still answers `GEORADIUS`). Only `Active` and publicly listed institutions are in the set. | The fast path the proposal promises. Ids never leave the server: the answer carries slugs. |
| G5 | **SQL Server is the truth, the GEO set is an index of it.** The set is rebuilt from SQL when the host starts and every night at 03:00 Colombo (Hangfire job `geo-index-rebuild`, triggered once at start; a `MULTI/EXEC` of `DEL` + `GEOADD`, so a search never sees half an index), and kept current by an outbox handler on `InstitutionListingChanged` (the only change that exists today; a status or address change will raise its own event to the same handler). If Redis is down or the key is missing, the same search runs in SQL on `Location.STDistance` under the existing spatial index. **Neither measure decides**: both fetch candidates from a circle 2 % wider, SQL re-checks `Active` and listed (the index can lag an outbox run), and one haversine on the mean-radius sphere (`GeoPoint.DistanceKmTo`) decides who is inside and in what order. A refresh that finds no key rebuilds the whole index rather than creating it with one member. | Like the booking lock (C2), Redis makes it fast and SQL makes it correct: an outage degrades the speed, not the answer. Redis measures on a sphere of radius 6372.8 km and SQL Server on the WGS 84 ellipsoid; east-west near Colombo they differ by 0.12 %, so each path judging by its own measure would disagree at the edge. A rebuild makes the set converge even if a message was missed. |
| G6 | "One calendar": `GET /api/search/month?month=&...` gives, for each day of the month, how many institutions have a slot that can be booked under the same filters. The donor picks a day on the `BowlCalendar`, and the list and the map show that day. | The proposal's "one calendar". Counting per day reuses G1's judgement for every day of the month. |
| G7 | Every slot-day is judged by `MealSlotRules.Openings`, from one batch query for all candidate institutions (slots, seasons, blocked dates and slot instances, through `core.IntList`). Results are not cached. | The search, an institution's calendar and the engine then cannot disagree. A cached "free" that has just been booked would send donors to a full slot; the engine would refuse it, but the search would have lied. |
| G8 | The search endpoints are anonymous and rate-limited per client, like the other public reads, and answer only what the public page shows (names, type, district, city, coordinates, slot names and times, places left). Each search writes one row to `booking.AvailabilitySearches` (no user id, no coordinates). | Searching must not need an account. The coordinates of an institution are already public (its address). The search log is the demand signal Phase D's dashboard can read; it stores nothing about the donor's position. |
| G9 | `/i/[slug]` is **rendered on the server**: name, type, address, description and the meal and service lists in the HTML, with `generateMetadata` (title, description, Open Graph, canonical, `hreflang` for en/si/ta) and schema.org JSON-LD. The calendar and the sheets stay interactive in the browser. A `sitemap.xml` lists every listed institution in the three languages; `robots.txt` keeps the signed-in screens out. | Promise 1. A search engine reads HTML, not the API. |
| G10 | The map is **Leaflet** (the `leaflet` package only, loaded in the browser) with OpenStreetMap tiles and their attribution. Markers are our own HTML marker, not Leaflet's default images. | In the stack already. One package, no React wrapper: the map is a single component. OSM's tile policy allows light use such as this; production would move to a tile provider (stated in the report). |
| G11 | The search page is **`/find`**: filters in the address (like the admin lists, so back undoes a filter), the list and the map side by side on a wide screen, and "List" and "Map" tabs on a phone. "Find a place to give" in the sidebar opens it; `/institutions` sends visitors there. | One place to look for somewhere to give. The address can be shared ("lunch near Galle on 14 March"). |

## 3. API

- `GET /api/search?date=yyyy-MM-dd&time=any|morning|midday|evening&type={code}&near=lat,lng&radiusKm=10`
  or `&district={code}`. Answer: `{ date, results: [{ slug, name, nameSi, nameTa, type, district,
  city, latitude, longitude, distanceKm, slots: [{ mealSlot, name, startTime, endTime, sponsors,
  headcount }] }], more }`, with `typeName`, `districtSi/Ta` and the slots' `nameSi/nameTa` as on
  the public institution list; `more` says more than 100 matched. Refusals: 400 `invalid_search`
  with `fields` (no date, a date in the past, a radius not in the list or without a point, both a
  point and a district, coordinates outside Sri Lanka's box (the box of `CK_RegReq_Latitude`/
  `Longitude`), an unknown time of day, type or district code).
- `GET /api/search/month?month=yyyy-MM&...same filters...` → `{ month, days: [{ date, institutions }] }`.
  The month may be this one up to 12 months ahead (`MaxMonthsAhead`); 400 `invalid_search` with
  `month` otherwise. No search log row.
- Both anonymous, `PublicReadPerClient` rate limit; added to `EndpointPolicyTests`' anonymous list.

## 4. Tests (what must be proved)

- **The answer**, against real SQL Server and Redis: only `Active` and listed institutions; only
  slots that `Openings` allows on that date (season, blocked date, full slot, advance window); the
  time-of-day boundaries; type and district filters; distance order and the radius edge.
- **Redis down gives the same answer**: the same searches with the GEO path switched off (and with
  an empty set) must return the same institutions in the same order.
- **The index converges**: approve, list, unlist, suspend and move an institution, run the outbox,
  and the set follows; a rebuild after a deleted member restores it.
- **Mutations**: drop the `Active`/listed filter from the GEO rebuild and from the SQL fallback;
  break a time-of-day boundary; skip `Openings`; sort by name when a point was given; forget the
  radius in the SQL fallback.
- **Pages**: the server HTML of `/en/i/{slug}` contains the name, the meta description and the
  JSON-LD; `sitemap.xml` lists it in three languages.
- **Browser** (desktop and 320 px, en/si/ta): a donor searches a date near a point, sees the list
  and the map, picks a day on the month view, opens an institution, and books from it; a search
  with no result says so in words.

## 5. Screens

- `/find`: date, time of day, type, where (near me / this area / district / anywhere), radius; the
  month view; the list (cards with the open slots, a "Book" link to `/i/{slug}?slot=&date=`); the
  map with a marker per result and the searched circle. Every word in en/si/ta.
- `/i/[slug]`: server-rendered head and body as G9; nothing changes for the donor.

## 6. Out of scope (the report says so)

- Geocoding typed place names (G3), routes and directions.
- Grouping nearby markers into clusters (another package; results are capped at 100).
- Searching for religious services across institutions (Phase E §6).
- Availability alerts and the admin's demand heat map (designed, optional; the search log is kept).
- A production tile provider (G10).

## 7. Work breakdown

- [x] **B.1 Search engine**: the batch availability query, the time-of-day rule, `GeoIndex`
      (Redis GEO + SQL fallback + spatial index), the rebuild job and the outbox handler,
      `GET /api/search`, the search log. Tests and mutations as §4. ~0.5 day
      Built 2026-11-01, not committed: `MealTimes`, `GeoPoint` (Domain); `AvailabilitySearch`,
      `InstitutionGeoHandler`, the `InstitutionListingChanged` outbox message in `SetListedAsync`
      (Application); `DiscoveryQueries` (one batch, `core.IntList`), `RedisInstitutionGeoIndex`,
      `GeoIndexRebuildJob` (Infrastructure); `SearchEndpoints`. Tests: 11 domain
      (`MealTimesTests`, `GeoPointTests`), 15 integration (`SearchEndpointsTests`, real SQL Server
      and Redis, incl. Redis down, index missing, index stale, the east edge point where the two
      measures part), `EndpointPolicyTests` and `HangfireJobsTests` updated; neighbours 67/67.
      Mutations 35/35 killed (30 integration, 5 domain; a no-op control survived), among them every
      one in §4 plus the 2 % margin, the rebuild's `DEL`, a refresh that creates a one-member index,
      the ids filter, the time-of-day filter, seasons, blocks and slot-days ignored.
- [x] **B.2 The month view**: `GET /api/search/month`. Tests and mutations. ~0.25 day
      Built 2026-11-01: `AvailabilitySearch.MonthAsync` (one read for the month, every day judged by
      the day search's own candidates and `SlotDayJudge`; this month back to 12 months ahead; not
      logged), `/month` on `SearchEndpoints` with the filters read once for both. Tests: 4 month
      tests (each day's count equals a hand count and the day search's result count; filters and
      Redis down; days gone count none; refusals and no log row) and one rate-limit test for both
      endpoints, which shared nothing before. `SearchEndpointsTests` 20/20.
- [x] **B.3 Public pages**: `/i/[slug]` on the server (G9), JSON-LD, `sitemap.xml`, `robots.txt`.
      ~0.25 day
      Built 2026-11-01: the page is a server component that reads the institution once per request
      (React `cache`, shared with `generateMetadata`) and hands it to `InstitutionPage` as
      `initial`, so the client component renders it in the server's HTML and does not fetch it
      again; if the API cannot answer, the page loads in the browser as before; a missing or
      inactive institution is a real 404 with the page's own words (segment `not-found.tsx`). The
      server forwards the visitor's address (the last `X-Forwarded-For` entry) so its reads count on
      the visitor's limit, not the web server's. Metadata: title, snippet (own description cut at
      160, else `Metadata.institution` in en/si/ta), canonical, `hreflang` en/si/ta + x-default,
      Open Graph, `metadataBase` from `SITE_URL`. JSON-LD with each faith's schema.org type
      (Place for care homes), `<` escaped. `sitemap.xml` (dynamic: home, directory, every listed
      institution, one entry per language), `robots.txt` (signed-in screens disallowed in all three
      languages). Tests: 12 vitest (`lib/bookings/seo.test.ts`), 5 Playwright
      (`e2e/discovery/public-pages.spec.ts`: raw HTML, the forwarded address on the API's counter,
      404, sitemap/robots, hydration without errors and no second fetch). Mutations 14/14 killed
      (control survived). The dev `Sethika` database now has RCSI on (published 2026-11-01).
      Owed to the batch's `npm run build`: confirm `/sitemap.xml` is listed as dynamic.
- [ ] **B.4 `/find`**: filters, list, month view, Leaflet map, near me, search this area; the
      sidebar link and the `/institutions` redirect; en/si/ta; Playwright desktop + 320 px. ~0.75 day
- [ ] **B.5 Done**: decisions into `docs/DECISIONS.md`, the report chapter, the full test runs, the PR.

**If time runs out, cut in this order:** the search log, the month view (G6; the list and the map
stay), "search this area" (near me and districts stay). Never cut the SQL fallback or its tests.

## 8. Definition of done

1. `dotnet build` and `dotnet format --verify-no-changes` clean.
2. Full `dotnet test` green; every new test seen to fail under a mutation.
3. `cd web && npm run lint && npm test && npm run build` clean; full `npm run e2e` green (Console SMS).
4. A second `SqlPackage /Action:Script` shows seed MERGEs only.
5. `docs/DECISIONS.md` summarises G1-G11 and the cut list; `docs/report/PHASE-B.md` written.
