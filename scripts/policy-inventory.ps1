#Requires -Version 5.1
<#
.SYNOPSIS
  Writes docs/policy-rules-inventory.csv: the static *Rules.cs files of the services, the policy domain each belongs to, and
  whether the policy engine governs that domain (PLAN-49, owner decision D-POL-1).

.DESCRIPTION
  Whether a domain is governed is read from PolicyDomainCatalog.cs, the one source of truth about the engine's domains; this
  script holds only which rules file belongs to which domain. A file of a governed domain is "governed": the engine is asked
  beside it at the domain's call sites (in Shadow in production: it records its decision and changes no outcome). A file of a
  domain out of the engine's scope, or of no policy domain at all, is "local": its rules are the only rules of its subject.
  One file may belong to two domains (LeaveAndAdvanceRules.cs): it gets a row for each.

  A service's files are not all of its domain. In HR.Core a file belongs to Core.Recruitment or to Core.Establishment only
  when its name states an entity the catalog lists for that domain (a requisition, an offer, a committee session, an appeal,
  a hire; an establishment change): the engine is asked about that entity. The other rules of the service (competitions,
  exams, assessments, the recruitment indicators, the establishment gap) have no policy domain: the engine is never asked
  about their subject, so they are local.

  The status column is Arabic, in the file as in the plan. This script stays ASCII: it builds the words from code points.

  -Check writes nothing and exits 1 when the file differs from what this script would write (a new *Rules.cs file, a file
  with no row, a domain that changed its status in the catalog).

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts/policy-inventory.ps1
.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts/policy-inventory.ps1 -Check
#>
param([switch]$Check)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$utf8 = New-Object System.Text.UTF8Encoding($false)
$utf8Bom = New-Object System.Text.UTF8Encoding($true)

function Read-Source([string]$relative) {
    return [System.IO.File]::ReadAllText((Join-Path $root $relative), $utf8)
}

# The Arabic words of the status column, from code points: governed, local, and the header "governed/local".
$governedWord = -join ([char[]](0x0645, 0x062D, 0x0643, 0x0648, 0x0645))
$localWord = -join ([char[]](0x0645, 0x062D, 0x0644, 0x064A))
$statusHeader = "$governedWord/$localWord"

# 1. The catalog: which domains the engine governs.
$constants = @{}
foreach ($m in [regex]::Matches((Read-Source "src/Shared/HRManagement.SharedKernel/Policy/PolicyDomains.cs"), 'public const string (\w+) = "([^"]+)";')) {
    $constants[$m.Groups[1].Value] = $m.Groups[2].Value
}
$catalog = Read-Source "src/Shared/HRManagement.SharedKernel/Policy/PolicyDomainCatalog.cs"
$governed = @{}
foreach ($m in [regex]::Matches($catalog, 'Domain = PolicyDomains\.(\w+),\s*Status = PolicyDomainStatus\.Governed')) { $governed[$constants[$m.Groups[1].Value]] = $true }
$outOfScope = @{}
foreach ($m in [regex]::Matches($catalog, 'OutOfScope\(PolicyDomains\.(\w+),')) { $outOfScope[$constants[$m.Groups[1].Value]] = $true }
if ($governed.Count -eq 0 -or ($governed.Count + $outOfScope.Count) -ne $constants.Count) {
    throw "The catalog could not be read: $($governed.Count) governed and $($outOfScope.Count) out of scope for $($constants.Count) domain constants."
}

# 2. Which rules file belongs to which policy domain: the service folder and a pattern on the file name, the first match wins.
#    An empty domain list: the file has no policy domain (the workflow engine's own rules, a loss report, which goes through
#    the approval contract and asks no policy).
$map = @(
    @{ Service = "AdminLogistics"; Pattern = '^AssetLostRules$'; Domains = @() },
    @{ Service = "AdminLogistics"; Pattern = '^Asset'; Domains = @("Assets.Custody") },
    @{ Service = "AdminLogistics"; Pattern = '^Supply'; Domains = @("Assets.Procurement") },
    @{ Service = "Compliance"; Pattern = '^Disciplinary'; Domains = @("Compliance.Disciplinary") },
    @{ Service = "DevAnalytics"; Pattern = '.'; Domains = @("Analytics.ETL") },
    @{ Service = "HRContracts"; Pattern = '^LeaveAndAdvanceRules$'; Domains = @("Contracts.Leave", "Contracts.Advance") },
    @{ Service = "HRContracts"; Pattern = '^Contract'; Domains = @("Contracts.Contract") },
    @{ Service = "HRContracts"; Pattern = '^Hire'; Domains = @("Contracts.Hire") },
    @{ Service = "HRContracts"; Pattern = '^Separation'; Domains = @("Contracts.Separation") },
    @{ Service = "HRCore"; Pattern = '^EstablishmentChange'; Domains = @("Core.Establishment") },
    @{ Service = "HRCore"; Pattern = '^(EmploymentStatus|MedicalEmploymentEffect|ServiceRegime|ShiftAllowance|SuccessionReadiness)'; Domains = @("Core.Employment") },
    @{ Service = "HRCore"; Pattern = '^(Requisition|Offer|SelectionCommittee|RecruitmentAppeal|Hire)'; Domains = @("Core.Recruitment") },
    @{ Service = "HRCore"; Pattern = '.'; Domains = @() },
    @{ Service = "HRHealth"; Pattern = '.'; Domains = @("Health.Insurance") },
    @{ Service = "HRMovements"; Pattern = '^Transfer'; Domains = @("Movements.Transfer") },
    @{ Service = "HRMovements"; Pattern = '.'; Domains = @("Movements.Workforce") },
    @{ Service = "HRTraining"; Pattern = '.'; Domains = @("Training.Learning") },
    @{ Service = "IDCard"; Pattern = '.'; Domains = @("IDCard.Lifecycle") },
    @{ Service = "ITInfrastructure"; Pattern = '.'; Domains = @("IT.Tickets") },
    @{ Service = "Payroll"; Pattern = '.'; Domains = @("Payroll.Period") },
    @{ Service = "RecordsArchive"; Pattern = '.'; Domains = @("Archive.Retention") }
)

# 3. The files, in a fixed order (the same on Windows and on Linux).
$servicesRoot = Join-Path (Join-Path $root "src") "Services"
$files = @(Get-ChildItem -Path $servicesRoot -Recurse -Filter "*Rules.cs" -File |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
    ForEach-Object { $_.FullName.Substring($root.Length + 1).Replace('\', '/') })
[System.Array]::Sort($files, [System.StringComparer]::Ordinal)

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('"File","Service","PolicyDomain","' + $statusHeader + '"')
$unmapped = New-Object System.Collections.Generic.List[string]
foreach ($file in $files) {
    $parts = $file.Split('/')
    $service = $parts[2]
    $name = [System.IO.Path]::GetFileNameWithoutExtension($parts[$parts.Length - 1])
    $entry = $null
    foreach ($candidate in $map) {
        if ($candidate.Service -ceq $service -and $name -cmatch $candidate.Pattern) { $entry = $candidate; break }
    }
    if ($null -eq $entry) { $unmapped.Add($file); continue }
    $domains = @($entry.Domains)
    if ($domains.Count -eq 0) {
        $lines.Add(('"{0}","{1}","","{2}"' -f $file, $service, $localWord))
        continue
    }
    foreach ($domain in $domains) {
        if (-not $governed.ContainsKey($domain) -and -not $outOfScope.ContainsKey($domain)) { throw "$file is mapped to '$domain', which is not a domain of the catalog." }
        $status = if ($governed.ContainsKey($domain)) { $governedWord } else { $localWord }
        $lines.Add(('"{0}","{1}","{2}","{3}"' -f $file, $service, $domain, $status))
    }
}
if ($unmapped.Count -gt 0) {
    throw ("No mapping for: " + ($unmapped -join ", ") + ". Add the service to the map of this script.")
}

$out = Join-Path (Join-Path $root "docs") "policy-rules-inventory.csv"
$text = ($lines -join "`n") + "`n"
if ($Check) {
    $current = ""
    if (Test-Path -LiteralPath $out) { $current = ([System.IO.File]::ReadAllText($out, $utf8)).Replace("`r`n", "`n").TrimStart([char]0xFEFF) }
    if ($current -cne $text) {
        Write-Output "policy-inventory: FAIL (docs/policy-rules-inventory.csv is not what this script writes; run it without -Check)"
        exit 1
    }
    Write-Output "policy-inventory: PASS ($($lines.Count - 1) rows, $($files.Count) files)"
    exit 0
}

# With a byte order mark, as before: a spreadsheet then reads the Arabic column as UTF-8.
[System.IO.File]::WriteAllText($out, $text, $utf8Bom)
Write-Output "Wrote $($lines.Count - 1) rows for $($files.Count) files to docs/policy-rules-inventory.csv"
exit 0
