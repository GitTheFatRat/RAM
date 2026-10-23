[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$Cookie = $env:RBX_LIVE_TEST_COOKIE
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Cookie)) {
    Write-Warning "Live testing disabled. Pass -Cookie <cookie> or set RBX_LIVE_TEST_COOKIE to run against live Roblox endpoints."
    exit 0
}

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot

try {
    $env:RBX_LIVE_TEST_COOKIE = $Cookie

    Write-Host "Running Live Integration Test Suite against real Roblox endpoints..." -ForegroundColor Cyan
    dotnet test "$repoRoot/tests/Infrastructure.LiveTests/Infrastructure.LiveTests.csproj" -c Release --filter Category=Live --logger "console;verbosity=normal"
    if ($LASTEXITCODE -ne 0) {
        throw "Live integration tests failed with exit code $LASTEXITCODE"
    }

    Write-Host "PASS: All live integration tests succeeded." -ForegroundColor Green
    exit 0
}
finally {
    $env:RBX_LIVE_TEST_COOKIE = $null
    Pop-Location
}
