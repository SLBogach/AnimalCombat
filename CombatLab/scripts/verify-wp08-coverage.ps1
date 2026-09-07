param(
    [Parameter(Mandatory = $true)]
    [string]$CoreResultsDirectory,

    [Parameter(Mandatory = $true)]
    [string]$ReplayResultsDirectory,

    [string]$IntegrationResultsDirectory
)

$ErrorActionPreference = 'Stop'

$expectedCoreClasses = @(
    'Battle.Core.Decisions.DecisionAvailabilityEvaluator',
    'Battle.Core.Decisions.DecisionSelector'
)
$expectedCoreMethods = @(
    @{
        ClassName = 'Battle.Core.Decisions.DecisionVariety'
        MethodName = 'IsAtRepeatCap'
    }
)
$expectedReplayMethods = @(
    @{
        ClassName = 'Battle.Replay.Verification.DecisionReplaySemanticValidator'
        MethodName = 'ValidateDecision'
    },
    @{
        ClassName = 'Battle.Replay.Verification.DecisionReplaySemanticValidator'
        MethodName = 'ValidateWeightedRng'
    }
)

function Get-CoverageDocuments {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ResultsDirectory,

        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    if (-not (Test-Path -LiteralPath $ResultsDirectory -PathType Container)) {
        throw "$Label coverage directory '$ResultsDirectory' does not exist."
    }

    $coverageFiles = @(
        Get-ChildItem `
            -LiteralPath $ResultsDirectory `
            -Recurse `
            -Filter 'coverage.cobertura.xml' `
            -File
    )
    if ($coverageFiles.Count -ne 1) {
        throw "Expected exactly one $Label coverage.cobertura.xml under '$ResultsDirectory', found $($coverageFiles.Count)."
    }

    return @(
        foreach ($coverageFile in $coverageFiles) {
            [xml](Get-Content -LiteralPath $coverageFile.FullName -Raw)
        }
    )
}

function Get-Package {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Documents,

        [Parameter(Mandatory = $true)]
        [string]$PackageName,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.List[string]]$Failures
    )

    $matches = @(
        foreach ($document in $Documents) {
            $document.coverage.packages.package |
                Where-Object { $_.name -eq $PackageName }
        }
    )
    if ($matches.Count -ne 1) {
        $Failures.Add("Expected one $PackageName coverage package, found $($matches.Count).")
        return $null
    }

    return $matches[0]
}

function Assert-FullBranchCoverage {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Documents,

        [Parameter(Mandatory = $true)]
        [string[]]$ExpectedClasses,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.List[string]]$Failures
    )

    $classes = @(
        foreach ($document in $Documents) {
            $document.coverage.packages.package.classes.class
        }
    )

    foreach ($expectedClass in $ExpectedClasses) {
        $matches = @($classes | Where-Object { $_.name -eq $expectedClass })
        if ($matches.Count -ne 1) {
            $Failures.Add("Expected one coverage entry for $expectedClass, found $($matches.Count).")
            continue
        }

        $branchRate = [decimal]::Parse(
            $matches[0].'branch-rate',
            [System.Globalization.CultureInfo]::InvariantCulture)
        if ($branchRate -ne 1) {
            $Failures.Add("$expectedClass branch coverage is $($branchRate * 100)%, expected 100%.")
        }
    }
}

function Assert-FullMethodBranchCoverage {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Documents,

        [Parameter(Mandatory = $true)]
        [object[]]$ExpectedMethods,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.List[string]]$Failures
    )

    $classes = @(
        foreach ($document in $Documents) {
            $document.coverage.packages.package.classes.class
        }
    )

    foreach ($expectedMethod in $ExpectedMethods) {
        $classMatches = @(
            $classes |
                Where-Object { $_.name -eq $expectedMethod.ClassName }
        )
        if ($classMatches.Count -ne 1) {
            $Failures.Add(
                "Expected one coverage entry for $($expectedMethod.ClassName).$($expectedMethod.MethodName), but found $($classMatches.Count) class entries.")
            continue
        }

        $methodMatches = @(
            $classMatches[0].methods.method |
                Where-Object { $_.name -eq $expectedMethod.MethodName }
        )
        if ($methodMatches.Count -ne 1) {
            $Failures.Add(
                "Expected one coverage entry for $($expectedMethod.ClassName).$($expectedMethod.MethodName), found $($methodMatches.Count).")
            continue
        }

        $branchRate = [decimal]::Parse(
            $methodMatches[0].'branch-rate',
            [System.Globalization.CultureInfo]::InvariantCulture)
        if ($branchRate -ne 1) {
            $Failures.Add(
                "$($expectedMethod.ClassName).$($expectedMethod.MethodName) branch coverage is $($branchRate * 100)%, expected 100%.")
        }
    }
}

$coreDocuments = Get-CoverageDocuments `
    -ResultsDirectory $CoreResultsDirectory `
    -Label 'Battle.Core'
$replayDocuments = Get-CoverageDocuments `
    -ResultsDirectory $ReplayResultsDirectory `
    -Label 'Battle.Replay/Battle.Contracts'
$integrationDocuments = @()
if (-not [string]::IsNullOrWhiteSpace($IntegrationResultsDirectory)) {
    $integrationDocuments = @(
        Get-CoverageDocuments `
            -ResultsDirectory $IntegrationResultsDirectory `
            -Label 'integration Battle.Core'
    )
}
$failures = [System.Collections.Generic.List[string]]::new()

$corePackage = Get-Package `
    -Documents $coreDocuments `
    -PackageName 'Battle.Core' `
    -Failures $failures
$lineCoveragePackages = @($corePackage)
if ($integrationDocuments.Count -gt 0) {
    $integrationCorePackage = Get-Package `
        -Documents $integrationDocuments `
        -PackageName 'Battle.Core' `
        -Failures $failures
    $lineCoveragePackages += $integrationCorePackage
}
if ($lineCoveragePackages.Count -gt 0 -and -not ($lineCoveragePackages -contains $null)) {
    $combinedLines = @{}
    foreach ($package in $lineCoveragePackages) {
        foreach ($class in $package.classes.class) {
            foreach ($line in $class.lines.line) {
                $key = $class.filename + ':' + $line.number
                if (-not $combinedLines.ContainsKey($key)) {
                    $combinedLines[$key] = $false
                }
                if ([int]$line.hits -gt 0) {
                    $combinedLines[$key] = $true
                }
            }
        }
    }
    $coveredLines = @($combinedLines.Values | Where-Object { $_ }).Count
    $coreLineRate = [decimal]$coveredLines / [decimal]$combinedLines.Count
    if ($coreLineRate -lt [decimal]0.85) {
        $failures.Add("Battle.Core line coverage is $($coreLineRate * 100)%, expected at least 85%.")
    }
}

$null = Get-Package `
    -Documents $replayDocuments `
    -PackageName 'Battle.Replay' `
    -Failures $failures
$null = Get-Package `
    -Documents $replayDocuments `
    -PackageName 'Battle.Contracts' `
    -Failures $failures

Assert-FullBranchCoverage `
    -Documents $coreDocuments `
    -ExpectedClasses $expectedCoreClasses `
    -Failures $failures
Assert-FullMethodBranchCoverage `
    -Documents $coreDocuments `
    -ExpectedMethods $expectedCoreMethods `
    -Failures $failures
Assert-FullMethodBranchCoverage `
    -Documents $replayDocuments `
    -ExpectedMethods $expectedReplayMethods `
    -Failures $failures

if ($failures.Count -gt 0) {
    throw ($failures -join [Environment]::NewLine)
}

Write-Output 'WP-08 critical decision/replay branch coverage: 100%; current combined Battle.Core line coverage: at least 85%.'
