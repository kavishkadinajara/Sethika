import type { FullConfig } from "@playwright/test";
import { clearRateLimits } from "./redis";
import { sql } from "./sql";

/*  next dev compiles a route on its first request, and two first requests at once can race on
    .next's prerender manifest - one reads it half-written and answers 500 ("Unexpected end of
    JSON input"). Opening every route once, one at a time, before the tests start in parallel
    keeps that dev-server race out of the results. A production build has no such step.      */
const routes = ["/", "/login", "/signup", "/apply", "/apply/new", "/apply/REQ-2026-000000"];
const locales = ["", "/si", "/ta"];

async function warmUp(baseURL: string) {
  for (const locale of locales) {
    for (const route of routes) {
      const response = await fetch(`${baseURL}${locale}${route === "/" && locale ? "" : route}`);
      await response.text();
      if (response.status >= 500) throw new Error(`Warming up ${locale}${route}: ${response.status}`);
    }
  }
}

/** Runs once before the suite: clean counters, and no applications left over from earlier runs. */
export default async function globalSetup(config: FullConfig) {
  await warmUp(config.projects[0].use.baseURL!);
  await clearRateLimits();

  // Earlier runs' applications would fill the reviewer's queue. An approved one created an
  // institution and stays; the rest go, with their uploads, outbox and audit rows.
  sql(`
    DECLARE @users TABLE (Id uniqueidentifier PRIMARY KEY);
    INSERT @users SELECT Id FROM auth.Users WHERE FullName LIKE N'E2E %';
    DECLARE @requests TABLE (Id int PRIMARY KEY, RequestNo varchar(30));
    INSERT @requests SELECT Id, RequestNo FROM org.RegistrationRequests
      WHERE ApplicantUserId IN (SELECT Id FROM @users) AND CreatedInstitutionId IS NULL;

    DELETE FROM core.OutboxMessages WHERE AggregateType = 'RegistrationRequest' AND AggregateId IN (SELECT RequestNo FROM @requests);
    DELETE FROM audit.AuditLogs WHERE EntityName = 'RegistrationRequest' AND EntityId IN (SELECT RequestNo FROM @requests);
    DELETE FROM org.RegistrationRequests WHERE Id IN (SELECT Id FROM @requests);
    DELETE f FROM core.Files f
      WHERE f.UploadedBy IN (SELECT Id FROM @users)
        AND NOT EXISTS (SELECT 1 FROM org.RegistrationRequestDocuments d WHERE d.FileId = f.Id)
        AND NOT EXISTS (SELECT 1 FROM org.InstitutionDocuments d WHERE d.FileId = f.Id);
  `);
}
