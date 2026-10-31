#!/usr/bin/env python3
"""Merge QA-004 + acceptance users into Keycloak realm import without destroying clientScopes."""
import json
import sys
from copy import deepcopy
from pathlib import Path

repo = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parents[1]
import_path = repo / "docker" / "keycloak" / "import" / "realm-hrmanagement.json"
extensions = [
    repo / "docker" / "keycloak" / "realm-qa004-e2e.json",
    repo / "docker" / "keycloak" / "realm-acceptance.json",
]

if not import_path.exists():
    raise SystemExit(f"Missing {import_path}; run prepare-keycloak-import.ps1 first")

with import_path.open(encoding="utf-8-sig") as f:
    main = json.load(f)

seen = {u.get("username") for u in main.get("users", []) if u.get("username")}
main.setdefault("roles", {}).setdefault("realm", [])
role_names = {r.get("name") for r in main["roles"]["realm"] if r.get("name")}

for ext_path in extensions:
    if not ext_path.exists():
        print(f"skip missing {ext_path}")
        continue
    with ext_path.open(encoding="utf-8") as f:
        ext = json.load(f)
    for role in ext.get("extraRoles", []):
        if role not in role_names:
            main["roles"]["realm"].append({"name": role, "description": "Acceptance extension role"})
            role_names.add(role)
    for user in ext.get("users", []):
        username = user.get("username")
        if not username or username in seen:
            continue
        if not user.get("email"):
            user["email"] = f"{username}@acceptance.local"
        user.setdefault("emailVerified", True)
        # Keycloak 24's user profile requires a first and last name: without them the first login stops on
        # "Update Account Information" (VERIFY_PROFILE) and never returns to the gateway.
        if not user.get("firstName"):
            user["firstName"] = "Acceptance"
        if not user.get("lastName"):
            user["lastName"] = username
        # The gateway asks for offline_access and an imported user gets no default roles: without the role the
        # code exchange fails ("Offline tokens not allowed for the user or client").
        roles = user.get("realmRoles") or []
        if "offline_access" not in roles:
            user["realmRoles"] = [*roles, "offline_access"]
        main.setdefault("users", []).append(deepcopy(user))
        seen.add(username)

with import_path.open("w", encoding="utf-8") as f:
    json.dump(main, f, indent=2, ensure_ascii=False)
    f.write("\n")

print(f"Merged realm extensions into {import_path} ({len(main.get('users', []))} users total)")
