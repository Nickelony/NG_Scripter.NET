# Runs the VB6 byte-parity golden tests (TestCategory=Parity) on every target framework of the test project and reports the result.
# dotnet test runs the suite on every framework and fails when any of them fails, so PARITY PASSED means every framework passed.
# Usage: powershell -ExecutionPolicy Bypass -File .\Run-Parity.ps1
$ErrorActionPreference = 'Stop'

Push-Location $PSScriptRoot

try
{
    Write-Host 'Running VB6 parity suite (TestCategory=Parity)...' -ForegroundColor Cyan

    # --blame-hang-timeout ends a hung test host, so a stuck test fails the run instead of waiting forever.
    dotnet test TRNGScriptCompiler.slnx --filter 'TestCategory=Parity' --blame-hang-timeout 5m -v:q

    if ($LASTEXITCODE -ne 0)
    {
        Write-Host 'PARITY FAILED' -ForegroundColor Red
        exit $LASTEXITCODE
    }

    Write-Host 'PARITY PASSED' -ForegroundColor Green
}
finally
{
    Pop-Location
}
