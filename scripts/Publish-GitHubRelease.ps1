[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[^/]+/[^/]+$')]
    [string] $Repository,

    [Parameter(Mandatory)]
    [ValidatePattern('^v\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string] $TagName,

    [Parameter(Mandatory)]
    [string] $GitHubToken
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$version = $TagName.Substring(1)
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$assetDirectory = Join-Path $repositoryRoot "artifacts/release/$version"
$assetBaseName = "BambuLab.X1Camera.Desktop-$version"
$expectedAssetNames = @(
    "$assetBaseName-linux-x64"
    "$assetBaseName-notices.zip"
    "$assetBaseName-win-x64.exe"
    'SHA256SUMS.txt'
)

if (-not (Test-Path -LiteralPath $assetDirectory -PathType Container)) {
    throw "Release asset directory does not exist: $assetDirectory"
}

$assets = @(Get-ChildItem -LiteralPath $assetDirectory -File | Sort-Object Name)
$actualAssetNames = @($assets.Name)
if ($assets.Count -ne $expectedAssetNames.Count -or
    (Compare-Object -ReferenceObject $expectedAssetNames -DifferenceObject $actualAssetNames)) {
    throw "Release assets do not match the expected set: $($expectedAssetNames -join ', ')."
}

$apiRoot = "https://api.github.com/repos/$Repository"
$headers = @{
    Accept = 'application/vnd.github+json'
    Authorization = "Bearer $GitHubToken"
    'X-GitHub-Api-Version' = '2022-11-28'
    'User-Agent' = 'BambuX1Camera-release-workflow'
}
$releaseBody = @'
This prerelease publishes the reusable `BambuLab.X1Camera` and
`BambuLab.X1Camera.Imaging` NuGet packages plus the Avalonia sample application.

The desktop downloads are self-contained, standalone single-file executables:

- Windows x64: download the `.exe` and run it directly.
- Linux x64: download the extensionless binary, run `chmod +x` on it, then launch it from a graphical X11 or XWayland desktop.

Avalonia is used only by the sample application and is not a dependency of either NuGet package. Required project and native-renderer notices are provided in the separate notices archive. Verify downloads with `SHA256SUMS.txt`.
'@
$releasePayload = @{
    tag_name = $TagName
    name = $TagName
    body = $releaseBody
    draft = $false
    prerelease = $true
    generate_release_notes = $true
    make_latest = 'false'
} | ConvertTo-Json

$encodedTag = [Uri]::EscapeDataString($TagName)
try {
    $release = Invoke-RestMethod `
        -Method Get `
        -Uri "$apiRoot/releases/tags/$encodedTag" `
        -Headers $headers
}
catch {
    $response = if ($_.Exception.PSObject.Properties.Name -contains 'Response') {
        $_.Exception.Response
    }
    else {
        $null
    }
    $statusCode = if ($null -eq $response) { 0 } else { [int] $response.StatusCode }
    if ($statusCode -ne 404) {
        throw
    }

    $release = Invoke-RestMethod `
        -Method Post `
        -Uri "$apiRoot/releases" `
        -Headers $headers `
        -ContentType 'application/json' `
        -Body $releasePayload
}

$uploadRoot = $release.upload_url -replace '\{\?name,label\}$', ''
foreach ($asset in $assets) {
    $existingAsset = @($release.assets | Where-Object { $_.name -eq $asset.Name }) |
        Select-Object -First 1
    if ($existingAsset) {
        Invoke-RestMethod `
            -Method Delete `
            -Uri "$apiRoot/releases/assets/$($existingAsset.id)" `
            -Headers $headers | Out-Null
    }

    $contentType = switch ($asset.Extension) {
        '.zip' { 'application/zip' }
        '.txt' { 'text/plain' }
        default { 'application/octet-stream' }
    }
    $assetName = [Uri]::EscapeDataString($asset.Name)
    $uploadUri = '{0}?name={1}' -f $uploadRoot, $assetName
    Invoke-RestMethod `
        -Method Post `
        -Uri $uploadUri `
        -Headers $headers `
        -ContentType $contentType `
        -InFile $asset.FullName | Out-Null
}

Write-Host "Published $($assets.Count) assets to $($release.html_url)."
