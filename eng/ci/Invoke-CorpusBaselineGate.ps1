[CmdletBinding()]
param(
    [ValidateSet("Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw "The SSIS corpus gate requires Windows."
}
if (-not [Environment]::Is64BitProcess) {
    throw "The SSIS corpus gate requires a Windows x64 PowerShell process."
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$cli = Join-Path $repositoryRoot "src\SsisAiRuntime.Cli\bin\$Configuration\net48\SsisAiRuntime.Cli.exe"
$fixtureDirectory = Join-Path $repositoryRoot "tests\LayoutFixtures"
$baselineDirectory = Join-Path $fixtureDirectory "CorpusBaselines"

if (-not (Test-Path $cli -PathType Leaf)) { throw "The built CLI was not found." }
if (-not (Test-Path $baselineDirectory -PathType Container)) { throw "The approved corpus baseline directory is missing." }

$fixtures = @(Get-ChildItem -LiteralPath $fixtureDirectory -Filter "*.dtsx" -File | Sort-Object Name)
if ($fixtures.Count -eq 0) { throw "The structural DTSX corpus is empty." }

function Invoke-CorpusCli {
    param([string[]]$Arguments)

    $output = & $cli @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    $text = [string]::Join([Environment]::NewLine, [string[]]$output).Trim()
    if ([string]::IsNullOrWhiteSpace($text)) { throw "The corpus CLI returned no JSON output." }
    try { $report = $text | ConvertFrom-Json }
    catch { throw "The corpus CLI returned invalid JSON." }

    [pscustomobject]@{ ExitCode = $exitCode; Report = $report }
}

foreach ($fixture in $fixtures) {
    $baseline = Join-Path $baselineDirectory ($fixture.BaseName + ".json")
    if (-not (Test-Path $baseline -PathType Leaf)) {
        throw "Approved baseline missing for $($fixture.Name)."
    }

    $first = Invoke-CorpusCli @("ai", "corpus.snapshot", $fixture.FullName)
    $second = Invoke-CorpusCli @("ai", "corpus.snapshot", $fixture.FullName)
    if ($first.ExitCode -notin @(0, 5) -or $second.ExitCode -notin @(0, 5) -or
        -not $first.Report.succeeded -or -not $second.Report.succeeded) {
        throw "Snapshot analysis failed for $($fixture.Name)."
    }

    $firstSnapshot = ConvertTo-Json -InputObject $first.Report.results.snapshot -Depth 100 -Compress
    $secondSnapshot = ConvertTo-Json -InputObject $second.Report.results.snapshot -Depth 100 -Compress
    if ($firstSnapshot -cne $secondSnapshot) {
        throw "Repeated snapshots differ for $($fixture.Name)."
    }

    $verification = Invoke-CorpusCli @("ai", "corpus.verify", $fixture.FullName, "--baseline", $baseline)
    if ($verification.ExitCode -notin @(0, 5) -or -not $verification.Report.succeeded -or
        -not $verification.Report.results.isMatch) {
        $changeSummary = $verification.Report.results.changes | ConvertTo-Json -Depth 20 -Compress
        throw "Approved baseline mismatch for $($fixture.Name): $changeSummary"
    }

    Write-Host ("Corpus baseline {0}: PASS; complete={1}; nodes={2}; edges={3}; gaps={4}" -f `
        $fixture.BaseName, $first.Report.isComplete, $first.Report.results.snapshot.nodeCount,
        $first.Report.results.snapshot.edgeCount, $first.Report.results.snapshot.coverageGapCount)
}

Write-Host ("Corpus baseline gate: PASS; {0} structural fixtures; baselines were not modified." -f $fixtures.Count)