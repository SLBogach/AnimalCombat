param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$rootWp10 = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$tempParentWp10 = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$tempWp10 = [IO.Path]::GetFullPath((Join-Path $tempParentWp10 ('combatlab-wp10-targets-' + [Guid]::NewGuid().ToString('N'))))
$prefixWp10 = $tempParentWp10.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $tempWp10.StartsWith($prefixWp10, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe temp path.' }
$projectWp10 = Join-Path $rootWp10 'tools/Wp06.TargetProbe/Wp06.TargetProbe.csproj'
$manifestWp10 = Get-Content (Join-Path $rootWp10 'fixtures/replay/v0.1/wp10.engine-0.5.0.manifest.json') -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Path $tempWp10 | Out-Null
try {
    dotnet restore $projectWp10 --locked-mode --disable-build-servers
    if ($LASTEXITCODE -ne 0) { throw 'Probe restore failed.' }
    foreach ($targetWp10 in @('netstandard2.1','net10.0')) {
        $outputWp10 = Join-Path $tempWp10 $targetWp10
        dotnet build $projectWp10 --configuration $Configuration --no-restore --disable-build-servers -p:CombatTarget=$targetWp10 --output $outputWp10
        if ($LASTEXITCODE -ne 0) { throw "Probe build failed: $targetWp10" }
        foreach ($entryWp10 in $manifestWp10.scenarios) {
            $actualWp10 = Join-Path $outputWp10 $entryWp10.replay
            dotnet (Join-Path $outputWp10 'Wp06.TargetProbe.dll') $rootWp10 $targetWp10 ('wp10:' + $entryWp10.scenario) $actualWp10
            if ($LASTEXITCODE -ne 0) { throw "Probe execution failed: $targetWp10/$($entryWp10.scenario)" }
            $hashWp10 = (Get-FileHash -LiteralPath $actualWp10 -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($hashWp10 -cne $entryWp10.file_sha256) { throw "Golden mismatch: $targetWp10/$($entryWp10.scenario)" }
            $goldenWp10 = Join-Path $rootWp10 ('fixtures/replay/v0.1/' + $entryWp10.replay)
            if ((Get-FileHash -LiteralPath $goldenWp10 -Algorithm SHA256).Hash.ToLowerInvariant() -cne $entryWp10.file_sha256) { throw 'Committed golden stale.' }
        }
    }
    Write-Output 'WP10 nine goldens match actual netstandard2.1/net10.0 dependencies; loaded TFMs asserted by probe.'
}
finally {
    if ([IO.Path]::GetFullPath($tempWp10).StartsWith($prefixWp10, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $tempWp10)) {
        Remove-Item -LiteralPath $tempWp10 -Recurse -Force
    }
}
