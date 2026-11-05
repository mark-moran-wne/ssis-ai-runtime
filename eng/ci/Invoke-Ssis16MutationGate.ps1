[CmdletBinding()]
param(
    [ValidateSet("Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw "The SSIS 16 mutation gate requires Windows."
}
if (-not [Environment]::Is64BitOperatingSystem -or -not [Environment]::Is64BitProcess) {
    throw "The SSIS 16 mutation gate requires a Windows x64 PowerShell process."
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$nativeHarness = Join-Path $repositoryRoot "tests\SsisAiRuntime.Ssis16IntegrationTests\bin\$Configuration\net48\SsisAiRuntime.Ssis16IntegrationTests.exe"

function Invoke-GateCommand {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Command,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Arguments
    )

    Write-Host "== $Name =="
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

Push-Location $repositoryRoot
try {
    Invoke-GateCommand "Release solution build" "dotnet" @("build", ".\SsisAiRuntime.sln", "-c", $Configuration)

    $portableTests = @(
        ".\tests\SsisAiRuntime.Tests\SsisAiRuntime.Tests.csproj",
        ".\tests\SsisAiRuntime.Corpus.Tests\SsisAiRuntime.Corpus.Tests.csproj",
        ".\tests\SsisAiRuntime.Mutations.Tests\SsisAiRuntime.Mutations.Tests.csproj"
    )
    foreach ($project in $portableTests) {
        Invoke-GateCommand "Portable tests: $project" "dotnet" @("test", $project, "-c", $Configuration, "--no-build")
    }

    $corpusGate = Join-Path $PSScriptRoot "Invoke-CorpusBaselineGate.ps1"
    Invoke-GateCommand "Curated corpus snapshot and baseline gate" "powershell.exe" @(
        "-NoProfile", "-File", $corpusGate, "-Configuration", $Configuration)

    if (-not (Test-Path $nativeHarness -PathType Leaf)) {
        throw "The SSIS 16 native integration harness was not produced at $nativeHarness."
    }

    Invoke-GateCommand "SSIS 16 native integration and fixture harness" $nativeHarness @()
    Invoke-GateCommand "MutationHost lifecycle probe" $nativeHarness @("--mutation-host-probe")
    Invoke-GateCommand "Designer/layout verification" $nativeHarness @("--designer-layout-probe")
    Invoke-GateCommand "Column-resize probe" $nativeHarness @("--column-resize-probe")
    Invoke-GateCommand "Exact path-splice fixtures" $nativeHarness @("--path-splice-probe")
    Invoke-GateCommand "Union All selected-input fixture" $nativeHarness @("--union-all-probe")

    Write-Host "SSIS16-Mutation-Gate: PASS"
}
finally {
    Pop-Location
}