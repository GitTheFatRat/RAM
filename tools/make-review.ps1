[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$zipPath = Join-Path $repoRoot "review.zip"

Push-Location $repoRoot
try {
    git archive --format=zip -o review.zip HEAD
    if ($LASTEXITCODE -ne 0) {
        throw "git archive failed with exit code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}

Write-Host "Review bundle created: $zipPath"
