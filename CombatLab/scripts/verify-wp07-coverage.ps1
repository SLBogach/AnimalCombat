param(
    [Parameter(Mandatory = $true)]
    [string]$ResultsDirectory
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

$failures = @(
    if ($corePackages.Count -ne 1) {
        "Expected one Battle.Core coverage package, found $($corePackages.Count)."
    }
    else {
        $coreLineRate = [decimal]::Parse(
            $corePackages[0].'line-rate',
            [System.Globalization.CultureInfo]::InvariantCulture)
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

Write-Output 'WP-07 critical branch coverage: 100%; Battle.Core line coverage: at least 85%.'
