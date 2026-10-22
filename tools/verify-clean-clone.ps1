[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("rbx_clone_" + [System.Guid]::NewGuid().ToString("N"))

Write-Host "Cloning repo locally to: $tempDir"
$success = $false

try {
    git clone --local "$repoRoot" "$tempDir"
    if ($LASTEXITCODE -ne 0) {
        throw "git clone failed with exit code $LASTEXITCODE"
    }

    Push-Location $tempDir
    try {
        Write-Host "Running dotnet build -c Release in clean clone..."
        dotnet build -c Release
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed with exit code $LASTEXITCODE"
        }

        Write-Host "Running dotnet test -c Release in clean clone..."
        dotnet test -c Release
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet test failed with exit code $LASTEXITCODE"
        }

        $success = $true
    }
    finally {
        Pop-Location
    }
}
catch {
    Write-Error $_.Exception.Message
    $success = $false
}
finally {
    if (Test-Path $tempDir) {
        Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($success) {
    Write-Host "PASS: Clean clone build and tests succeeded."
    exit 0
}
else {
    Write-Host "FAIL: Clean clone failed."
    exit 1
}
