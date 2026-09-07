param(
    [Parameter(Mandatory = $true)]
    [string]$ResultsDirectory
)

$ErrorActionPreference = 'Stop'

$expectedFullBranchClasses = @(
    'Battle.Core.Outcome.TimeoutOutcomeResolver'
)
$expectedMethods = @(
    @{ ClassName = 'Battle.Core.Engine.BattleState'; MethodName = 'CreateSnapshot' },
    @{ ClassName = 'Battle.Core.Engine.BattleState'; MethodName = 'Get' },
    @{ ClassName = 'Battle.Core.Engine.BattleState'; MethodName = 'GetOpponent' },
    @{ ClassName = 'Battle.Core.Engine.BattleState'; MethodName = 'AdvanceTick' },
    @{ ClassName = 'Battle.Core.Engine.BattleState'; MethodName = 'RecordOutcome' },
    @{ ClassName = 'Battle.Core.Engine.BattleState'; MethodName = 'MarkTerminal' },
    @{ ClassName = 'Battle.Core.Engine.BattleState'; MethodName = 'EnsureMutable' },
    @{ ClassName = 'Battle.Core.Engine.CombatEventEmitter'; MethodName = 'Emit' },
    @{ ClassName = 'Battle.Core.Engine.FighterRuntimeState'; MethodName = 'get_IsDecisionReady' },
    @{ ClassName = 'Battle.Core.Engine.FighterRuntimeState'; MethodName = 'CommitSystemWait' },
    @{ ClassName = 'Battle.Core.Engine.FighterRuntimeState'; MethodName = 'AdvanceActionLifecycle' },
    @{ ClassName = 'Battle.Core.Engine.FighterRuntimeState'; MethodName = 'NextDecisionId' },
    @{ ClassName = 'Battle.Core.Engine.FighterRuntimeState'; MethodName = 'PeekNextDecisionId' },
    @{ ClassName = 'Battle.Core.Engine.FighterRuntimeState'; MethodName = 'CommitDecisionId' },
    @{ ClassName = 'Battle.Core.Engine.FighterRuntimeState'; MethodName = 'SetHealthForTesting' }
)

$coverageFiles = @(
    Get-ChildItem `
        -LiteralPath $ResultsDirectory `
        -Recurse `
        -Filter 'coverage.cobertura.xml' `
        -File
)

if ($coverageFiles.Count -eq 0) {
    throw "No coverage.cobertura.xml files were found under '$ResultsDirectory'."
}

$classes = @(
    foreach ($coverageFile in $coverageFiles) {
        [xml]$coverage = Get-Content -LiteralPath $coverageFile.FullName -Raw
        $coverage.coverage.packages.package.classes.class
    }
)

$failures = [System.Collections.Generic.List[string]]::new()

foreach ($expectedClass in $expectedFullBranchClasses) {
    $matches = @($classes | Where-Object { $_.name -eq $expectedClass })

    if ($matches.Count -ne 1) {
        $failures.Add("Expected one coverage entry for $expectedClass, found $($matches.Count).")
        continue
    }

    $branchRate = [decimal]::Parse(
        $matches[0].'branch-rate',
        [System.Globalization.CultureInfo]::InvariantCulture)

    if ($branchRate -ne 1) {
        $failures.Add("$expectedClass branch coverage is $($branchRate * 100)%, expected 100%.")
    }
}

# BattleState, CombatEventEmitter, and FighterRuntimeState are extension points shared
# by later work packages. Pin the WP-06-owned transition and terminal guards instead
# of treating branches introduced by WP-07+ as part of the historical WP-06 gate.
foreach ($expectedMethod in $expectedMethods) {
    $classMatches = @(
        $classes |
            Where-Object { $_.name -eq $expectedMethod.ClassName }
    )
    if ($classMatches.Count -ne 1) {
        $failures.Add(
            "Expected one coverage entry for $($expectedMethod.ClassName).$($expectedMethod.MethodName), but found $($classMatches.Count) class entries.")
        continue
    }

    $methodMatches = @(
        $classMatches[0].methods.method |
            Where-Object { $_.name -eq $expectedMethod.MethodName }
    )
    if ($methodMatches.Count -ne 1) {
        $failures.Add(
            "Expected one coverage entry for $($expectedMethod.ClassName).$($expectedMethod.MethodName), found $($methodMatches.Count).")
        continue
    }

    $branchRate = [decimal]::Parse(
        $methodMatches[0].'branch-rate',
        [System.Globalization.CultureInfo]::InvariantCulture)
    if ($branchRate -ne 1) {
        $failures.Add(
            "$($expectedMethod.ClassName).$($expectedMethod.MethodName) branch coverage is $($branchRate * 100)%, expected 100%.")
    }
}

if ($failures.Count -gt 0) {
    throw ($failures -join [Environment]::NewLine)
}

Write-Output 'WP-06-owned timeout and transition/terminal guard branch coverage: 100%.'
