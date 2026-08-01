#!/usr/bin/env python3
"""UX-002-D: ensure critical Index tables sit in .table-responsive."""
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

CRITICAL = [
    "src/Services/HRCore/HR.Core.Web/Views/Employees/Index.cshtml",
    "src/Services/HRCore/HR.Core.Web/Views/OrgUnits/Index.cshtml",
    "src/Services/HRCore/HR.Core.Web/Views/Documents/Index.cshtml",
    "src/Services/HRContracts/HR.Contracts.Web/Views/Contracts/Index.cshtml",
    "src/Services/HRContracts/HR.Contracts.Web/Views/LeavePolicies/Index.cshtml",
    "src/Services/Payroll/Payroll.Web/Views/CompensationTypes/Index.cshtml",
    "src/Services/Payroll/Payroll.Web/Views/PayGrades/Index.cshtml",
    "src/Services/AuthDirectory/Auth.Directory.Web/Views/AuthUsers/Index.cshtml",
]


def wrap_tables(text: str) -> str:
    if "table-responsive" in text:
        # still wrap bare tables not already inside responsive
        pass

    def repl(m: re.Match[str]) -> str:
        before = text[max(0, m.start() - 80) : m.start()]
        if "table-responsive" in before:
            return m.group(0)
        return f'<div class="table-responsive">\n{m.group(0)}\n</div>'

    # Match opening table tags with class containing table
    return re.sub(
        r"<table\b[^>]*class=\"[^\"]*\btable\b[^\"]*\"[^>]*>.*?</table>",
        repl,
        text,
        flags=re.I | re.S,
    )


def main() -> None:
    for rel in CRITICAL:
        path = ROOT / rel
        if not path.exists():
            print("missing", rel)
            continue
        orig = path.read_text(encoding="utf-8")
        text = wrap_tables(orig)
        if text != orig:
            path.write_text(text, encoding="utf-8", newline="\n")
            print("wrapped", rel)
        else:
            print("ok", rel)


if __name__ == "__main__":
    main()
