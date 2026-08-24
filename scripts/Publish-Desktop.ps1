[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet('win-x64', 'linux-x64')]
    [string[]] $RuntimeIdentifier = @('win-x64', 'linux-x64')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $repositoryRoot 'samples/BambuLab.X1Camera.Desktop/BambuLab.X1Camera.Desktop.csproj'
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts/desktop'))
$artifactPrefix = $artifactRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar

foreach ($runtime in $RuntimeIdentifier) {
    $outputPath = [IO.Path]::GetFullPath((Join-Path $artifactRoot $runtime))
    if (-not $outputPath.StartsWith($artifactPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Publish output resolved outside the desktop artifact directory."
    }

    if (Test-Path -LiteralPath $outputPath) {
        $resolvedOutput = (Resolve-Path -LiteralPath $outputPath).Path
        if (-not $resolvedOutput.StartsWith($artifactPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Existing publish output resolved outside the desktop artifact directory."
        }

        Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
    }

    & dotnet publish $projectPath `
        --configuration Release `
        --runtime $runtime `
        --self-contained true `
        -p:ContinuousIntegrationBuild=true `
        --output $outputPath
    if ($LASTEXITCODE -ne 0) {
        throw "Desktop publish failed for $runtime."
    }
}
