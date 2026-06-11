param(
    [string]$HostBase,
    [string]$Owner,
    [string]$Repo,
    [string]$Tag,
    [string]$TargetAbi = "10.11.8.0",
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Normalize-Version {
    param([Parameter(Mandatory = $true)][string]$Version)

    $segments = $Version.Split('.')
    if ($segments.Count -gt 4) {
        throw "Version '$Version' has more than 4 segments."
    }

    while ($segments.Count -lt 4) {
        $segments += "0"
    }

    return ($segments -join '.')
}

function Increment-Version {
    param([Parameter(Mandatory = $true)][string]$Version)

    $normalized = Normalize-Version -Version $Version
    $segments = $normalized.Split('.')
    $segments[3] = [int]$segments[3] + 1
    return ($segments -join '.')
}

function Get-RemoteReleaseInfo {
    param([string]$RepoRoot)

    $origin = $null
    try {
        $origin = git -C $RepoRoot remote get-url origin 2>$null
    }
    catch {
        $origin = $null
    }

    if ([string]::IsNullOrWhiteSpace($origin)) {
        return [PSCustomObject]@{
            HostBase = "https://github.com"
            Owner    = "c2bw"
            Repo     = "jellyfin-plugin-jellych"
        }
    }

    $trimmed = $origin.Trim()
    if ($trimmed -match '^https?://(?<host>[^/]+)/(?<owner>[^/]+)/(?<repo>[^/]+?)(?:\.git)?$') {
        return [PSCustomObject]@{
            HostBase = "https://$($Matches.host)"
            Owner    = $Matches.owner
            Repo     = $Matches.repo
        }
    }

    if ($trimmed -match '^(?:ssh://)?git@(?<host>[^/:]+)[:/](?<owner>[^/]+)/(?<repo>[^/]+?)(?:\.git)?$') {
        return [PSCustomObject]@{
            HostBase = "https://$($Matches.host)"
            Owner    = $Matches.owner
            Repo     = $Matches.repo
        }
    }

    throw "Could not parse origin URL '$trimmed'. Provide -HostBase, -Owner, and -Repo explicitly."
}

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $repoRoot

$propsPath = Join-Path $repoRoot "Directory.Build.props"
$csprojPath = Join-Path $repoRoot "Jellyfin.Plugin.Jellych\Jellyfin.Plugin.Jellych.csproj"
$solutionPath = Join-Path $repoRoot "Jellyfin.Plugin.Jellych.sln"
$manifestPath = Join-Path $repoRoot "manifest.json"
$pluginManifestPath = Join-Path $repoRoot "Jellyfin.Plugin.Jellych\manifest.xml"
$dllPath = Join-Path $repoRoot "Jellyfin.Plugin.Jellych\bin\Release\net9.0\Jellych.WebhookPlugin.dll"
$distDir = Join-Path $repoRoot "dist"

if (-not (Test-Path $solutionPath)) {
    throw "Could not find solution file at $solutionPath"
}

if (-not (Test-Path $csprojPath)) {
    throw "Could not find project file at $csprojPath"
}

if (-not (Test-Path $manifestPath)) {
    throw "Could not find manifest at $manifestPath"
}

if (-not (Test-Path $pluginManifestPath)) {
    throw "Could not find plugin manifest at $pluginManifestPath"
}

[xml]$props = Get-Content -Path $propsPath
$rawVersion = $props.Project.PropertyGroup.Version | Select-Object -First 1

if ([string]::IsNullOrWhiteSpace($rawVersion)) {
    [xml]$csproj = Get-Content -Path $csprojPath
    $rawVersion = $csproj.Project.PropertyGroup.Version | Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($rawVersion)) {
    throw "Version is missing in $propsPath and $csprojPath"
}

$pluginVersion = Normalize-Version -Version $rawVersion.Trim()

# Increment the patch version for the new release
$nextVersion = Increment-Version -Version $pluginVersion

# Update Directory.Build.props with the new version
$props.Project.PropertyGroup.Version = $nextVersion
$props.Project.PropertyGroup.AssemblyVersion = $nextVersion
$props.Project.PropertyGroup.FileVersion = $nextVersion
$props.Save($propsPath)
Write-Host "Updated version in Directory.Build.props to $nextVersion"

$pluginVersion = $nextVersion

$remoteInfo = Get-RemoteReleaseInfo -RepoRoot $repoRoot
if ([string]::IsNullOrWhiteSpace($HostBase)) {
    $HostBase = $remoteInfo.HostBase
}
if ([string]::IsNullOrWhiteSpace($Owner)) {
    $Owner = $remoteInfo.Owner
}
if ([string]::IsNullOrWhiteSpace($Repo)) {
    $Repo = $remoteInfo.Repo
}

$HostBase = $HostBase.TrimEnd('/')

if ([string]::IsNullOrWhiteSpace($Tag)) {
    $Tag = "v$pluginVersion"
}

$zipName = "$TargetAbi-jellychwebhook_$pluginVersion.zip"
$zipPath = Join-Path $distDir $zipName
if ($HostBase -eq "https://github.com") {
    $sourceUrl = "https://raw.githubusercontent.com/$Owner/$Repo/$pluginVersion/dist/$zipName"
}
else {
    $sourceUrl = "$HostBase/$Owner/$Repo/-/raw/$pluginVersion/dist/$zipName"
}

[xml]$pluginManifest = Get-Content -Path $pluginManifestPath
$pluginManifestEntry = $pluginManifest.SelectSingleNode("/PluginManifest/manifest")
if ($null -eq $pluginManifestEntry) {
    throw "Could not find plugin manifest entry in $pluginManifestPath"
}

$pluginManifestEntry.SelectSingleNode("version").InnerText = $pluginVersion
$pluginManifestEntry.SelectSingleNode("targetAbi").InnerText = $TargetAbi
$pluginManifestEntry.SelectSingleNode("url").InnerText = "$HostBase/$Owner/$Repo"
$pluginManifest.Save($pluginManifestPath)
Write-Host "Updated plugin manifest: $pluginManifestPath"

Write-Host "Plugin version: $pluginVersion"
Write-Host "Tag: $Tag"
Write-Host "Target ABI: $TargetAbi"
Write-Host "Release host: $HostBase"
Write-Host "Repository: $Owner/$Repo"

if (-not $SkipBuild) {
    Write-Host "Building plugin..."
    dotnet clean $solutionPath -c Release | Out-Host
    dotnet restore $solutionPath | Out-Host
    dotnet build $solutionPath -c Release --no-restore | Out-Host
}

if (-not (Test-Path $dllPath)) {
    throw "Built DLL not found at $dllPath"
}

New-Item -ItemType Directory -Path $distDir -Force | Out-Null
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Compress-Archive -Path $dllPath -DestinationPath $zipPath -Force
$checksum = (Get-FileHash -Path $zipPath -Algorithm MD5).Hash.ToLowerInvariant()
$timestamp = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

Write-Host "Zip created: $zipPath"
Write-Host "MD5: $checksum"

$manifestRaw = Get-Content -Path $manifestPath -Raw | ConvertFrom-Json
$manifestList = @($manifestRaw)
if ($manifestList.Count -lt 1) {
    throw "manifest.json must contain at least one plugin entry."
}

$plugin = $manifestList[0]
if ($null -eq $plugin.versions) {
    $plugin | Add-Member -MemberType NoteProperty -Name versions -Value @()
}

$existing = $plugin.versions | Where-Object { $_.version -eq $pluginVersion }

if ($null -ne $existing) {
    $existing.changelog = "Release $pluginVersion"
    $existing.targetAbi = $TargetAbi
    $existing.sourceUrl = $sourceUrl
    $existing.checksum = $checksum
    $existing.timestamp = $timestamp
    Write-Host "Updated existing manifest version entry: $pluginVersion"
}
else {
    $newEntry = [PSCustomObject]@{
        version   = $pluginVersion
        changelog = "Release $pluginVersion"
        targetAbi = $TargetAbi
        sourceUrl = $sourceUrl
        checksum  = $checksum
        timestamp = $timestamp
    }

    $plugin.versions = @($newEntry) + @($plugin.versions)
    Write-Host "Added new manifest version entry: $pluginVersion"
}

ConvertTo-Json -InputObject $manifestList -Depth 20 | Set-Content -Path $manifestPath -Encoding utf8

Write-Host "manifest.json updated: $manifestPath"
Write-Host "Next step: upload $zipName to release tag $Tag"
