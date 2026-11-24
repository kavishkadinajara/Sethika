"""Generate the class-diagram PlantUML sources from the C# code.

Usage (from the repo root):
    python -I docs/diagrams/tools/class_from_cs.py

Reads src/Sethika.Domain, Sethika.Application and Sethika.Infrastructure, and writes
docs/diagrams/src/class/*.puml. Each diagram names the classes it shows (DIAGRAMS below); members come
from the code, so a diagram cannot list a property or method that does not exist.

- Navigation properties (to another class in the same diagram) become associations, not attributes.
- A primary-constructor parameter whose type is in the diagram becomes a dependency (handler -> port).
- A class implementing an interface in the diagram gets a realisation arrow.
- Static classes made only of string constants are drawn as <<constants>> (the readable status values
  the database CHECKs against).
"""
from __future__ import annotations

import re
from dataclasses import dataclass, field
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
SRC = ROOT / "src"
OUT = ROOT / "docs" / "diagrams" / "src" / "class"

MODIFIERS = {
    "public", "internal", "private", "protected", "static", "virtual", "override", "async", "required",
    "sealed", "abstract", "readonly", "new", "partial", "unsafe", "extern", "const",
}


@dataclass
class Member:
    kind: str  # prop | method | const
    name: str
    type: str = ""
    params: list[tuple[str, str]] = field(default_factory=list)
    static: bool = False
    private_set: bool = False


@dataclass
class TypeDef:
    name: str
    kind: str  # class | record | interface | struct
    static: bool
    bases: list[str]
    ctor_params: list[tuple[str, str]]
    members: list[Member]
    namespace: str
    layer: str


def clean(code: str) -> str:
    code = re.sub(r"/\*.*?\*/", "", code, flags=re.S)
    code = re.sub(r"//[^\n]*", "", code)
    code = re.sub(r'"""[\s\S]*?"""', '""', code)
    code = re.sub(r'\$?@?"(?:[^"\\\n]|\\.|"")*"', '""', code)
    code = re.sub(r"'(?:[^'\\]|\\.)'", "''", code)
    return code


def split_params(text: str) -> list[tuple[str, str]]:
    out, depth, cur = [], 0, ""
    for ch in text:
        depth += {"<": 1, "(": 1, "[": 1, ">": -1, ")": -1, "]": -1}.get(ch, 0)
        if ch == "," and depth == 0:
            out.append(cur)
            cur = ""
        else:
            cur += ch
    if cur.strip():
        out.append(cur)
    result = []
    for p in out:
        p = re.sub(r"\[[^\]]*\]", "", p).split("=")[0].strip()
        p = re.sub(r"^(this|params|ref|out|in)\s+", "", p)
        if " " in p:
            t, n = p.rsplit(" ", 1)
            result.append((t.strip(), n.strip()))
    return result


def statements(body: str):
    """Yield (header, ended_by) for each member at the top level of a type body."""
    depth, cur, i = 0, "", 0
    while i < len(body):
        ch = body[i]
        if depth == 0 and ch == "{":
            yield cur.strip(), "{"
            cur, d = "", 1
            i += 1
            while d and i < len(body):
                d += {"{": 1, "}": -1}.get(body[i], 0)
                i += 1
            # an initialiser after an auto-property: "{ get; set; } = ...;"
            rest = re.match(r"\s*=[^;]*;", body[i:])
            if rest:
                i += rest.end()
            continue
        if depth == 0 and ch == ";":
            yield cur.strip(), ";"
            cur = ""
        else:
            depth += {"(": 1, ")": -1, "[": 1, "]": -1}.get(ch, 0)
            cur += ch
        i += 1


def parse_member(header: str, interface: bool) -> Member | None:
    header = re.sub(r"^\s*(\[[^\]]*\]\s*)+", "", header)
    if not header or re.match(r"(public\s+|internal\s+)?(static\s+|sealed\s+|abstract\s+|partial\s+|readonly\s+)*(class|record|struct|interface|enum)\b", header):
        return None
    words = header.split()
    if not interface and words[0] != "public":
        return None
    static = "static" in words[:4]
    is_const = "const" in words[:4]
    head = header.split("=>")[0] if "=>" in header else header
    head = head.split("=")[0] if is_const else head
    while head.split() and head.split()[0] in MODIFIERS:
        head = head.split(None, 1)[1] if len(head.split()) > 1 else ""
    head = head.strip()
    if not head:
        return None
    m = re.match(r"^(.+?)\s+(\w+)\s*(<[^>]*>)?\s*\((.*)\)\s*(where .*)?$", head, re.S)
    if m:
        return Member("method", m.group(2), m.group(1).strip(), split_params(m.group(4)), static)
    if re.match(r"^\w+\s*\(", head):
        return None  # constructor
    m = re.match(r"^(.+?)\s+(\w+)$", head, re.S)
    if not m:
        return None
    kind = "const" if is_const else "prop"
    return Member(kind, m.group(2), re.sub(r"\s+", " ", m.group(1)), static=static)


TYPE_RE = re.compile(
    r"(?:public|internal)\s+((?:static\s+|sealed\s+|abstract\s+|partial\s+|readonly\s+)*)"
    r"(class|record struct|record|interface|struct)\s+(\w+)(<[^>{(]*>)?\s*(\((?:[^()]|\([^()]*\))*\))?\s*(:\s*[^{;]+)?\s*([{;])",
    re.S,
)


def parse_file(path: Path, layer: str) -> list[TypeDef]:
    code = clean(path.read_text(encoding="utf-8-sig"))
    ns = (re.search(r"namespace\s+([\w.]+)", code) or [None, ""])[1]
    types = []
    for m in TYPE_RE.finditer(code):
        mods, kind, name = m.group(1) or "", m.group(2), m.group(3)
        ctor = split_params(m.group(5)[1:-1]) if m.group(5) else []
        bases = []
        if m.group(6):
            bases = [re.sub(r"\(.*", "", b).strip() for b in split_params_bases(m.group(6)[1:])]
        members: list[Member] = []
        if m.group(7) == "{":
            start, d, i = m.end(), 1, m.end()
            while d and i < len(code):
                d += {"{": 1, "}": -1}.get(code[i], 0)
                i += 1
            body = code[start : i - 1]
            for header, ended in statements(body):
                mem = parse_member(header, kind == "interface")
                if mem is None:
                    continue
                if mem.kind == "prop":
                    mem.private_set = bool(re.search(r"(private|init|protected)\s+set", body)) and False
                members.append(mem)
            # private setters: read them per property from the source
            for mem in members:
                if mem.kind == "prop" and re.search(rf"\b{mem.name}\s*\{{\s*get;\s*(private|internal)\s+set;", body):
                    mem.private_set = True
        if kind.startswith("record") and ctor:
            members = [Member("prop", n, t) for t, n in ctor] + members
            ctor = []
        types.append(TypeDef(name, kind, "static" in mods, bases, ctor, members, ns, layer))
    return types


def split_params_bases(text: str) -> list[str]:
    out, depth, cur = [], 0, ""
    for ch in text:
        depth += {"<": 1, "(": 1, ">": -1, ")": -1}.get(ch, 0)
        if ch == "," and depth == 0:
            out.append(cur)
            cur = ""
        else:
            cur += ch
    return [c for c in (out + [cur]) if c.strip() and not c.strip().startswith("where")]


def load() -> dict[str, TypeDef]:
    found: dict[str, TypeDef] = {}
    for layer in ("Domain", "Application", "Infrastructure"):
        for path in sorted((SRC / f"Sethika.{layer}").rglob("*.cs")):
            if "\\obj\\" in str(path) or "/obj/" in str(path) or "\\bin\\" in str(path):
                continue
            for t in parse_file(path, layer):
                found.setdefault(t.name, t)
    return found


COLLECTION = re.compile(r"^(?:I?(?:Collection|List|ReadOnlyCollection|ReadOnlyList|Enumerable)|HashSet)<(\w+)>$")


def target(type_text: str, names: set[str]) -> tuple[str, str] | None:
    t = type_text.rstrip("?").strip()
    if t in names:
        return t, "1" if not type_text.endswith("?") else "0..1"
    if (m := COLLECTION.match(t)) and m.group(1) in names:
        return m.group(1), "*"
    return None


def short(t: str) -> str:
    return t.replace("System.", "").replace("Task<", "Task<")


ID_ALIASES = {
    "UserId": "User",
    "DonorUserId": "User",
    "ApplicantUserId": "User",
    "RequestId": "RegistrationRequest",
    "RegistrationRequestId": "RegistrationRequest",
    "LeaderId": "ReligiousLeader",
    "AppointmentId": "ServiceAppointment",
    "MerchantId": "PaymentMerchant",
    "PaymentMerchantId": "PaymentMerchant",
}


# References that are unique in the database, so the referencing side is at most one.
ONE_TO_ONE = {("Institution", "RegistrationRequestId")}


def render_type(t: TypeDef, names: set[str], hide: set[str], max_methods: int, methods_only: bool = False) -> str:
    consts = [m for m in t.members if m.kind == "const"]
    others = [m for m in t.members if m.kind != "const"]
    if t.static and consts and not [m for m in others if m.kind == "method"]:
        lines = [f"enum {t.name} <<constants>> {{"] + [f"  {c.name}" for c in consts] + ["}"]
        return "\n".join(lines) + "\n"
    if t.kind == "interface":
        head = f"interface {t.name}"
    elif t.static:
        head = f"class {t.name} <<static>>"
    elif t.kind.startswith("record"):
        head = f"class {t.name} <<record>>"
    else:
        head = f"class {t.name}"
    lines = [head + " {"]
    if not methods_only:
        lines.extend(f"  {{static}} +{c.name} : const" for c in consts if c.name not in hide)
    for m in others:
        if methods_only or m.kind != "prop" or m.name in hide or target(m.type, names):
            continue
        ro = " {readOnly}" if m.private_set else ""
        lines.append(f"  {'{static} ' if m.static else ''}+{m.name} : {short(m.type)}{ro}")
    methods = [m for m in others if m.kind == "method" and m.name not in hide]
    if methods:
        lines.append("  --")
    for m in methods[:max_methods]:
        params = ", ".join(n for t_, n in m.params if t_ != "CancellationToken")
        lines.append(f"  {'{static} ' if m.static else ''}+{m.name}({params}) : {short(m.type)}")
    if len(methods) > max_methods:
        lines.append(f"  .. {len(methods) - max_methods} more ..")
    lines.append("}")
    return "\n".join(lines) + "\n"


HEADER = """@startuml
!pragma layout smetana
!include ../style.iuml
hide empty members
skinparam classAttributeIconSize 0
skinparam nodesep 40
skinparam ranksep 60
skinparam class {
  BackgroundColor<<static>> #FFF8EA
  BackgroundColor<<constants>> #F7F4EC
  BackgroundColor<<record>> #F2F7F4
}
"""


def diagram(name: str, title: str, groups: list[tuple[str, list[str]]], all_types: dict[str, TypeDef],
            hide: set[str] = frozenset(), max_methods: int = 12, note: str = "", methods_only: bool = False, left_to_right: bool = False) -> None:
    wanted = [n for _, ns in groups for n in ns]
    missing = [n for n in wanted if n not in all_types]
    if missing:
        raise SystemExit(f"{name}: not found in the code: {missing}")
    names = set(wanted)
    body = [HEADER, f"title {title}\n"]
    if left_to_right:
        body.append("left to right direction\n")
    for label, ns in groups:
        body.append(f'package "{label}" {{\n')
        body.extend(render_type(all_types[n], names, set(hide), max_methods, methods_only) for n in ns)
        body.append("}\n")

    links: dict[frozenset, tuple[str, str, str, str]] = {}
    extra = []
    for n in wanted:
        t = all_types[n]
        for m in t.members:
            if m.kind != "prop":
                continue
            hit = target(m.type, names)
            if hit is None and m.name.endswith("Id"):
                # A foreign key by id (EF maps most relations this way): RecurringBookingId -> RecurringBooking.
                ref = ID_ALIASES.get(m.name, m.name[:-2])
                if ref in names and ref != n:
                    hit = (ref, "0..1" if m.type.endswith("?") else "1")
                    if (n, m.name) in ONE_TO_ONE:
                        links[frozenset((n, ref))] = (n, "0..1", ref, "1")
                        continue
            if hit is None or hit[0] == n:
                continue
            other, mult = hit
            key = frozenset((n, other))
            # Keep one line per pair: "1" from the one side, "*" from the many side.
            a, am, b, bm = links.get(key, (n, "", other, ""))
            if a == n:
                bm = mult if mult == "*" or not bm else bm
            else:
                am = mult if mult == "*" or not am else am
            links[key] = (a, am, b, bm)
        for b in t.bases:
            base = re.sub(r"<.*", "", b).strip()
            if base in names:
                # Written base-first so the layout ranks the implementation after its interface.
                arrow = "<|.." if all_types[base].kind == "interface" else "<|--"
                extra.append(f"{base} {arrow} {n}\n")
        for ptype, _ in t.ctor_params:
            many = COLLECTION.match(ptype.strip())
            p = many.group(1) if many else re.sub(r"<.*|\?", "", ptype).strip()
            if p in names and p != n:
                extra.append(f"{n} ..> {p} : {'uses all' if many else 'uses'}\n")
    for a, am, b, bm in links.values():
        # The side holding the reference is "many" unless the other side said otherwise.
        am = am or "*"
        bm = bm or "1"
        body.append(f'{a} "{am}" -- "{bm}" {b}\n')
    body.extend(sorted(set(extra)))
    if note:
        body.append(f"legend bottom right\n{note}\nendlegend\n")
    body.append("@enduml\n")
    (OUT / f"{name}.puml").write_text("".join(body), encoding="utf-8")
    print(f"{name}: {len(wanted)} types, {len(links)} associations")


LEGEND = (
    "  +Name : Type {readOnly} = changed only through the class's own methods\n"
    "  <<static>> = rules with no state    <<constants>> = readable status values (VARCHAR + CHECK)\n"
    "  Generated from the C# code by docs/diagrams/tools/class_from_cs.py"
)
HIDE = {"RowVersion", "ToString", "Equals", "GetHashCode"}


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    types = load()
    print(f"{len(types)} types parsed")

    diagram(
        "01_domain_bookings",
        "Class diagram 1 — Booking domain (Sethika.Domain.Bookings)",
        [
            ("Meal slots", ["MealSlot", "MealSlotSeason", "BlockedDate", "SlotInstance", "MealSlotRules", "SlotInstanceStatus"]),
            ("Bookings", ["Booking", "BookingStatusChange", "BookingContributor", "BookingStatus", "InstitutionDonor"]),
            ("Recurring, standing and memorial", ["RecurringBooking", "StandingSponsorship", "StandingDayChange", "MemorialPlan", "MemorialRule", "MemorialObservances"]),
        ],
        types,
        HIDE,
        note=LEGEND,
    )
    diagram(
        "02_domain_onboarding_access",
        "Class diagram 2 — Onboarding, institutions and access domain",
        [
            ("Onboarding", ["RegistrationRequest", "RegistrationRequestDocument", "RegistrationOfficeBearer", "RegistrationClarification", "RegistrationChecklistEntry", "RegistrationStatusChange", "RegistrationStatus"]),
            ("Institutions", ["Institution", "InstitutionMember", "InstitutionDocument", "InstitutionStatusChange", "StaffInvitation"]),
            ("Access (RBAC)", ["User", "UserRole", "Role", "RolePermission", "RoleLevel", "Permission", "RefreshToken"]),
        ],
        types,
        HIDE,
        note=LEGEND,
    )
    diagram(
        "03_domain_giving_services",
        "Class diagram 3 — Giving and religious services domain",
        [
            ("Giving", ["Need", "Donation", "Pledge", "OnlinePayment", "PaymentMerchant", "PayHereSignature", "NeedStatus", "PledgeStatus", "OnlinePaymentStatus"]),
            ("Religious services (Phase E)", ["ServiceType", "ServiceAppointment", "AppointmentLeader", "AppointmentStatusChange", "ReligiousLeader", "LeaderWeeklyHours", "LeaderTimeOff", "LeaderTimeBlock", "ServiceRules", "AppointmentStatus"]),
        ],
        types,
        HIDE,
        note=LEGEND,
    )
    diagram(
        "04_application_ports",
        "Class diagram 4 — Use-case handlers, ports (Application) and adapters (Infrastructure)",
        [
            ("Application layer: use-case handlers", ["BookMealHandler", "SessionHandler", "ApproveRegistrationHandler"]),
            ("Application layer: ports (interfaces)", ["ISethikaDbContext", "ISlotLocks", "ISlotInventory", "INumberSeries", "IRegistrationLocks", "IInstitutionModules", "ITokenService"]),
            ("Infrastructure layer: adapters", ["RedisSlotLocks", "SqlSlotInventory", "SqlNumberSeries", "SqlRegistrationLocks", "SqlInstitutionModules", "JwtTokenService", "SethikaDbContext"]),
        ],
        types,
        HIDE | {"Database", "ChangeTracker", "SaveChanges", "KeyFor"},
        max_methods=6,
        methods_only=True,
        left_to_right=True,
        note="  Handlers depend only on interfaces (ports) declared in Sethika.Application;\n"
        "  Sethika.Infrastructure implements them (dependency inversion).\n"
        "  Generated from the C# code by docs/diagrams/tools/class_from_cs.py",
    )


if __name__ == "__main__":
    main()
