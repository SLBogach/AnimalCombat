param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$combatLabRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$temporaryParent = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$temporaryRoot = Join-Path `
    $temporaryParent `
    ("combatlab-wp06-targets-" + [Guid]::NewGuid().ToString("N"))
$temporaryRoot = [System.IO.Path]::GetFullPath($temporaryRoot)

if (-not $temporaryRoot.StartsWith(
        $temporaryParent,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to use a temporary directory outside the OS temp root."
}

$probeProject = Join-Path $combatLabRoot "tools/Wp06.TargetProbe/Wp06.TargetProbe.csproj"
$assemblies = @(
    "Battle.Contracts",
    "Battle.Core",
    "Battle.Config",
    "Battle.Replay"
)
$targets = @("netstandard2.1", "net10.0")
$results = @{}
New-Item -ItemType Directory -Path $temporaryRoot | Out-Null

try {
    dotnet restore $probeProject --locked-mode
    if ($LASTEXITCODE -ne 0) {
        throw "WP-06 target probe restore failed with exit code $LASTEXITCODE."
    }

    foreach ($target in $targets) {
        foreach ($assembly in $assemblies) {
            $assemblyPath = Join-Path `
                $combatLabRoot `
                "src/$assembly/bin/$Configuration/$target/$assembly.dll"
            if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
                throw "Missing $target assembly '$assemblyPath'. Build CombatLab.sln first."
            }
        }
    }

    foreach ($target in $targets) {
        $outputDirectory = Join-Path $temporaryRoot $target
        dotnet build `
            $probeProject `
            --configuration $Configuration `
            --no-restore `
            --output $outputDirectory `
            --property:CombatTarget=$target `
            /nodeReuse:false
        if ($LASTEXITCODE -ne 0) {
            throw "WP-06 $target probe build failed with exit code $LASTEXITCODE."
        }

        $probeAssembly = Join-Path $outputDirectory "Wp06.TargetProbe.dll"
        $result = (& dotnet $probeAssembly $combatLabRoot $target | Out-String).Trim()
        if ($LASTEXITCODE -ne 0) {
            throw "WP-06 $target probe execution failed with exit code $LASTEXITCODE."
        }

        if ([string]::IsNullOrWhiteSpace($result)) {
            throw "WP-06 $target probe produced no canonical result."
        }

        $results[$target] = $result
    }

    if (-not [System.StringComparer]::Ordinal.Equals(
            $results["netstandard2.1"],
            $results["net10.0"])) {
        throw "WP-06 target determinism failed: netstandard2.1 and net10.0 replay bytes differ."
    }

    Write-Output "WP-06 target determinism: canonical wait_equal_l1 replay bytes match."
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
