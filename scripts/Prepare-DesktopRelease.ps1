[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string] $Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Remove-AbsolutePdbPaths {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    $bytes = [IO.File]::ReadAllBytes($Path)
    $singleByteText = [Text.Encoding]::GetEncoding(28591).GetString($bytes)
    $absolutePdbPattern = '(?i)(?:[A-Z]:\\|/(?:home|Users|build|workspace|__w)/)[A-Za-z0-9_. +:/\\-]{1,768}\.pdb'
    $matches = @([regex]::Matches($singleByteText, $absolutePdbPattern))

    foreach ($match in $matches) {
        $fileName = $match.Value -replace '^.*[\\/]', ''
        $replacement = [Text.Encoding]::ASCII.GetBytes($fileName)
        if ($replacement.Length -gt $match.Length) {
            throw "PDB filename exceeds the matched path length in $Path."
        }

        [Array]::Clear($bytes, $match.Index, $match.Length)
        [Array]::Copy($replacement, 0, $bytes, $match.Index, $replacement.Length)
    }

    [IO.File]::WriteAllBytes($Path, $bytes)
    $remainingText = [Text.Encoding]::GetEncoding(28591).GetString($bytes)
    if ([regex]::IsMatch($remainingText, $absolutePdbPattern)) {
        throw "An absolute PDB path remains in $Path."
    }

    return $matches.Count
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$desktopArtifactRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts/desktop'))
$releaseArtifactRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts/release'))
$releasePrefix = $releaseArtifactRoot.TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$releaseDirectory = [IO.Path]::GetFullPath((Join-Path $releaseArtifactRoot $Version))

if (-not $releaseDirectory.StartsWith($releasePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Release output resolved outside the release artifact directory.'
}

if (Test-Path -LiteralPath $releaseDirectory) {
    $resolvedReleaseDirectory = (Resolve-Path -LiteralPath $releaseDirectory).Path
    if (-not $resolvedReleaseDirectory.StartsWith($releasePrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Existing release output resolved outside the release artifact directory.'
    }

    Remove-Item -LiteralPath $resolvedReleaseDirectory -Recurse -Force
}

& (Join-Path $PSScriptRoot 'Publish-Desktop.ps1')
if ($LASTEXITCODE -ne 0) {
    throw 'Desktop publishing failed.'
}

New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
$assetBaseName = "BambuLab.X1Camera.Desktop-$Version"
$windowsSource = Join-Path $desktopArtifactRoot 'win-x64/BambuLab.X1Camera.Desktop.exe'
$linuxSource = Join-Path $desktopArtifactRoot 'linux-x64/BambuLab.X1Camera.Desktop'
$windowsAsset = Join-Path $releaseDirectory "$assetBaseName-win-x64.exe"
$linuxAsset = Join-Path $releaseDirectory "$assetBaseName-linux-x64"

foreach ($source in @($windowsSource, $linuxSource)) {
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Expected single-file executable was not produced: $source"
    }

    if ((Get-Item -LiteralPath $source).Length -lt 1MB) {
        throw "Published executable is unexpectedly small: $source"
    }
}

Copy-Item -LiteralPath $windowsSource -Destination $windowsAsset
Copy-Item -LiteralPath $linuxSource -Destination $linuxAsset
$removedWindowsPdbPaths = Remove-AbsolutePdbPaths -Path $windowsAsset
$removedLinuxPdbPaths = Remove-AbsolutePdbPaths -Path $linuxAsset
Write-Verbose "Removed $removedWindowsPdbPaths absolute PDB paths from the Windows asset."
Write-Verbose "Removed $removedLinuxPdbPaths absolute PDB paths from the Linux asset."

$noticeStaging = Join-Path $releaseDirectory '_notices'
New-Item -ItemType Directory -Path $noticeStaging -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination $noticeStaging
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'THIRD-PARTY-NOTICES.md') -Destination $noticeStaging

foreach ($runtime in @('win-x64', 'linux-x64')) {
    $runtimeNoticeSource = Join-Path $desktopArtifactRoot "$runtime/licenses"
    if (-not (Test-Path -LiteralPath $runtimeNoticeSource -PathType Container)) {
        throw "Required renderer notices were not published for $runtime."
    }

    $runtimeNoticeDestination = Join-Path $noticeStaging $runtime
    New-Item -ItemType Directory -Path $runtimeNoticeDestination -Force | Out-Null
    Copy-Item -LiteralPath $runtimeNoticeSource -Destination $runtimeNoticeDestination -Recurse
}

$noticesAsset = Join-Path $releaseDirectory "$assetBaseName-notices.zip"
Compress-Archive -Path (Join-Path $noticeStaging '*') -DestinationPath $noticesAsset -CompressionLevel Optimal
Remove-Item -LiteralPath $noticeStaging -Recurse -Force

$checksumAssets = @($windowsAsset, $linuxAsset, $noticesAsset)
$checksumLines = foreach ($asset in $checksumAssets) {
    $hash = (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($asset))"
}

$checksumPath = Join-Path $releaseDirectory 'SHA256SUMS.txt'
[IO.File]::WriteAllLines($checksumPath, $checksumLines, [Text.UTF8Encoding]::new($false))

$releaseAssets = @(Get-ChildItem -LiteralPath $releaseDirectory -File)
if ($releaseAssets.Count -ne 4) {
    throw "Expected four release assets, found $($releaseAssets.Count)."
}

$releaseAssets | Sort-Object Name | Select-Object Name, Length
