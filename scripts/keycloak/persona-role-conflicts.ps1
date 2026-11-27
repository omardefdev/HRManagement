#Requires -Version 5.1
# PLAN-58 (P-9, D-PAY-20): the realm import refuses a persona whose roles hold a conflicting pair of the separation-of-duties matrix
# (SodMatrixDefaults.CreateDefaultConflicts), unless the persona carries "sodConflictFixture": true (the one test persona that proves
# the refusal). Dot-sourced by acceptance-personas.ps1; run alone with -SelfTest to prove the guard on planted personas.
param([switch]$SelfTest)

# The conflicting pairs, as the matrix names them (kept in step by Pay058RoleConflictTests).
$script:PersonaConflictingRolePairs = @(
    @("payroll-processor", "payroll-approver"),
    @("payroll-approver", "payroll-poster"),
    @("payroll-processor", "payroll-poster"),
    @("hr-core", "hr-management-approver")
)

function Get-PersonaRoleConflicts {
    param([Parameter(Mandatory)]$Personas)
    $found = New-Object System.Collections.Generic.List[string]
    foreach ($persona in @($Personas)) {
        $roles = @($persona.roles | ForEach-Object { [string]$_ })
        $fixture = ($null -ne $persona.sodConflictFixture) -and [bool]$persona.sodConflictFixture
        foreach ($pair in $script:PersonaConflictingRolePairs) {
            if (($roles -contains $pair[0]) -and ($roles -contains $pair[1]) -and -not $fixture) {
                $found.Add(("{0}: {1} + {2}" -f [string]$persona.id, $pair[0], $pair[1]))
            }
        }
    }
    return , $found.ToArray()
}

function Assert-NoPersonaRoleConflicts {
    param([Parameter(Mandatory)]$Personas)
    $conflicts = Get-PersonaRoleConflicts -Personas $Personas
    if ($conflicts.Count -gt 0) {
        throw ("Acceptance personas hold conflicting roles (separation of duties, PLAN-58); fix their roles or mark the one test fixture " +
            "with sodConflictFixture: " + ($conflicts -join "; "))
    }
}

if ($SelfTest) {
    $ok = $true
    $clean = @([pscustomobject]@{ id = "a-processor"; roles = @("payroll", "payroll-processor") })
    $planted = @([pscustomobject]@{ id = "a-both"; roles = @("payroll", "payroll-processor", "payroll-approver") })
    $fixture = @([pscustomobject]@{ id = "a-multirole"; roles = @("payroll-processor", "payroll-approver", "payroll-poster"); sodConflictFixture = $true })
    $hr = @([pscustomobject]@{ id = "a-hr"; roles = @("hr-core", "hr-management-approver") })
    if ((Get-PersonaRoleConflicts -Personas $clean).Count -ne 0) { Write-Host "FAIL: a clean persona was refused"; $ok = $false }
    if ((Get-PersonaRoleConflicts -Personas $planted).Count -ne 1) { Write-Host "FAIL: the planted processor+approver passed"; $ok = $false }
    if ((Get-PersonaRoleConflicts -Personas $fixture).Count -ne 0) { Write-Host "FAIL: the marked fixture was refused"; $ok = $false }
    if ((Get-PersonaRoleConflicts -Personas $hr).Count -ne 1) { Write-Host "FAIL: the planted hr-core+approver passed"; $ok = $false }
    $threw = $false
    try { Assert-NoPersonaRoleConflicts -Personas $planted } catch { $threw = $true }
    if (-not $threw) { Write-Host "FAIL: the import did not stop on a planted conflict"; $ok = $false }
    if ($ok) { Write-Host "persona-role-conflicts self-test: PASS"; exit 0 }
    Write-Host "persona-role-conflicts self-test: FAIL"
    exit 1
}
