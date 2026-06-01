<#
.SYNOPSIS
Compiles every corpus workspace and the quirks workspace once with the instrumented NG Center, and collects
the VB6 outputs into each workspace's "VB6 Outputs\99999" folder.

.DESCRIPTION
Each workspace is copied to its own scratch engine folder and compiled through NG Center's command line in a
fresh process. The script hashes the inputs before and after the compile, collects script.dat, the language
.dat files, new @image files and LastCompilerLog.txt, and prints a summary. See PRODUCING-VB6-OUTPUTS.md in the
docs folder at the repository root, ..\..\docs from this script.

.PARAMETER NGCenterPath
The instrumented NG_Center.exe.

.PARAMETER ScratchRoot
A new folder for the scratch engine folders. The script never deletes it, so a later run needs a new one.

.PARAMETER Workspace
Names of the workspaces to compile, wildcards allowed; Quirks names the quirks workspace.

.PARAMETER TimeoutSeconds
How long one compile may take before the script stops NG Center, which then waits on a message box.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\Run-VB6Corpus.ps1 -NGCenterPath 'C:\NG_Center\NG_Center.exe'
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [string]$NGCenterPath,

    [string]$ScratchRoot = (Join-Path $env:TEMP ('NGCorpusRun-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))),

    [string[]]$Workspace = @('*'),

    [int]$TimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$fixtures = $PSScriptRoot
$outputSubfolder = 'VB6 Outputs\99999'
# The quirks workspace declares PluginDemo in a Plugin= line, and its main language is French.
$quirksPlugin = 'PluginDemo.script'
$mainLanguagePlaceholders = @{ 'Quirks' = 'French' }

function Stop-Run([string]$message)
{
    Write-Host $message -ForegroundColor Red
    exit 1
}

# Get-FileHash honors -WhatIf in Windows PowerShell and then returns nothing, so the hash comes from .NET.
function Get-Sha256([string]$path)
{
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($algorithm.ComputeHash([IO.File]::ReadAllBytes($path))) }
    finally { $algorithm.Dispose() }
}

# Maps each file below a folder to its SHA-256 hash, keyed by the path relative to that folder.
function Get-TreeHashes([string]$root)
{
    $hashes = @{}
    foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File -Force)
    {
        $hashes[$file.FullName.Substring($root.Length + 1)] = Get-Sha256 $file.FullName
    }
    return $hashes
}

# NG Center writes its log in ANSI and appends one message per line.
function Read-AnsiLines([string]$path)
{
    if (-not (Test-Path -LiteralPath $path)) { return @() }
    return [IO.File]::ReadAllLines($path, [Text.Encoding]::Default)
}

function Test-SameBytes([string]$first, [string]$second)
{
    return (Get-Sha256 $first) -eq (Get-Sha256 $second)
}

# ---------------------------------------------------------------- checks before the first compile

if (-not (Test-Path -LiteralPath $NGCenterPath -PathType Leaf)) { Stop-Run "NG_Center.exe not found: $NGCenterPath" }
$NGCenterPath = (Resolve-Path -LiteralPath $NGCenterPath).Path
$ngFolder = Split-Path -Parent $NGCenterPath
$settingsPath = Join-Path $ngFolder 'center_settings.bin'

if (-not (Test-Path -LiteralPath $settingsPath))
{
    Stop-Run 'NG Center has no center_settings.bin. Start NG Center once without arguments, choose an engine folder and close it, then run this script again.'
}

if (Test-Path -LiteralPath (Join-Path $ngFolder 'JUST_INSTALLED.txt'))
{
    Stop-Run 'NG Center holds JUST_INSTALLED.txt, so its next start asks questions. Start NG Center once without arguments, then run this script again.'
}

# NG Center rewrites script.txt and the language files at startup for each plugin script in its folder
# that the script does not declare, so the run refuses to start while any is there.
$pluginScripts = @(Get-ChildItem -LiteralPath $ngFolder -Filter 'Plugin*.script' -File)
if ($pluginScripts.Count -gt 0)
{
    $names = ($pluginScripts | ForEach-Object { $_.Name }) -join ', '
    Stop-Run "Move these plugin scripts out of $ngFolder for the run: $names"
}

$processNames = @('NG_Center', [IO.Path]::GetFileNameWithoutExtension($NGCenterPath)) | Select-Object -Unique
if (@(Get-Process -Name $processNames -ErrorAction SilentlyContinue).Count -gt 0)
{
    Stop-Run 'Close every NG Center window first: a second instance shares its log and its settings file.'
}

$ScratchRoot = [IO.Path]::GetFullPath($ScratchRoot)
if ($ScratchRoot -match '[^\x20-\x7E]') { Stop-Run "Pass a -ScratchRoot made of ASCII characters only: $ScratchRoot" }
if ($ScratchRoot.Length -gt 150) { Stop-Run "Pass a -ScratchRoot of at most 150 characters, because NG Center keeps the engine folder path in 256 characters and opens files through 260-character paths: $ScratchRoot" }
if (Test-Path -LiteralPath $ScratchRoot) { Stop-Run "The scratch folder already exists; pass a new -ScratchRoot: $ScratchRoot" }

# The corpus workspaces in name order, then the quirks workspace, which is the only one run with a plugin.
$runs = New-Object System.Collections.Generic.List[object]
foreach ($folder in Get-ChildItem -LiteralPath (Join-Path $fixtures 'Corpus') -Directory | Sort-Object Name)
{
    if (Test-Path -LiteralPath (Join-Path $folder.FullName 'Workspace\script'))
    {
        $runs.Add([pscustomobject]@{ Name = $folder.Name; Root = $folder.FullName })
    }
}
$runs.Add([pscustomobject]@{ Name = 'Quirks'; Root = (Join-Path $fixtures 'Quirks') })

# powershell -File passes a list as one comma-separated string, so the names are split here.
$patterns = @($Workspace | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })
$selected = @($runs | Where-Object { $name = $_.Name; @($patterns | Where-Object { $name -like $_ }).Count -gt 0 })
if ($selected.Count -eq 0) { Stop-Run "No workspace matches: $($Workspace -join ', ')" }

# Outputs from an earlier run stay until the owner removes them, because the script deletes no file it did not create.
foreach ($run in $selected)
{
    $outputs = Join-Path $run.Root $outputSubfolder
    $leftovers = @(Get-ChildItem -LiteralPath $outputs -File -Force -ErrorAction SilentlyContinue)
    if ($leftovers.Count -gt 0) { Stop-Run "Empty $outputs first; it holds $($leftovers.Count) file(s) from an earlier run." }
}

$settingsBefore = (Get-Sha256 $settingsPath)
$ngItemsBefore = @(Get-ChildItem -LiteralPath $ngFolder -Force | ForEach-Object { $_.Name })
$ngVersion = (Get-Item -LiteralPath $NGCenterPath).VersionInfo.FileVersion
$codePage = Get-ItemPropertyValue 'HKLM:\SYSTEM\CurrentControlSet\Control\Nls\CodePage' 'ACP'
$runInfo = @(
    "NG Center: $NGCenterPath",
    "NG Center file version: $ngVersion",
    "Windows ANSI code page: $codePage",
    "System locale: $((Get-WinSystemLocale).Name)",
    "Culture: $((Get-Culture).Name)",
    "Run: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
)

Write-Host ($runInfo -join [Environment]::NewLine)
Write-Host "Scratch folder: $ScratchRoot"

if (-not $PSCmdlet.ShouldProcess($ScratchRoot, 'Create the scratch folder and back up center_settings.bin'))
{
    foreach ($run in $selected)
    {
        Write-Host ("Would compile {0}: `"{1}`" `"{2}\{0}\script\script.txt`" -Log -NoWait -NoMsgBox" -f $run.Name, $NGCenterPath, $ScratchRoot)
    }
    return
}

New-Item -ItemType Directory -Path $ScratchRoot | Out-Null
$settingsBackup = Join-Path $ScratchRoot 'center_settings.before.bin'
Copy-Item -LiteralPath $settingsPath -Destination $settingsBackup

# ---------------------------------------------------------------- one compile per workspace

$results = New-Object System.Collections.Generic.List[object]

foreach ($run in $selected)
{
    $source = Join-Path $run.Root 'Workspace'
    $trle = Join-Path $ScratchRoot $run.Name
    $result = [pscustomobject]@{ Workspace = $run.Name; Result = ''; Main = ''; Collected = 0; Detail = @() }
    $results.Add($result)

    Write-Host ''
    Write-Host "=== $($run.Name)" -ForegroundColor Cyan

    Copy-Item -LiteralPath $source -Destination $trle -Recurse
    $inputs = Get-TreeHashes $trle

    # NG Center copies its own objects.h over an older one at startup, so the copy gets the current time.
    $objects = Join-Path $trle 'Objects.h'
    if (Test-Path -LiteralPath $objects) { (Get-Item -LiteralPath $objects).LastWriteTime = Get-Date }

    # NG Center stops at startup without this file, and no compile reads it.
    $sounds = Join-Path $trle 'sound\LevelSFX Creator'
    New-Item -ItemType Directory -Force -Path $sounds | Out-Null
    [IO.File]::WriteAllText((Join-Path $sounds 'sounds.txt'), "; Placeholder for NG Center`r`n", [Text.Encoding]::ASCII)

    # NG Center picks the last listed language whose .dat sits in the engine folder as the main language,
    # else english, else the first; it deletes the placeholder before it compiles.
    if ($mainLanguagePlaceholders.ContainsKey($run.Name))
    {
        [IO.File]::WriteAllBytes((Join-Path $trle ($mainLanguagePlaceholders[$run.Name] + '.dat')), (New-Object byte[] 0))
    }

    $scriptFile = Get-ChildItem -LiteralPath (Join-Path $trle 'script') -File | Where-Object { $_.Name -ieq 'script.txt' } | Select-Object -First 1
    if ($null -eq $scriptFile)
    {
        $result.Result = 'not run'
        $result.Detail = @('The workspace has no script\script.txt.')
        continue
    }

    $copiedPlugin = $null
    if ($run.Name -eq 'Quirks')
    {
        $copiedPlugin = Join-Path $ngFolder $quirksPlugin
        Copy-Item -LiteralPath (Join-Path $source $quirksPlugin) -Destination $copiedPlugin
    }

    $arguments = '"' + $scriptFile.FullName + '" -Log -NoWait -NoMsgBox'
    $timedOut = $false

    try
    {
        $process = Start-Process -FilePath $NGCenterPath -ArgumentList $arguments -WorkingDirectory $ngFolder -PassThru
        if (-not $process.WaitForExit($TimeoutSeconds * 1000))
        {
            $timedOut = $true
            Stop-Process -Id $process.Id -Force
            $process.WaitForExit()
        }
    }
    finally
    {
        if ($null -ne $copiedPlugin -and (Test-Path -LiteralPath $copiedPlugin)) { Remove-Item -LiteralPath $copiedPlugin }
    }

    $log = Join-Path $trle 'LastCompilerLog.txt'
    $logLines = @(Read-AnsiLines $log)

    # Inputs must be unchanged, except a pix\imageN.bmp that IF_CRYPTIC turned into pix\@imageN.bmp.
    $after = Get-TreeHashes $trle
    $changed = New-Object System.Collections.Generic.List[string]
    foreach ($key in $inputs.Keys)
    {
        if ($after.ContainsKey($key))
        {
            if ($after[$key] -ne $inputs[$key]) { $changed.Add("changed: $key") }
        }
        elseif (-not ($key -match '^pix\\(image\d+\.bmp)$' -and $after.ContainsKey('pix\@' + $Matches[1])))
        {
            $changed.Add("removed: $key")
        }
    }

    $mainCopies = @(Get-ChildItem -LiteralPath $trle -Filter '*.dat' -File | Where-Object { $_.Name -ine 'script.dat' })
    $result.Main = ($mainCopies | ForEach-Object { $_.BaseName }) -join ', '

    $errorLines = @($logLines | Where-Object { $_ -match '(?i)\berror\b' -and $_ -notmatch '(?i)^\s*(Last error|Failed the compiling)' } | ForEach-Object { $_.Trim() } | Select-Object -Unique)
    $warningCount = @($logLines | Where-Object { $_ -match '(?i)\bwarning\b' }).Count
    $completed = @($logLines | Where-Object { $_ -match 'Completed compilation' }).Count -gt 0
    $scriptDat = Get-ChildItem -LiteralPath (Join-Path $trle 'script') -Filter '*.dat' -File | Where-Object { $_.Name -ieq 'script.dat' } | Select-Object -First 1

    $outputs = Join-Path $run.Root $outputSubfolder
    New-Item -ItemType Directory -Force -Path $outputs | Out-Null

    if ($timedOut)
    {
        $result.Result = 'timed out'
        $result.Detail = @("NG Center ran past $TimeoutSeconds s and was stopped; a message box probably waited.") + $errorLines
    }
    elseif ($changed.Count -gt 0)
    {
        $result.Result = 'inputs changed'
        $result.Detail = @('NG Center rewrote an input, so the outputs stay in the scratch folder.') + $changed
    }
    elseif ($completed -and $errorLines.Count -eq 0 -and $null -ne $scriptDat)
    {
        $result.Result = 'compiled'
        $result.Detail = @("$warningCount warning line(s) in the log")

        $collect = @(Get-ChildItem -LiteralPath (Join-Path $trle 'script') -Filter '*.dat' -File)
        $pix = Join-Path $trle 'pix'
        if (Test-Path -LiteralPath $pix)
        {
            $collect += @(Get-ChildItem -LiteralPath $pix -Filter '@image*.bmp' -File | Where-Object { -not $inputs.ContainsKey('pix\' + $_.Name) })
        }

        foreach ($file in $collect) { Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $outputs $file.Name) }
        $result.Collected = $collect.Count
        [IO.File]::WriteAllLines((Join-Path $outputs 'run-info.txt'), $runInfo, [Text.Encoding]::ASCII)

        # VB6 copies script.dat and the main language .dat to the engine folder; each copy must match its source.
        $rootScript = Join-Path $trle 'script.dat'
        if (-not (Test-Path -LiteralPath $rootScript) -or -not (Test-SameBytes $rootScript $scriptDat.FullName))
        {
            $result.Detail += 'The script.dat in the engine folder differs from script\script.dat.'
        }
        foreach ($copy in $mainCopies)
        {
            $twin = Join-Path $trle ('script\' + $copy.Name)
            if (-not (Test-Path -LiteralPath $twin) -or -not (Test-SameBytes $copy.FullName $twin))
            {
                $result.Detail += "The $($copy.Name) in the engine folder differs from the one in script\."
            }
        }
    }
    else
    {
        $result.Result = 'failed'
        if ($errorLines.Count -eq 0) { $errorLines = @('The log holds no error line; read LastCompilerLog.txt.') }
        $result.Detail = $errorLines
    }

    if ($result.Result -ne 'inputs changed' -and (Test-Path -LiteralPath $log))
    {
        Copy-Item -LiteralPath $log -Destination (Join-Path $outputs 'LastCompilerLog.txt')
    }

    Write-Host "$($result.Result): $($result.Detail -join ' | ')"
}

# ---------------------------------------------------------------- summary

Write-Host ''
Write-Host 'Summary' -ForegroundColor Cyan
$results | Format-Table -AutoSize -Wrap -Property Workspace, Result, Main, Collected, @{ Label = 'Detail'; Expression = { $_.Detail -join [Environment]::NewLine } } | Out-Host

$failed = @($results | Where-Object { $_.Result -ne 'compiled' })
Write-Host ("{0} compiled, {1} not." -f ($results.Count - $failed.Count), $failed.Count)

$newItems = @(Get-ChildItem -LiteralPath $ngFolder -Force | ForEach-Object { $_.Name } | Where-Object { $ngItemsBefore -notcontains $_ })
if ($newItems.Count -gt 0)
{
    Write-Host "NG Center created these items in its own folder, which the script leaves in place: $($newItems -join ', ')"
}

# NG Center saves its settings on exit, with the engine folder it had before the command-line compile,
# unless its startup failed; then the saved engine folder is the scratch folder.
$settingsAfter = (Get-Sha256 $settingsPath)
if ($settingsAfter -eq $settingsBefore)
{
    Write-Host 'center_settings.bin is unchanged, so the saved engine folder is unchanged.'
}
else
{
    $text = [Text.Encoding]::Default.GetString([IO.File]::ReadAllBytes($settingsPath))
    if ($text.IndexOf($ScratchRoot, [StringComparison]::OrdinalIgnoreCase) -ge 0)
    {
        Write-Host 'center_settings.bin now names a scratch folder as the engine folder.' -ForegroundColor Yellow
    }
    else
    {
        Write-Host 'center_settings.bin changed, but it names no scratch folder.' -ForegroundColor Yellow
    }
    Write-Host "To restore it, close NG Center and run: Copy-Item -LiteralPath '$settingsBackup' -Destination '$settingsPath'"
}

Write-Host "The scratch folders stay in $ScratchRoot; delete it when the outputs are committed."
if ($failed.Count -gt 0) { exit 1 }
