# REL-004: compare OpenAPI 3.x specs and flag breaking changes.
# Works on file pairs (baseline vs candidate). Pure PowerShell — no external diff tool required.
#
# Usage:
#   powershell -File scripts/rel004-openapi-diff.ps1 `
#     -Baseline docs/rel-compat/openapi-baseline/hr-core-v1.openapi.json `
#     -Candidate path/to/candidate.json
#
# Exit 0 = no breaking changes (expand-only or identical)
# Exit 1 = breaking changes detected
# Exit 2 = usage / parse error

param(
    [Parameter(Mandatory = $true)]
    [string]$Baseline,
    [Parameter(Mandatory = $true)]
    [string]$Candidate,
    [string]$RepoRoot = "",
    [switch]$AllowDeprecatedRemoval,
    [switch]$JsonOut,
    [string]$OutFile = ""
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Resolve-RepoPath([string]$rel) {
    if ([IO.Path]::IsPathRooted($rel)) { return $rel }
    return (Join-Path $RepoRoot ($rel -replace '/', [IO.Path]::DirectorySeparatorChar))
}

function Read-OpenApi([string]$path) {
    $full = Resolve-RepoPath $path
    if (-not (Test-Path $full)) { throw "file not found: $full" }
    return (Get-Content -Raw -Path $full | ConvertFrom-Json)
}

function Get-SchemaProperties($schema) {
    $props = @{}
    if ($null -eq $schema) { return $props }
    if ($schema.PSObject.Properties.Name -contains 'properties') {
        foreach ($p in $schema.properties.PSObject.Properties) {
            $props[$p.Name] = $p.Value
        }
    }
    return $props
}

function Get-RequiredSet($schema) {
    $set = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    if ($null -eq $schema) { return $set }
    if ($schema.PSObject.Properties.Name -contains 'required') {
        foreach ($r in @($schema.required)) { [void]$set.Add([string]$r) }
    }
    return $set
}

function Get-PrimaryType($schema) {
    if ($null -eq $schema) { return $null }
    if ($schema.PSObject.Properties.Name -contains 'type') { return [string]$schema.type }
    if ($schema.PSObject.Properties.Name -contains '$ref') { return [string]$schema.'$ref' }
    return $null
}

function Compare-Schemas([string]$context, $baseSchema, $candSchema, [ref]$breaking, [ref]$warnings) {
    if ($null -eq $baseSchema -and $null -ne $candSchema) { return }
    if ($null -ne $baseSchema -and $null -eq $candSchema) {
        $breaking.Value += "BREAKING: schema removed at $context"
        return
    }

    $baseReq = Get-RequiredSet $baseSchema
    $candReq = Get-RequiredSet $candSchema
    foreach ($req in $baseReq) {
        if (-not $candReq.Contains($req)) {
            $baseProps = Get-SchemaProperties $baseSchema
            $prop = $baseProps[$req]
            $wasDeprecated = $false
            if ($null -ne $prop -and ($prop.PSObject.Properties.Name -contains 'deprecated')) {
                $wasDeprecated = [bool]$prop.deprecated
            }
            if ($wasDeprecated -and $AllowDeprecatedRemoval) {
                $warnings.Value += "WARN: deprecated required property '$req' removed at $context"
            }
            else {
                $breaking.Value += "BREAKING: required property '$req' no longer required or removed at $context"
            }
        }
    }
    foreach ($req in $candReq) {
        if (-not $baseReq.Contains($req)) {
            $breaking.Value += "BREAKING: new required property '$req' at $context"
        }
    }

    $baseProps = Get-SchemaProperties $baseSchema
    $candProps = Get-SchemaProperties $candSchema
    foreach ($name in $baseProps.Keys) {
        if (-not $candProps.ContainsKey($name)) {
            $prop = $baseProps[$name]
            $wasDeprecated = ($null -ne $prop -and ($prop.PSObject.Properties.Name -contains 'deprecated') -and [bool]$prop.deprecated)
            if ($wasDeprecated -and $AllowDeprecatedRemoval) {
                $warnings.Value += "WARN: deprecated property '$name' removed at $context"
            }
            else {
                $breaking.Value += "BREAKING: property '$name' removed at $context"
            }
        }
        else {
            $bt = Get-PrimaryType $baseProps[$name]
            $ct = Get-PrimaryType $candProps[$name]
            if ($bt -and $ct -and $bt -ne $ct) {
                $breaking.Value += "BREAKING: property '$name' type changed $bt -> $ct at $context"
            }
        }
    }
}

$breaking = [System.Collections.Generic.List[string]]::new()
$warnings = [System.Collections.Generic.List[string]]::new()
$expandOnly = [System.Collections.Generic.List[string]]::new()

try {
    $base = Read-OpenApi $Baseline
    $cand = Read-OpenApi $Candidate
}
catch {
    Write-Error $_
    exit 2
}

# Paths removed
$basePaths = @{}
if ($base.PSObject.Properties.Name -contains 'paths') {
    foreach ($p in $base.paths.PSObject.Properties) { $basePaths[$p.Name] = $p.Value }
}
$candPaths = @{}
if ($cand.PSObject.Properties.Name -contains 'paths') {
    foreach ($p in $cand.paths.PSObject.Properties) { $candPaths[$p.Name] = $p.Value }
}

foreach ($path in $basePaths.Keys) {
    if (-not $candPaths.ContainsKey($path)) {
        $breaking.Add("BREAKING: path removed $path")
        continue
    }
    $basePath = $basePaths[$path]
    $candPath = $candPaths[$path]
    foreach ($method in @('get', 'post', 'put', 'patch', 'delete', 'head', 'options')) {
        $baseHas = $basePath.PSObject.Properties.Name -contains $method
        $candHas = $candPath.PSObject.Properties.Name -contains $method
        if ($baseHas -and -not $candHas) {
            $breaking.Add("BREAKING: operation removed $method $path")
            continue
        }
        if (-not $baseHas) { continue }

        $baseOp = $basePath.$method
        $candOp = $candPath.$method

        # Response 200 schema compare (minimal contract surface)
        $base200 = $null
        $cand200 = $null
        if ($baseOp.responses.PSObject.Properties.Name -contains '200') {
            $br = $baseOp.responses.'200'
            if ($br.content.'application/json'.schema) { $base200 = $br.content.'application/json'.schema }
        }
        if ($candOp.responses.PSObject.Properties.Name -contains '200') {
            $cr = $candOp.responses.'200'
            if ($cr.content.'application/json'.schema) { $cand200 = $cr.content.'application/json'.schema }
        }
        Compare-Schemas "$method $path response 200" $base200 $cand200 ([ref]$breaking) ([ref]$warnings)

        # Request body
        if ($baseOp.PSObject.Properties.Name -contains 'requestBody') {
            $baseBody = $null
            $candBody = $null
            if ($baseOp.requestBody.content.'application/json'.schema) { $baseBody = $baseOp.requestBody.content.'application/json'.schema }
            if ($candOp.requestBody.content.'application/json'.schema) { $candBody = $candOp.requestBody.content.'application/json'.schema }
            Compare-Schemas "$method $path requestBody" $baseBody $candBody ([ref]$breaking) ([ref]$warnings)
        }
    }
}

# New paths are expand-only
foreach ($path in $candPaths.Keys) {
    if (-not $basePaths.ContainsKey($path)) {
        $expandOnly.Add("EXPAND: new path $path")
    }
}

# Component schemas
$baseComponents = @{}
$candComponents = @{}
if ($base.components.PSObject.Properties.Name -contains 'schemas') {
    foreach ($s in $base.components.schemas.PSObject.Properties) { $baseComponents[$s.Name] = $s.Value }
}
if ($cand.components.PSObject.Properties.Name -contains 'schemas') {
    foreach ($s in $cand.components.schemas.PSObject.Properties) { $candComponents[$s.Name] = $s.Value }
}
foreach ($name in $baseComponents.Keys) {
    if (-not $candComponents.ContainsKey($name)) {
        $breaking.Add("BREAKING: component schema removed $name")
    }
    else {
        Compare-Schemas "components.schemas.$name" $baseComponents[$name] $candComponents[$name] ([ref]$breaking) ([ref]$warnings)
    }
}
foreach ($name in $candComponents.Keys) {
    if (-not $baseComponents.ContainsKey($name)) {
        $expandOnly.Add("EXPAND: new component schema $name")
    }
}

$result = [ordered]@{
    card             = "REL-004"
    baseline         = $Baseline
    candidate        = $Candidate
    breakingCount    = $breaking.Count
    warningCount     = $warnings.Count
    expandCount      = $expandOnly.Count
    breaking         = @($breaking)
    warnings         = @($warnings)
    expandOnly       = @($expandOnly)
    verdict          = if ($breaking.Count -eq 0) { "PASS" } else { "FAIL" }
}

if ($JsonOut -or $OutFile) {
    $json = $result | ConvertTo-Json -Depth 8
    if ($OutFile) {
        $outFull = Resolve-RepoPath $OutFile
        $dir = Split-Path $outFull -Parent
        if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
        Set-Content -Path $outFull -Value $json -Encoding UTF8
    }
    if ($JsonOut) { Write-Output $json }
}

Write-Host "REL-004 OpenAPI diff: $($result.verdict) (breaking=$($breaking.Count) warnings=$($warnings.Count) expand=$($expandOnly.Count))"
foreach ($b in $breaking) { Write-Host $b }
foreach ($w in $warnings) { Write-Host $w }

if ($breaking.Count -gt 0) { exit 1 }
exit 0
