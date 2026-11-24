# Sethika diagrams

Every diagram is a PlantUML source in `src/`, rendered three ways:

| Folder | Use |
|---|---|
| `png/` | Word report and slides (rendered at 2×) |
| `svg/` | web, or any size without blur |
| `pdf/` | print and appendix (vector, one page per diagram) |

## Contents

### ERD — `erd/`, generated from the SQL project
| File | Shows |
|---|---|
| `00_overview` | the 6 modules, their 94 tables, and how many FKs link each pair |
| `01_access_rbac` | `auth`: users, sessions, roles, permissions catalogue (Portal → Module → Screen → Element) |
| `02_onboarding_institutions` | `org`: registration requests, institutions, members, modules, plans |
| `03_booking` | `booking`: meal slots, slot instances, bookings, recurring, standing, memorial |
| `04_religious_services` | Phase E: service types, appointments, leaders, hours, leave |
| `05_giving_engagement` | `giving` + `notify` + gratitude wall |
| `06_core_audit` | `core` reference data, number series, outbox, `audit` |

Temporal `*History` tables are left out (they mirror their table). Lines for `CreatedBy`-style columns
(to `auth.Users`) and for the `InstitutionId` tenant key (outside `org`) are not drawn, so the
relationships that matter stay readable; the legend on each diagram says so.

### Class diagrams — `class/`, generated from the C# code
| File | Shows |
|---|---|
| `01_domain_bookings` | booking domain: `MealSlot`, `SlotInstance`, `Booking`, `MealSlotRules`, recurring / standing / memorial |
| `02_domain_onboarding_access` | `RegistrationRequest` and its parts, `Institution`, RBAC (`User`, `Role`, `Permission`, `RefreshToken`) |
| `03_domain_giving_services` | `Need`, `Donation`, `Pledge`, `OnlinePayment`, PayHere; Phase E services and leaders |
| `04_application_ports` | Clean Architecture: handlers → ports (Application interfaces) ← adapters (Infrastructure) |

### Use case diagrams — `usecase/`
| File | Shows |
|---|---|
| `00_context` | all actors, the system boundary, external systems |
| `01_donor` | visitor and donor: discovery, account, bookings, memorial, services, giving |
| `02_institution` | institution staff, owner, religious leader; step-up MFA includes |
| `03_onboarding_platform` | applicant, platform admin, scheduler |

Use cases follow the permission catalogue (`Scripts/Seed/10_RbacCatalogue.sql`); `<<include>> Verify
with SMS code` is drawn exactly for the permissions with `RequiresMfa = 1`.

### Sequence diagrams — `sequence/`
| File | Flow |
|---|---|
| `01_sign_up_otp` | phone OTP (hash in Redis), ticket, one-transaction account creation |
| `02_request_authorization_rls` | JWT → `[HasPermission]` → cached effective permissions → step-up → RLS session context |
| `03_book_meal` | Redis lock → rules → slot instance → conditional UPDATE → number → booking + outbox |
| `04_outbox_notifications` | `UPDLOCK, READPAST` batch, SMS with `DedupKey`, push, SignalR, evening reminders |
| `05_session_refresh` | refresh-token rotation, reuse detection, two-tab grace period |
| `06_onboarding_approve` | approval as one transaction |
| `07_online_giving_payhere` | checkout hash, signed notification, receipt in one transaction |

## Regenerating

Requires Java 17+ and `plantuml-1.2024.7.jar` (github.com/plantuml/plantuml/releases; not committed).
Playwright comes from `web/node_modules`.

```bash
python -I docs/diagrams/tools/erd_from_sql.py      # after a schema change
python -I docs/diagrams/tools/class_from_cs.py     # after a domain / handler change
node docs/diagrams/tools/render.mjs <path/to/plantuml.jar> [name-filter]
```

The ERD and class generators read the real SQL and C#, so a diagram cannot show a column, property or
method that does not exist; a class named in `class_from_cs.py` that has been renamed stops the script.
