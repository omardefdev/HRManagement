#Requires -Version 5.1
<#
.SYNOPSIS
  PLAN-42 (P-11): the locks two plan sessions use on this machine. One hrm-acceptance stack, one demo server and the Docker volumes are
  shared by every clone, so a session takes the lock before it touches one. ASCII only, Windows PowerShell 5.1 and PowerShell 7.
  docs/PARALLEL_PLANS.md is the protocol.

.DESCRIPTION
  A lock is a JSON file in %USERPROFILE%\.hrm-locks (-LockDir elsewhere): plan, clone path, purpose, acquired time, expiry.
    Acquire  exit 0 when taken; exit 1 and the holder when it is held and not expired. An expired lock is taken over, and the output
             says so: the session records that note in its progress file.
    Release  exit 0 when freed or free already; exit 1 when another plan holds it.
    Renew    exit 0 when this plan's lock got a new expiry; exit 1 otherwise.
    Status   prints the holder or "free"; always exit 0.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts/parallel/hrm-lock.ps1 -Action Acquire -Resource acceptance-stack -Plan PLAN-43 -Purpose "AC-43 on prod-like"
#>
param(
    [Parameter(Mandatory)][ValidateSet("Acquire", "Release", "Renew", "Status")][string]$Action,
    [Parameter(Mandatory)][ValidateSet("acceptance-stack", "demo-server")][string]$Resource,
    [string]$Plan = "",
    [string]$Purpose = "",
    [string]$ClonePath = "",
    [double]$Hours = 4,
    [string]$LockDir = "",
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($LockDir)) { $LockDir = Join-Path $env:USERPROFILE ".hrm-locks" }
if ([string]::IsNullOrWhiteSpace($RepoRoot)) { $RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot) }
if ([string]::IsNullOrWhiteSpace($ClonePath)) { $ClonePath = $RepoRoot }
$path = Join-Path $LockDir "$Resource.lock"
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$invariant = [System.Globalization.CultureInfo]::InvariantCulture

function Read-Lock {
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    return [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
}

function Test-Expired($Lock) {
    $expires = [datetime]::Parse([string]$Lock.expiresUtc, $invariant, [System.Globalization.DateTimeStyles]::RoundtripKind)
    return $expires.ToUniversalTime() -lt [datetime]::UtcNow
}

function Format-Lock($Lock) {
    return "plan $($Lock.plan), clone $($Lock.clonePath), purpose '$($Lock.purpose)', acquired $($Lock.acquiredUtc), expires $($Lock.expiresUtc)"
}

function New-LockText {
    $now = [datetime]::UtcNow
    $lock = [ordered]@{
        plan        = $Plan
        clonePath   = $ClonePath
        purpose     = $Purpose
        acquiredUtc = $now.ToString("o", $invariant)
        expiresUtc  = $now.AddHours($Hours).ToString("o", $invariant)
    }
    return ($lock | ConvertTo-Json) + "`n"
}

# CreateNew fails when the file exists, so two sessions cannot both win.
function Write-NewLock {
    New-Item -ItemType Directory -Force -Path $LockDir | Out-Null
    $bytes = $utf8NoBom.GetBytes((New-LockText))
    $stream = [System.IO.File]::Open($path, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    try { $stream.Write($bytes, 0, $bytes.Length) }
    finally { $stream.Dispose() }
}

if ($Action -ne "Status" -and [string]::IsNullOrWhiteSpace($Plan)) {
    Write-Host "hrm-lock: -Plan is required for $Action"
    exit 2
}

$lock = Read-Lock
switch ($Action) {
    "Status" {
        if ($null -eq $lock) { Write-Host "hrm-lock: $Resource is free" }
        elseif (Test-Expired $lock) { Write-Host "hrm-lock: $Resource is held but expired: $(Format-Lock $lock)" }
        else { Write-Host "hrm-lock: $Resource is held: $(Format-Lock $lock)" }
        exit 0
    }
    "Acquire" {
        if ($null -ne $lock) {
            if ([string]$lock.plan -eq $Plan -and -not (Test-Expired $lock)) {
                Write-Host "hrm-lock: $Resource is already held by $Plan"
                exit 0
            }
            if (-not (Test-Expired $lock)) {
                Write-Host "hrm-lock: $Resource is held: $(Format-Lock $lock)"
                exit 1
            }
            Write-Host "hrm-lock: taking over an expired lock: $(Format-Lock $lock). Record this in the progress file."
            Remove-Item -LiteralPath $path -Force
        }
        try { Write-NewLock }
        catch [System.IO.IOException] {
            Write-Host "hrm-lock: $Resource was taken a moment ago: $(Format-Lock (Read-Lock))"
            exit 1
        }
        Write-Host "hrm-lock: $Resource acquired by $Plan for $Hours hours"
        exit 0
    }
    "Renew" {
        if ($null -eq $lock -or [string]$lock.plan -ne $Plan) {
            Write-Host "hrm-lock: $Plan does not hold $Resource"
            exit 1
        }
        [System.IO.File]::WriteAllText($path, (New-LockText), $utf8NoBom)
        Write-Host "hrm-lock: $Resource renewed by $Plan for $Hours hours"
        exit 0
    }
    "Release" {
        if ($null -eq $lock) {
            Write-Host "hrm-lock: $Resource is free"
            exit 0
        }
        if ([string]$lock.plan -ne $Plan) {
            Write-Host "hrm-lock: $Resource is held by another plan: $(Format-Lock $lock)"
            exit 1
        }
        Remove-Item -LiteralPath $path -Force
        Write-Host "hrm-lock: $Resource released by $Plan"
        exit 0
    }
}
