#!/usr/bin/env python3
"""UX-002: add aria-label to icon-only a/button in Index.cshtml + Shared views."""
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1] / "src"

ICON_LABELS = {
    "fa-plus": "إضافة",
    "fa-edit": "تعديل",
    "fa-pen": "تعديل",
    "fa-trash": "حذف",
    "fa-eye": "تفاصيل",
    "fa-search": "بحث",
    "fa-download": "تنزيل",
    "fa-upload": "رفع",
    "fa-print": "طباعة",
    "fa-check": "تأكيد",
    "fa-times": "إلغاء",
    "fa-ban": "تعطيل",
    "fa-unlock": "تفعيل",
    "fa-sync": "تحديث",
    "fa-redo": "إعادة",
    "fa-copy": "نسخ",
    "fa-link": "ربط",
    "fa-file": "ملف",
    "fa-info-circle": "معلومات",
    "fa-cog": "إعدادات",
    "fa-cogs": "إعدادات",
    "fa-user": "مستخدم",
    "fa-users": "مستخدمون",
    "fa-key": "مفتاح",
    "fa-lock": "قفل",
    "fa-save": "حفظ",
    "fa-filter": "تصفية",
    "fa-list": "قائمة",
    "fa-calendar": "تقويم",
    "fa-history": "سجل",
    "fa-envelope": "رسالة",
    "fa-bell": "تنبيه",
    "fa-home": "الرئيسية",
    "fa-bars": "القائمة",
    "fa-expand": "توسيع",
    "fa-building": "جهة",
    "fa-id-card": "بطاقة",
    "fa-money-bill": "مبلغ",
    "fa-file-contract": "عقد",
    "fa-user-minus": "فصل",
    "fa-user-plus": "تعيين",
    "fa-exchange-alt": "نقل",
    "fa-plane": "انتداب",
    "fa-handshake": "إعارة",
    "fa-folder-open": "مجلد",
    "fa-paperclip": "مرفق",
    "fa-clipboard-list": "قائمة",
    "fa-chart-bar": "تقرير",
    "fa-chart-pie": "تقرير",
}

TAG_RE = re.compile(
    r"<(?P<tag>a|button)(?P<pre>[^>]*?)>"
    r"(?P<body>\s*(?:<i[^>]*class=\"[^\"]*(?:fas|far|fab)[^\"]*\"[^>]*>\s*</i>\s*)+)"
    r"\s*</(?P=tag)>",
    re.I | re.S,
)


def pick_label(body: str) -> str:
    classes = " ".join(re.findall(r'class="([^"]+)"', body))
    for key, label in ICON_LABELS.items():
        if key in classes:
            return label
    return "إجراء"


def has_accessible_name(pre: str, body: str) -> bool:
    if re.search(r"aria-label\s*=", pre, re.I):
        return True
    if re.search(r"title\s*=", pre, re.I):
        return True
    text = re.sub(r"<[^>]+>", "", body).strip()
    if text:
        return True
    if "visually-hidden" in body or "sr-only" in body:
        return True
    return False


def process(text: str) -> tuple[str, int]:
    fixed = 0

    def repl(m: re.Match[str]) -> str:
        nonlocal fixed
        pre, body, tag = m.group("pre"), m.group("body"), m.group("tag")
        if not has_accessible_name(pre, body):
            label = pick_label(body)
            pre = f'{pre} aria-label="{label}"'
            fixed += 1
        new_body = re.sub(r"(<i\b)(?![^>]*aria-hidden)", r'\1 aria-hidden="true"', body, flags=re.I)
        return f"<{tag}{pre}>{new_body}</{tag}>"

    return TAG_RE.sub(repl, text), fixed


def main() -> None:
    files = list(ROOT.rglob("**/Index.cshtml"))
    shared = ROOT / "Shared" / "HRManagement.Web.Shared" / "Views"
    if shared.exists():
        files.extend(shared.rglob("*.cshtml"))
    # critical non-index lists
    for extra in [
        "Services/Payroll/Payroll.Web/Views/PayrollReports/MonthlySummary.cshtml",
    ]:
        p = ROOT / extra
        if p.exists():
            files.append(p)

    changed = 0
    total_fixed = 0
    seen: set[Path] = set()
    for f in files:
        if f in seen or "wwwroot" in f.parts:
            continue
        seen.add(f)
        orig = f.read_text(encoding="utf-8")
        text, n = process(orig)
        if text != orig:
            f.write_text(text, encoding="utf-8", newline="\n")
            changed += 1
            total_fixed += n
            print(f"updated {f.relative_to(ROOT.parent)} (+{n})")
    print(f"DONE files={changed} labels={total_fixed}")


if __name__ == "__main__":
    main()
