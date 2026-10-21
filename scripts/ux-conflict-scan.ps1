# UX-004: flag [HttpPost] actions that return BadRequest() without return View in the same method.
param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$controllers = Get-ChildItem -Path (Join-Path $Root "src") -Recurse -Filter "*Controller*.cs" -File
$findings = @()

foreach ($file in $controllers) {
    $lines = Get-Content -LiteralPath $file.FullName
    $inHttpPost = $false
    $braceDepth = 0
    $methodStart = -1
    $methodHasViewReturn = $false
    $methodHasBadRequest = $false
    $methodName = ""

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ($line -match '^\s*\[HttpPost') {
            $inHttpPost = $true
            $braceDepth = 0
            $methodStart = -1
            $methodHasViewReturn = $false
            $methodHasBadRequest = $false
            $methodName = ""
            continue
        }

        if (-not $inHttpPost) { continue }

        if ($methodStart -lt 0 -and $line -match '^\s*(public|private|protected|internal).*\(') {
            $methodStart = $i + 1
            if ($line -match '\s(\w+)\s*\(') { $methodName = $Matches[1] }
        }

        if ($methodStart -ge 0) {
            if ($line -match 'return\s+View\s*\(') { $methodHasViewReturn = $true }
            if ($line -match 'return\s+BadRequest\s*\(') { $methodHasBadRequest = $true }

            $open = ([regex]::Matches($line, '\{')).Count
            $close = ([regex]::Matches($line, '\}')).Count
            $braceDepth += $open - $close

            if ($braceDepth -le 0 -and $line -match '\}') {
                if ($methodHasBadRequest -and -not $methodHasViewReturn) {
                    $relative = $file.FullName.Substring($Root.Length + 1)
                    $findings += [pscustomobject]@{
                        File = $relative
                        Line = $methodStart
                        Method = $methodName
                    }
                }
                $inHttpPost = $false
            }
        }
    }
}

if ($findings.Count -eq 0) {
    Write-Host "ux-conflict-scan: no HttpPost BadRequest-without-View findings."
    exit 0
}

Write-Host "ux-conflict-scan: $($findings.Count) finding(s):"
$findings | Format-Table -AutoSize
exit 1
