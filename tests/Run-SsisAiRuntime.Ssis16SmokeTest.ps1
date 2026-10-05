[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$PackagePath,

    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repositoryRoot 'SsisAiRuntime.sln'
$outputRoot = Join-Path $env:TEMP "SsisAiRuntimeSmoke-$([Guid]::NewGuid().ToString('N'))\"

& dotnet build $solutionPath -c $Configuration "-p:BaseOutputPath=$outputRoot"
if ($LASTEXITCODE -ne 0) {
    throw "Solution build failed with exit code $LASTEXITCODE."
}

# Use a fresh process so previously loaded SSIS assemblies cannot lock build outputs.
$job = Start-Job -ArgumentList $outputRoot, $Configuration, (Resolve-Path -LiteralPath $PackagePath).Path -ScriptBlock {
    param($buildOutputRoot, $buildConfiguration, $dtsxPath)

    $configurationRoot = Join-Path $buildOutputRoot $buildConfiguration
    Add-Type -Path (Join-Path $configurationRoot 'netstandard2.0\SsisAiRuntime.Core.dll')
    Add-Type -Path (Join-Path $configurationRoot 'netstandard2.0\SsisAiRuntime.Inspectors.dll')
    Add-Type -Path (Join-Path $configurationRoot 'net48\SsisAiRuntime.Ssis16.dll')

    function Get-ExecutableCounts($executables) {
        $counts = [pscustomobject]@{ Total = 0; Sql = 0; Pipeline = 0 }
        foreach ($executable in $executables) {
            $counts.Total++
            if ($executable -is [Microsoft.SqlServer.Dts.Runtime.IDTSSequence]) {
                $nestedCounts = Get-ExecutableCounts $executable.Executables
                $counts.Total += $nestedCounts.Total
                $counts.Sql += $nestedCounts.Sql
                $counts.Pipeline += $nestedCounts.Pipeline
            }
            elseif ($executable.CreationName -match 'ExecuteSQLTask') {
                $counts.Sql++
            }
            elseif ($executable.CreationName -like 'SSIS.Pipeline*') {
                $counts.Pipeline++
            }
        }

        return $counts
    }

    $load = [SsisAiRuntime.Ssis16.PackageLoader]::new().Load($dtsxPath)
    if (-not $load.Succeeded) {
        $codes = ($load.Diagnostics.Items | ForEach-Object { $_.Code }) -join ', '
        throw "Package load failed: $codes"
    }

    $session = $load.Session
    $overview = [SsisAiRuntime.Ssis16.PackageOverviewInspector]::new().InspectDetailed($session)
    $connections = [SsisAiRuntime.Ssis16.PackageConnectionInspector]::new().InspectDetailed($session)
    $variables = [SsisAiRuntime.Ssis16.PackageVariableInspector]::new().InspectDetailed($session)
    $parameters = [SsisAiRuntime.Ssis16.PackageParameterInspector]::new().InspectDetailed($session)
    $executables = [SsisAiRuntime.Ssis16.PackageExecutableInspector]::new().InspectDetailed($session)
    $precedence = [SsisAiRuntime.Ssis16.PackagePrecedenceInspector]::new().InspectDetailed($session)
    $sql = [SsisAiRuntime.Ssis16.PackageSqlInspector]::new().InspectDetailed($session)
    $expressions = [SsisAiRuntime.Ssis16.PackageExpressionInspector]::new().InspectDetailed($session)
    $dataFlows = [SsisAiRuntime.Ssis16.PackageDataFlowInspector]::new().InspectDetailed($session)
    $contextBuilder = [SsisAiRuntime.Inspectors.PackageContextBuilder]::new()
    $sqlContext = $contextBuilder.BuildSql($overview.Items[0], $sql)
    $lineageContext = $contextBuilder.BuildLineage($overview.Items[0], $dataFlows)
    $configurationContext = $contextBuilder.BuildConfiguration($overview.Items[0], $connections, $variables, $parameters, $expressions)
    $handleCatalog = [SsisAiRuntime.Inspectors.SemanticHandleCatalogBuilder]::new().Build(
        $overview.Items[0],
        $connections.Items,
        $variables.Items,
        $parameters.Items,
        $executables.Items,
        $dataFlows.Items)
    $controlFlow = [SsisAiRuntime.Inspectors.ControlFlowGraphBuilder]::new().Build($executables.Items, $precedence, $handleCatalog)
    $nativeCounts = Get-ExecutableCounts $session.Package.Executables

    $connectionStringsOmitted = @($connections.Items | Where-Object { -not $_.ConnectionStringOmitted }).Count -eq 0
    $variableValuesOmitted = @($variables.Items | Where-Object { -not $_.ValueOmitted }).Count -eq 0
    $expressionsOmitted = @($variables.Items | Where-Object { $_.HasExpression -and -not $_.ExpressionOmitted }).Count -eq 0
    $parameterValuesOmitted = @($parameters.Items | Where-Object { -not $_.ValueOmitted }).Count -eq 0
    $dataFlowComponents = @($dataFlows.Items | ForEach-Object { $_.Components })
    $inputColumnCount = ($dataFlowComponents | ForEach-Object { $_.InputColumns.Count } | Measure-Object -Sum).Sum
    $outputColumnCount = ($dataFlowComponents | ForEach-Object { $_.OutputColumns.Count } | Measure-Object -Sum).Sum
    $externalMetadataColumnCount = ($dataFlowComponents | ForEach-Object { $_.ExternalMetadataColumns.Count } | Measure-Object -Sum).Sum
    $dataFlowSettings = @($dataFlowComponents | ForEach-Object { $_.Settings })
    $redactedDataFlowSettingCount = @($dataFlowSettings | Where-Object { $_.ValueRedacted }).Count
    $handleValues = @($handleCatalog.Entries | ForEach-Object { $_.Handle.Value })
    $handleValuesUnique = @($handleValues | Select-Object -Unique).Count -eq $handleValues.Count
    $sessionHandlesMatch = @($handleCatalog.Entries | Where-Object { $_.Handle.SessionId -ne $session.SessionId }).Count -eq 0
    $duplicateNameGroups = @($handleCatalog.Entries | Group-Object { $_.Name.ToUpperInvariant() } | Where-Object { $_.Count -gt 1 })
    $ambiguousNameGroups = 0
    foreach ($group in $duplicateNameGroups) {
        if ($handleCatalog.FindByName($group.Name).Status -eq [SsisAiRuntime.Inspectors.SemanticHandleResolutionStatus]::Ambiguous) {
            $ambiguousNameGroups++
        }
    }
    $sqlLiteralsSanitized = @($sql.Items | Where-Object {
        [SsisAiRuntime.Inspectors.SqlTextSanitizer]::Sanitize($_.StatementText).Redacted
    }).Count -eq 0

    if (-not $overview.IsComplete -or -not $connections.IsComplete -or -not $variables.IsComplete -or -not $parameters.IsComplete) {
        throw 'A basic metadata inspector reported an unexpected incomplete result.'
    }

    $assertionsPass = (
        $connections.Items.Count -eq $session.Package.Connections.Count -and
        $variables.Items.Count -eq $session.Package.Variables.Count -and
        $parameters.Items.Count -eq $session.Package.Parameters.Count -and
        $executables.Items.Count -eq $nativeCounts.Total -and
        $controlFlow.Nodes.Count -eq $executables.Items.Count -and
        @($controlFlow.Edges | Where-Object { $_.Kind -eq [SsisAiRuntime.Inspectors.ControlFlowEdgeKind]::Precedence }).Count -eq $precedence.Items.Count -and
        $sql.Items.Count -eq $nativeCounts.Sql -and
        $dataFlows.Items.Count -eq $nativeCounts.Pipeline -and
        $sqlContext.SqlStatements.Count -eq $sql.Items.Count -and
        $sqlContext.DataFlows.Count -eq 0 -and
        $lineageContext.DataFlows.Count -eq $dataFlows.Items.Count -and
        $lineageContext.SqlStatements.Count -eq 0 -and
        $configurationContext.Connections.Count -eq $connections.Items.Count -and
        $configurationContext.SqlStatements.Count -eq 0 -and
        $handleValuesUnique -and
        $sessionHandlesMatch -and
        $ambiguousNameGroups -eq $duplicateNameGroups.Count -and
        $connectionStringsOmitted -and
        $variableValuesOmitted -and
        $expressionsOmitted -and
        $parameterValuesOmitted -and
        $sqlLiteralsSanitized
    )

    if (-not $assertionsPass) {
        throw "Inspector assertion failed. Connection counts: $($connections.Items.Count)/$($session.Package.Connections.Count); variable counts: $($variables.Items.Count)/$($session.Package.Variables.Count); parameter counts: $($parameters.Items.Count)/$($session.Package.Parameters.Count); executable counts: $($executables.Items.Count)/$($nativeCounts.Total); SQL counts: $($sql.Items.Count)/$($nativeCounts.Sql); data-flow counts: $($dataFlows.Items.Count)/$($nativeCounts.Pipeline); redaction flags: $connectionStringsOmitted/$variableValuesOmitted/$expressionsOmitted/$parameterValuesOmitted/$sqlLiteralsSanitized."
    }

    [pscustomobject]@{
        Succeeded = $true
        RuntimeVersion = $load.Diagnostics.RuntimeVersion
        ProcessArchitecture = $load.Diagnostics.ProcessArchitecture
        ConnectionCount = $connections.Items.Count
        VariableCount = $variables.Items.Count
        ParameterCount = $parameters.Items.Count
        ExecutableCount = $executables.Items.Count
        PrecedenceConstraintCount = $precedence.Items.Count
        ControlFlowNodeCount = $controlFlow.Nodes.Count
        ControlFlowEdgeCount = $controlFlow.Edges.Count
        ControlFlowUnsupportedCount = $controlFlow.UnsupportedItems.Count
        SqlTaskCount = $sql.Items.Count
        DataFlowCount = $dataFlows.Items.Count
        SqlContextStatementCount = $sqlContext.SqlStatements.Count
        LineageContextDataFlowCount = $lineageContext.DataFlows.Count
        ConfigurationContextConnectionCount = $configurationContext.Connections.Count
        SemanticHandleCount = $handleCatalog.Entries.Count
        SemanticHandleValuesUnique = $handleValuesUnique
        SemanticHandlesSessionScoped = $sessionHandlesMatch
        DuplicateNameGroups = $duplicateNameGroups.Count
        DuplicateNamesReportedAmbiguous = $ambiguousNameGroups
        DataFlowComponentCount = ($dataFlows.Items | ForEach-Object { $_.Components.Count } | Measure-Object -Sum).Sum
        DataFlowPathCount = ($dataFlows.Items | ForEach-Object { $_.Paths.Count } | Measure-Object -Sum).Sum
        DataFlowRuntimeConnectionCount = ($dataFlowComponents | ForEach-Object { $_.RuntimeConnections.Count } | Measure-Object -Sum).Sum
        InputColumnCount = $inputColumnCount
        OutputColumnCount = $outputColumnCount
        ExternalMetadataColumnCount = $externalMetadataColumnCount
        DataFlowSettingCount = $dataFlowSettings.Count
        RedactedDataFlowSettingCount = $redactedDataFlowSettingCount
        UnsupportedExecutableDetails = $executables.UnsupportedItems.Count
        UnsupportedSqlDetails = $sql.UnsupportedItems.Count
        UnsupportedDataFlowDetails = $dataFlows.UnsupportedItems.Count
        ExpressionOwners = $expressions.Items.Count
        ExpressionDetailsIncomplete = (-not $expressions.IsComplete)
        SensitiveValuesOmitted = ($connectionStringsOmitted -and $variableValuesOmitted -and $parameterValuesOmitted -and $sqlLiteralsSanitized)
        PackageExecuted = $false
    }
}

try {
    $null = Wait-Job $job
    $jobState = $job.State
    $result = Receive-Job $job
    $result | Format-List
    if ($jobState -ne 'Completed') {
        throw "Smoke test failed in the isolated process (state: $jobState)."
    }
}
finally {
    Remove-Job $job -Force -ErrorAction SilentlyContinue
}