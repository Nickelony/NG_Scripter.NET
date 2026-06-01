# Runs the test suite with XPlat code coverage and a TRX log once per target framework of the test project, and enforces the library
# line-coverage floor on each run. The reports of a framework land in artifacts\test-results\<framework>, and each run of the script
# empties artifacts\test-results first.
# Usage: powershell -ExecutionPolicy Bypass -File .\Run-Coverage.ps1 [-LineThreshold 0.90]
param(
    [double]$LineThreshold = 0.90
)

$ErrorActionPreference = 'Stop'

Push-Location $PSScriptRoot

try
{
    # The paths are joined by .NET, so the script runs under Windows PowerShell and under pwsh on Linux alike.
    $testProject = [System.IO.Path]::Combine($PSScriptRoot, 'TRNGScriptCompiler.Tests', 'TRNGScriptCompiler.Tests.csproj')
    $resultsRoot = [System.IO.Path]::Combine($PSScriptRoot, 'artifacts', 'test-results')

    if (Test-Path $resultsRoot)
    {
        Remove-Item -Recurse -Force $resultsRoot
    }

    $frameworks = @((dotnet msbuild $testProject -getProperty:TargetFrameworks).Trim() -split ';' | Where-Object { $_ })

    if ($LASTEXITCODE -ne 0 -or $frameworks.Count -eq 0)
    {
        Write-Host 'TARGET FRAMEWORKS UNKNOWN' -ForegroundColor Red
        exit 1
    }

    $failures = @()

    foreach ($framework in $frameworks)
    {
        $resultsDirectory = [System.IO.Path]::Combine($resultsRoot, $framework)

        Write-Host ("Running tests on {0} with code coverage (floor: {1:N0}% line on TRNGScriptCompiler)..." -f $framework, ($LineThreshold * 100)) -ForegroundColor Cyan

        # --blame-hang-timeout ends a hung test host, so a stuck test fails the run instead of holding CI until its job limit.
        dotnet test $testProject --framework $framework --collect:"XPlat Code Coverage" --logger trx --results-directory $resultsDirectory --blame-hang-timeout 5m -v:q

        if ($LASTEXITCODE -ne 0)
        {
            $failures += "${framework}: tests failed"
            continue
        }

        $report = Get-ChildItem $resultsDirectory -Recurse -Filter 'coverage.cobertura.xml' -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1

        if (-not $report)
        {
            $failures += "${framework}: coverage report missing"
            continue
        }

        [xml]$coverage = Get-Content $report.FullName

        $classes = @($coverage.coverage.packages.package |
            Where-Object { $_.name -eq 'TRNGScriptCompiler' } |
            ForEach-Object { $_.classes.class })

        $lines = @($classes | ForEach-Object { $_.lines.line })

        if ($lines.Count -eq 0)
        {
            $failures += "${framework}: coverage report has no library lines"
            continue
        }

        $coveredLines = @($lines | Where-Object { [int]$_.hits -gt 0 }).Count
        $lineRate = $coveredLines / $lines.Count

        Write-Host ("{0}: TRNGScriptCompiler line coverage: {1:N2}% ({2}/{3})" -f $framework, ($lineRate * 100), $coveredLines, $lines.Count)

        if ($lineRate -lt $LineThreshold)
        {
            $failures += ("{0}: coverage below the floor, required {1:P1}, actual {2:P2}" -f $framework, $LineThreshold, $lineRate)
        }
    }

    if ($failures.Count -gt 0)
    {
        foreach ($failure in $failures)
        {
            Write-Host "FAILED $failure" -ForegroundColor Red
        }

        exit 1
    }

    Write-Host 'COVERAGE PASSED' -ForegroundColor Green
}
finally
{
    Pop-Location
}
