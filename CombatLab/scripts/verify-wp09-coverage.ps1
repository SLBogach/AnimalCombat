param(
    [Parameter(Mandatory = $true)]
    [string]$CoreResultsDirectory,

    [Parameter(Mandatory = $true)]
    [string]$IntegrationResultsDirectory,

    [Parameter(Mandatory = $true)]
    [string]$ReplayResultsDirectory
)

$ErrorActionPreference = 'Stop'
$requiredCoreClasses = @(
    'Battle.Core.Resolution.ResolutionMath',
    'Battle.Core.Resolution.ImpactIntentCollector',
    'Battle.Core.Resolution.ImpactIntentOrderer',
    'Battle.Core.Resolution.ForcedMovementResolver',
    'Battle.Core.Resolution.ResolutionSystem'
)
$requiredReplayClasses = @(
    'Battle.Replay.Verification.ResolutionReplaySemanticValidator'
)

function Get-CoverageDocument {
    param([string]$Directory, [string]$Label)
    if (-not (Test-Path -LiteralPath $Directory -PathType Container)) {
        throw "$Label coverage directory '$Directory' does not exist."
    }
    $files = @(Get-ChildItem -LiteralPath $Directory -Recurse -Filter 'coverage.cobertura.xml' -File)
    if ($files.Count -ne 1) {
        throw "Expected exactly one $Label coverage file, found $($files.Count)."
    }
    return [xml](Get-Content -LiteralPath $files[0].FullName -Raw)
}

function Get-Package {
    param([xml]$Document, [string]$Name)
    $matches = @($Document.coverage.packages.package | Where-Object { $_.name -eq $Name })
    if ($matches.Count -ne 1) { throw "Expected one coverage package '$Name', found $($matches.Count)." }
    return $matches[0]
}

function Assert-ClassCovered {
    param([object]$Package, [string[]]$Names)
    foreach ($name in $Names) {
        $matches = @($Package.classes.class | Where-Object { $_.name -eq $name })
        if ($matches.Count -ne 1) { throw "Expected one coverage entry for $name, found $($matches.Count)." }
        $lineRate = [decimal]::Parse($matches[0].'line-rate', [System.Globalization.CultureInfo]::InvariantCulture)
        $branchRate = [decimal]::Parse($matches[0].'branch-rate', [System.Globalization.CultureInfo]::InvariantCulture)
        if ($lineRate -le 0 -or $branchRate -le 0) {
            throw "$name must execute both lines and critical branches (line=$lineRate, branch=$branchRate)."
        }
    }
}

function Assert-FullMethodBranchCoverage {
    param([object]$Package, [object[]]$Methods)
    foreach ($expected in $Methods) {
        $classMatches = @($Package.classes.class | Where-Object { $_.name -eq $expected.ClassName })
        if ($classMatches.Count -ne 1) {
            throw "Expected one coverage entry for $($expected.ClassName), found $($classMatches.Count)."
        }
        $methodMatches = @($classMatches[0].methods.method | Where-Object { $_.name -eq $expected.MethodName })
        if ($methodMatches.Count -ne 1) {
            throw "Expected one coverage method $($expected.ClassName).$($expected.MethodName), found $($methodMatches.Count)."
        }
        $branchRate = [decimal]::Parse(
            $methodMatches[0].'branch-rate',
            [System.Globalization.CultureInfo]::InvariantCulture)
        if ($branchRate -ne 1) {
            throw "$($expected.ClassName).$($expected.MethodName) branch coverage is $($branchRate * 100)%, expected 100%."
        }
    }
}

$coreDocument = Get-CoverageDocument -Directory $CoreResultsDirectory -Label 'Battle.Core'
$integrationDocument = Get-CoverageDocument -Directory $IntegrationResultsDirectory -Label 'integration Battle.Core'
$replayDocument = Get-CoverageDocument -Directory $ReplayResultsDirectory -Label 'Battle.Replay'
$corePackage = Get-Package -Document $coreDocument -Name 'Battle.Core'
$integrationCorePackage = Get-Package -Document $integrationDocument -Name 'Battle.Core'
$replayPackage = Get-Package -Document $replayDocument -Name 'Battle.Replay'
$combinedLines = @{}
foreach ($package in @($corePackage, $integrationCorePackage)) {
    foreach ($class in $package.classes.class) {
        foreach ($line in $class.lines.line) {
            $key = $class.filename + ':' + $line.number
            if (-not $combinedLines.ContainsKey($key)) { $combinedLines[$key] = $false }
            if ([int]$line.hits -gt 0) { $combinedLines[$key] = $true }
        }
    }
}
$coveredLines = @($combinedLines.Values | Where-Object { $_ }).Count
$coreLineRate = [decimal]$coveredLines / [decimal]$combinedLines.Count
if ($coreLineRate -lt [decimal]0.85) {
    throw "Battle.Core line coverage is $($coreLineRate * 100)%, expected at least 85%."
}

Assert-ClassCovered -Package $corePackage -Names $requiredCoreClasses
Assert-ClassCovered -Package $replayPackage -Names $requiredReplayClasses
Assert-FullMethodBranchCoverage -Package $corePackage -Methods @(
    @{ ClassName = 'Battle.Core.Resolution.ResolutionMath'; MethodName = 'ApplyHealth' },
    @{ ClassName = 'Battle.Core.Resolution.ResolutionMath'; MethodName = 'ApplyBlock' },
    @{ ClassName = 'Battle.Core.Resolution.ResolutionMath'; MethodName = 'ComputeControl' },
    @{ ClassName = 'Battle.Core.Resolution.ResolutionMath'; MethodName = 'ActiveTickBudget' },
    @{ ClassName = 'Battle.Core.Resolution.ImpactIntentOrderer'; MethodName = 'Order' },
    @{ ClassName = 'Battle.Core.Resolution.ResolutionSystem'; MethodName = 'ResolveGroup' },
    @{ ClassName = 'Battle.Core.Resolution.ResolutionSystem'; MethodName = 'BuildBlockedPlan' }
)
Assert-FullMethodBranchCoverage -Package $replayPackage -Methods @(
    @{ ClassName = 'Battle.Replay.Verification.ResolutionReplaySemanticValidator'; MethodName = 'ValidateDamage' },
    @{ ClassName = 'Battle.Replay.Verification.ResolutionReplaySemanticValidator'; MethodName = 'ValidateMarkerFrames' },
    @{ ClassName = 'Battle.Replay.Verification.ResolutionReplaySemanticValidator'; MethodName = 'ValidateSortedStrings' }
)
Write-Output "WP-09 selected arithmetic/order/transition/safety branches: 100%; combined Battle.Core line coverage: $([Math]::Round($coreLineRate * 100, 2))%."
