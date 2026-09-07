param(
    [Parameter(Mandatory = $true)]
    [string]$ResultsDirectory,

    [string]$IntegrationResultsDirectory
)

$ErrorActionPreference = 'Stop'

$expectedClasses = @(
    'Battle.Core.Movement.ArenaGeometry',
    'Battle.Core.Movement.ProportionalAllocator',
    'Battle.Core.Movement.MovementPairResolver',
    'Battle.Core.Movement.SeparationResolver',
    'Battle.Core.Decisions.Wp07SystemActionAvailability'
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

$corePackages = @(
    foreach ($coverageFile in $coverageFiles) {
        [xml]$coverage = Get-Content -LiteralPath $coverageFile.FullName -Raw
        $coverage.coverage.packages.package | Where-Object { $_.name -eq 'Battle.Core' }
    }
)

$integrationCorePackages = @()
if (-not [string]::IsNullOrWhiteSpace($IntegrationResultsDirectory)) {
    if (-not (Test-Path -LiteralPath $IntegrationResultsDirectory -PathType Container)) {
        throw "Integration coverage directory '$IntegrationResultsDirectory' does not exist."
    }

    $integrationCoverageFiles = @(
        Get-ChildItem `
            -LiteralPath $IntegrationResultsDirectory `
            -Recurse `
            -Filter 'coverage.cobertura.xml' `
            -File
    )
    if ($integrationCoverageFiles.Count -ne 1) {
        throw "Expected exactly one integration coverage.cobertura.xml under '$IntegrationResultsDirectory', found $($integrationCoverageFiles.Count)."
    }

    $integrationCorePackages = @(
        foreach ($coverageFile in $integrationCoverageFiles) {
            [xml]$coverage = Get-Content -LiteralPath $coverageFile.FullName -Raw
            $coverage.coverage.packages.package | Where-Object { $_.name -eq 'Battle.Core' }
        }
    )
}

$failures = @(
    if ($corePackages.Count -ne 1) {
        "Expected one Battle.Core coverage package, found $($corePackages.Count)."
    }
    else {
        if (-not [string]::IsNullOrWhiteSpace($IntegrationResultsDirectory) -and
            $integrationCorePackages.Count -ne 1) {
            "Expected one integration Battle.Core coverage package, found $($integrationCorePackages.Count)."
        }

        $combinedLines = @{}
        foreach ($package in @($corePackages + $integrationCorePackages)) {
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
            "Battle.Core line coverage is $($coreLineRate * 100)%, expected at least 85%."
        }
    }

    foreach ($expectedClass in $expectedClasses) {
        $matches = @($classes | Where-Object { $_.name -eq $expectedClass })
        if ($matches.Count -ne 1) {
            "Expected one coverage entry for $expectedClass, found $($matches.Count)."
            continue
        }

        $branchRate = [decimal]::Parse(
            $matches[0].'branch-rate',
            [System.Globalization.CultureInfo]::InvariantCulture)
        if ($branchRate -ne 1) {
            "$expectedClass branch coverage is $($branchRate * 100)%, expected 100%."
        }
    }
)

if ($failures.Count -gt 0) {
    throw ($failures -join [Environment]::NewLine)
}

Write-Output 'WP-07 critical branch coverage: 100%; current combined Battle.Core line coverage: at least 85%.'
