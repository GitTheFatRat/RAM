[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot

try {
    Write-Host "Building solution in Debug configuration..." -ForegroundColor Cyan
    dotnet build -c Debug
    if ($LASTEXITCODE -ne 0) {
        throw "Debug build failed with exit code $LASTEXITCODE"
    }

    Write-Host "Running sandboxed UI automation harness (Category=UI)..." -ForegroundColor Cyan
    $env:RBXMANAGER_RUN_UI_TESTS = "1"

    dotnet test "$repoRoot/tests/App.UiTests/App.UiTests.csproj" -c Debug --filter Category=UI --logger "console;verbosity=normal"
    $testExitCode = $LASTEXITCODE

    # Find the most recent run artifacts
    $uiArtifactsDir = Join-Path $repoRoot "artifacts/ui"
    if (Test-Path $uiArtifactsDir) {
        $latestRun = Get-ChildItem -Directory $uiArtifactsDir | Sort-Object CreationTime -Descending | Select-Object -First 1
        if ($latestRun) {
            $summaryFile = Join-Path $latestRun.FullName "summary.md"
            if (Test-Path $summaryFile) {
                Write-Host ""
                Write-Host "================ UI Test Summary ================" -ForegroundColor Green
                Get-Content $summaryFile | Write-Host
                Write-Host "Artifacts location: $($latestRun.FullName)" -ForegroundColor Yellow
                Write-Host "================================================" -ForegroundColor Green
            }
        }
    }

    if ($testExitCode -ne 0) {
        Write-Error "UI tests failed with exit code $testExitCode"
        exit 1
    }

    Write-Host "PASS: All UI scenarios and visual assertions passed successfully." -ForegroundColor Green
    exit 0
}
finally {
    $env:RBXMANAGER_RUN_UI_TESTS = $null
    Pop-Location
}
