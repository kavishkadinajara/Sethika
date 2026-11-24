"""Generate the ERD PlantUML sources from the SQL project (the schema source of truth).

Usage (from the repo root):
    python -I docs/diagrams/tools/erd_from_sql.py

Reads every src/Sethika.Database/<schema>/Tables/*.sql (temporal *History tables are left out: they
mirror their table) and writes docs/diagrams/src/erd/*.puml - one overview and one per module.

Columns that only record *who* did something (CreatedBy, DecidedBy, ...) reference auth.Users. They
are shown as FK columns but get no line, otherwise every table would have one to Users.
"""
from __future__ import annotations

import re
from dataclasses import dataclass, field
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
DB = ROOT / "src" / "Sethika.Database"
OUT = ROOT / "docs" / "diagrams" / "src" / "erd"


@dataclass
class Column:
    name: str
    type: str
    nullable: bool
    pk: bool = False
    fk: str | None = None  # referenced table, "schema.Table"


@dataclass
class Table:
    full: str
    columns: list[Column] = field(default_factory=list)
    fks: list[tuple[list[str], str]] = field(default_factory=list)  # (child columns, parent table)

    @property
    def schema(self) -> str:
        return self.full.split(".")[0]

    @property
    def alias(self) -> str:
        return self.full.replace(".", "_")


def strip_comments(sql: str) -> str:
    sql = re.sub(r"/\*.*?\*/", "", sql, flags=re.S)
    return re.sub(r"--[^\n]*", "", sql)


def split_top_level(body: str) -> list[str]:
    parts, depth, cur, quote = [], 0, [], False
    for ch in body:
        if ch == "'":
            quote = not quote
        if not quote:
            if ch == "(":
                depth += 1
            elif ch == ")":
                depth -= 1
            elif ch == "," and depth == 0:
                parts.append("".join(cur).strip())
                cur = []
                continue
        cur.append(ch)
    if "".join(cur).strip():
        parts.append("".join(cur).strip())
    return parts


TYPE_RE = re.compile(r"^(\w+)\s+((?:\w+)(?:\s*\([^)]*\))?)", re.S)
REF_RE = re.compile(r"REFERENCES\s+([\w]+\.[\w]+)\s*\(", re.I)
FK_RE = re.compile(r"FOREIGN\s+KEY\s*\(([^)]*)\)\s*REFERENCES\s+([\w]+\.[\w]+)", re.I | re.S)
PK_RE = re.compile(r"PRIMARY\s+KEY\s*(?:CLUSTERED|NONCLUSTERED)?\s*\(([^)]*)\)", re.I)
KEYWORDS = {"CONSTRAINT", "PRIMARY", "FOREIGN", "UNIQUE", "CHECK", "INDEX", "PERIOD"}


def parse(path: Path) -> Table | None:
    sql = strip_comments(path.read_text(encoding="utf-8-sig"))
    m = re.search(r"CREATE\s+TABLE\s+([\w]+\.[\w]+)\s*\(", sql, re.I)
    if not m:
        return None
    start, depth, i = m.end(), 1, m.end()
    while depth:
        depth += {"(": 1, ")": -1}.get(sql[i], 0)
        i += 1
    table = Table(m.group(1))
    by_name: dict[str, Column] = {}
    for part in split_top_level(sql[start : i - 1]):
        first = part.split()[0].upper()
        if first in KEYWORDS:
            if pk := PK_RE.search(part):
                for c in pk.group(1).split(","):
                    if (col := by_name.get(c.strip().split()[0])) is not None:
                        col.pk = True
            if fk := FK_RE.search(part):
                cols = [c.strip() for c in fk.group(1).split(",")]
                table.fks.append((cols, fk.group(2)))
                for c in cols:
                    if (col := by_name.get(c)) is not None and col.fk is None:
                        col.fk = fk.group(2)
            continue
        tm = TYPE_RE.match(part)
        if not tm:
            continue
        name, typ = tm.group(1), re.sub(r"\s+", "", tm.group(2)).upper()
        if typ.startswith("AS"):
            typ = "computed"
        # Only the column's own definition, not a CHECK that may say "IS NOT NULL".
        upper = re.split(r"\bCONSTRAINT\b|\bCHECK\b", part, flags=re.I)[0].upper()
        col = Column(
            name=name,
            type=typ,
            nullable="NOT NULL" not in upper and "PRIMARY KEY" not in upper,
            pk="PRIMARY KEY" in upper,
        )
        if ref := REF_RE.search(part):
            col.fk = ref.group(1)
            table.fks.append(([name], ref.group(1)))
        table.columns.append(col)
        by_name[name] = col
    return table


def load() -> dict[str, Table]:
    tables = {}
    for path in sorted(DB.glob("*/Tables/*.sql")):
        if path.stem.endswith("History") and path.stem not in {
            "BookingStatusHistory",
            "AppointmentStatusHistory",
            "InstitutionStatusHistory",
            "RegistrationRequestStatusHistory",
            "LoginHistory",
            "PasswordHistory",
        }:
            continue  # temporal history tables mirror their table
        if (t := parse(path)) is not None:
            tables[t.full] = t
    return tables


def is_actor_column(name: str) -> bool:
    return bool(re.search(r"(By|ByUserId)$", name))


def edges(tables: dict[str, Table], members: set[str]) -> list[tuple[str, str, bool]]:
    """Distinct (child, parent, mandatory) pairs with the child in `members`."""
    seen: dict[tuple[str, str], bool] = {}
    for t in tables.values():
        if t.full not in members:
            continue
        cols = {c.name: c for c in t.columns}
        for child_cols, parent in t.fks:
            if parent not in tables or all(is_actor_column(c) for c in child_cols):
                continue
            # The tenant key: every tenant table has one, so outside org it gets no line.
            if child_cols == ["InstitutionId"] and parent == "org.Institutions" and t.schema != "org":
                continue
            mandatory = all(c in cols and not cols[c].nullable for c in child_cols)
            key = (t.full, parent)
            seen[key] = seen.get(key, False) or mandatory
    return [(c, p, m) for (c, p), m in seen.items()]


HEADER = """@startuml
!pragma layout smetana
!include ../style.iuml
skinparam linetype ortho
hide circle
hide methods
hide stereotypes
"""

SCHEMA_COLOURS = {
    "auth": "#E6F0EC",
    "org": "#FBF1DF",
    "booking": "#EAF2FA",
    "giving": "#F6E9F2",
    "notify": "#EEF0E3",
    "core": "#F2F2F2",
    "audit": "#F2F2F2",
}


def entity(t: Table, with_columns: bool) -> str:
    colour = SCHEMA_COLOURS.get(t.schema, "#FFFFFF")
    if not with_columns:
        return f'entity "{t.full}" as {t.alias} {colour}\n'
    lines = [f'entity "{t.full}" as {t.alias} {colour} {{']
    keys = [c for c in t.columns if c.pk]
    rest = [c for c in t.columns if not c.pk]
    for c in keys:
        lines.append(f"  * <b>{c.name}</b> : {c.type} <<PK>>")
    lines.append("  --")
    for c in rest:
        mark = "* " if not c.nullable else "  "
        fk = " <<FK>>" if c.fk else ""
        lines.append(f"  {mark}{c.name} : {c.type}{fk}")
    lines.append("}")
    return "\n".join(lines) + "\n"


def stub(full: str) -> str:
    return f'entity "{full}" as {full.replace(".", "_")} #FFFFFF ##[dashed]\n'


def write(name: str, title: str, tables: dict[str, Table], members: set[str], with_columns: bool) -> None:
    rels = edges(tables, members)
    outside = sorted({p for _, p, _ in rels if p not in members})
    body = [HEADER, f"title {title}\n"]
    for schema in sorted({tables[m].schema for m in members}):
        body.append(f'package "{schema}" {{\n')
        for full in sorted(m for m in members if tables[m].schema == schema):
            body.append(entity(tables[full], with_columns))
        body.append("}\n")
    if outside:
        body.append('package "other modules" #FFFFFF {\n')
        body.extend(stub(o) for o in outside)
        body.append("}\n")
    for child, parent, mandatory in sorted(rels):
        if child == parent:
            body.append(f"{tables[child].alias} }}o..o| {tables[child].alias}\n")
            continue
        arrow = "}o--||" if mandatory else "}o..o|"
        body.append(f"{child.replace('.', '_')} {arrow} {parent.replace('.', '_')}\n")
    body.append(
        "legend bottom right\n"
        "  * = NOT NULL    <<PK>> primary key    <<FK>> foreign key\n"
        "  solid line = mandatory FK, dotted = optional FK\n"
        "  CreatedBy / DecidedBy / ...By columns reference auth.Users (no line drawn)\n"
        "  InstitutionId (tenant key, Row-Level Security) references org.Institutions:\n"
        "  drawn only from org tables\n"
        "  Generated from src/Sethika.Database by docs/diagrams/tools/erd_from_sql.py\n"
        "endlegend\n@enduml\n"
    )
    (OUT / f"{name}.puml").write_text("".join(body), encoding="utf-8")
    print(f"{name}: {len(members)} tables, {len(rels)} relationships")


SERVICES = {
    "booking.ServiceTypes",
    "booking.ServiceAppointments",
    "booking.AppointmentLeaders",
    "booking.AppointmentStatusHistory",
    "booking.LeaderTimeBlocks",
    "org.ReligiousLeaders",
    "org.LeaderWeeklyHours",
    "org.LeaderTimeOff",
}
ENGAGEMENT = {"booking.GratitudePosts", "booking.GratitudePostPhotos"}


def write_overview(tables: dict[str, Table], modules: list[tuple[str, str, set[str]]]) -> None:
    """One box per module listing its tables; one line per module pair, labelled with its FK count."""
    owner = {t: key for key, _, members in modules for t in members}
    counts: dict[tuple[str, str], int] = {}
    for child, parent, _ in edges(tables, set(tables)):
        a, b = owner.get(child), owner.get(parent)
        if a and b and a != b:
            counts[(a, b)] = counts.get((a, b), 0) + 1
    body = [HEADER.replace("hide methods\n", ""), "title Sethika — database overview: 6 modules, %d tables\n" % len(tables)]
    body.append("skinparam linetype polyline\nskinparam nodesep 60\nskinparam ranksep 90\n")
    for key, label, members in modules:
        colour = SCHEMA_COLOURS.get(next(iter(members)).split(".")[0], "#FFFFFF")
        body.append(f'entity "{label}" as {key} {colour} {{\n')
        for full in sorted(members):
            body.append(f"  {full}\n")
        body.append("}\n")
    for (a, b), n in sorted(counts.items()):
        body.append(f'{a} --> {b} : {n} FK\n')
    body.append(
        "legend bottom right\n"
        "  arrow = foreign keys from one module's tables to another's\n"
        "  (actor columns such as CreatedBy and the tenant key InstitutionId are not counted)\n"
        "  Each module has its own ERD with every column.\n"
        "  Generated from src/Sethika.Database by docs/diagrams/tools/erd_from_sql.py\n"
        "endlegend\n@enduml\n"
    )
    (OUT / "00_overview.puml").write_text("".join(body), encoding="utf-8")
    print(f"00_overview: {len(modules)} modules, {len(counts)} module links")


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    tables = load()
    every = set(tables)
    print(f"{len(tables)} tables parsed")

    def schema(*names: str) -> set[str]:
        return {t for t in every if tables[t].schema in names}

    modules = [
        ("01_access_rbac", "ERD 1 — Access, RBAC and sessions (auth)", "Access & RBAC", schema("auth")),
        ("02_onboarding_institutions", "ERD 2 — Onboarding and institutions (org)", "Onboarding & institutions", schema("org") - SERVICES),
        ("03_booking", "ERD 3 — Meal slots, booking engine, recurring and memorial (booking)", "Booking", schema("booking") - SERVICES - ENGAGEMENT),
        ("04_religious_services", "ERD 4 — Religious services and leaders (Phase E)", "Religious services", SERVICES & every),
        ("05_giving_engagement", "ERD 5 — Giving, needs, payments, gratitude and notifications", "Giving & engagement", schema("giving", "notify") | (ENGAGEMENT & every)),
        ("06_core_audit", "ERD 6 — Core reference data, outbox and audit (core, audit)", "Core & audit", schema("core", "audit")),
    ]
    assert set().union(*(m for *_, m in modules)) == every, "a table belongs to no module"
    write_overview(tables, [(f"m{name[:2]}", label, members) for name, _, label, members in modules])
    for name, title, _, members in modules:
        write(name, title, tables, members, True)


if __name__ == "__main__":
    main()
