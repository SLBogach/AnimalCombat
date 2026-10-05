param(
    [Parameter(Mandatory = $true)][string]$CoreResultsDirectory,
    [Parameter(Mandatory = $true)][string]$IntegrationResultsDirectory,
    [Parameter(Mandatory = $true)][string]$ReplayResultsDirectory
)
$ErrorActionPreference = 'Stop'
$cultureWp10 = [Globalization.CultureInfo]::InvariantCulture
function Read-Wp10Coverage([string]$directory, [string]$packageName) {
    $files = @(Get-ChildItem -LiteralPath $directory -Recurse -Filter 'coverage.cobertura.xml' -File)
    if ($files.Count -ne 1) { throw "Expected exactly one fresh coverage report under '$directory', found $($files.Count)." }
    [xml]$document = Get-Content -LiteralPath $files[0].FullName -Raw
    $packages = @($document.coverage.packages.package | Where-Object { $_.name -eq $packageName })
    if ($packages.Count -ne 1) { throw "Expected one '$packageName' coverage package." }
    return $packages[0]
}
$coreWp10 = Read-Wp10Coverage $CoreResultsDirectory 'Battle.Core'
$integrationWp10 = Read-Wp10Coverage $IntegrationResultsDirectory 'Battle.Core'
$replayWp10 = Read-Wp10Coverage $ReplayResultsDirectory 'Battle.Replay'
# Full classes, not hand-picked already-green methods. Include the compiler-generated
# queue/closure/iterator branches belonging to each critical implementation.
$criticalCoreWp10 = @(
    'Battle.Core.Effects.EffectStatMath',
    'Battle.Core.Effects.EffectArithmeticProof',
    'Battle.Core.Effects.EffectConsumerArithmeticProof',
    'Battle.Core.Effects.EffectStore',
    'Battle.Core.Effects.EffectTriggerQueue',
    'Battle.Core.Effects.EffectControlMath',
    'Battle.Core.Effects.KnockdownTimeline',
    'Battle.Core.Effects.EffectControlSystem',
    'Battle.Core.Effects.EffectDecisionView',
    'Battle.Core.Effects.EffectRuntime',
    'Battle.Core.Engine.AtomicBattleBatch'
)
$failuresWp10 = [Collections.Generic.List[string]]::new()
function Assert-Wp10Critical([object]$package, [string[]]$names) {
    foreach ($name in $names) {
        $exact = @($package.classes.class | Where-Object { $_.name -eq $name })
        if ($exact.Count -ne 1) { $failuresWp10.Add("Expected one critical class '$name', found $($exact.Count)."); continue }
        if ([decimal]::Parse($exact[0].'line-rate', $cultureWp10) -le 0) { $failuresWp10.Add("Critical class '$name' was not executed.") }
        $entries = @($package.classes.class | Where-Object {
            $_.name -eq $name -or $_.name.StartsWith($name + '/', [StringComparison]::Ordinal)
        })
        foreach ($entry in $entries) {
            $rate = [decimal]::Parse($entry.'branch-rate', $cultureWp10)
            if ($rate -ne 1) { $failuresWp10.Add("$($entry.name) branch coverage is $($rate * 100)%, expected 100%.") }
        }
    }
}
Assert-Wp10Critical $coreWp10 $criticalCoreWp10
Assert-Wp10Critical $replayWp10 @('Battle.Replay.Verification.EffectReplaySemanticValidator')
$runtimeWp10 = @($integrationWp10.classes.class | Where-Object { $_.name -eq 'Battle.Core.Effects.EffectRuntime' })
if ($runtimeWp10.Count -ne 1 -or [decimal]::Parse($runtimeWp10[0].'line-rate', $cultureWp10) -le 0) {
    $failuresWp10.Add('Full-loop integration coverage must execute the production effect runtime.')
}
$linesWp10 = @{}
foreach ($packageWp10 in @($coreWp10, $integrationWp10)) {
    foreach ($classWp10 in $packageWp10.classes.class) {
        foreach ($lineWp10 in $classWp10.lines.line) {
            $keyWp10 = $classWp10.filename + ':' + $lineWp10.number
            $linesWp10[$keyWp10] = ($linesWp10[$keyWp10] -eq $true) -or ([long]$lineWp10.hits -gt 0)
        }
    }
}
if ($linesWp10.Count -eq 0) { throw 'No instrumented Battle.Core lines found.' }
$lineRateWp10 = [decimal]@($linesWp10.Values | Where-Object { $_ -eq $true }).Count / $linesWp10.Count
if ($lineRateWp10 -lt [decimal]0.85) { $failuresWp10.Add("Combined Battle.Core line coverage is $($lineRateWp10 * 100)%, expected at least 85%.") }
if ($failuresWp10.Count -gt 0) { throw ($failuresWp10 -join [Environment]::NewLine) }
Write-Output "WP-10 critical effect/queue/control/atomic/replay branch coverage: 100%; combined Battle.Core line coverage: $([Math]::Round($lineRateWp10 * 100, 2))%."
