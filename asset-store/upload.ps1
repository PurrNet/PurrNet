#!/usr/bin/env pwsh
#Requires -Version 7.0
param(
    [string]$Product = 'PurrNet',
    [string]$Ref,
    [string[]]$Unity,
    [switch]$Upload,
    [switch]$Validate,
    [switch]$List,
    [switch]$Offline,
    [switch]$NoFetch,
    [string]$EditorsRoot
)

$ErrorActionPreference = 'Stop'

$modules = 'ai', 'androidjni', 'animation', 'assetbundle', 'audio', 'cloth', 'director', 'imageconversion', 'imgui',
    'jsonserialize', 'particlesystem', 'physics', 'physics2d', 'screencapture', 'terrain', 'terrainphysics', 'tilemap',
    'ui', 'uielements', 'umbra', 'unityanalytics', 'unitywebrequest', 'unitywebrequestassetbundle',
    'unitywebrequestaudio', 'unitywebrequesttexture', 'unitywebrequestwww', 'vehicles', 'video', 'wind', 'xr'

$compileOnly = @{
    '2022' = @{ 'com.unity.ugui' = '1.0.0'; 'com.unity.textmeshpro' = '3.0.6' }
    '6000' = @{ 'com.unity.ugui' = '2.0.0' }
}

function Slash([string]$path) { $path.Replace('\', '/') }

$fetched = @{}
function Update-Repo([string]$path) {
    if ($NoFetch -or $fetched[$path]) { return }
    git -C $path fetch --quiet origin
    if ($LASTEXITCODE -ne 0) { throw "git fetch failed in $path." }
    git -C $path fetch --quiet origin '+refs/heads/release:refs/remotes/origin/release'
    if ($LASTEXITCODE -ne 0) { throw "Could not fetch the release branch of $path." }
    $fetched[$path] = $true
}

function Get-ReleaseTag([string]$path) {
    Update-Repo $path
    $tip = git -C $path rev-parse --verify --quiet 'refs/remotes/origin/release^{commit}'
    if (-not $tip) { throw "$path has no origin/release; run without -NoFetch or pass -Ref." }
    $tag = git -C $path tag --points-at $tip --list 'v*' | Where-Object { $_ -notmatch '-' } | Select-Object -First 1
    if (-not $tag) { throw "origin/release in $path ($($tip.Substring(0, 9))) has commits after its last release tag; pass -Ref to choose what to upload." }
    $tag
}

function Find-Editor([string]$version) {
    $root = $EditorsRoot
    if ($IsWindows) {
        if (-not $root) { $root = 'C:\Program Files\Unity\Hub\Editor' }
        $exe = Join-Path $root "$version/Editor/Unity.exe"
    }
    elseif ($IsMacOS) {
        if (-not $root) { $root = '/Applications/Unity/Hub/Editor' }
        $exe = Join-Path $root "$version/Unity.app/Contents/MacOS/Unity"
    }
    else {
        if (-not $root) { $root = Join-Path $HOME 'Unity/Hub/Editor' }
        $exe = Join-Path $root "$version/Editor/Unity"
    }
    if (-not (Test-Path $exe)) {
        $installed = (Get-ChildItem $root -Directory -ErrorAction SilentlyContinue | Select-Object -Expand Name) -join ', '
        throw "Unity $version is not installed under $root (installed: $installed). The store keeps one upload per exact editor build, so install that exact version."
    }
    $exe
}

$products = Get-Content (Join-Path $PSScriptRoot 'products.json') -Raw | ConvertFrom-Json
$config = $products.$Product
if (-not $config) { throw "Unknown product '$Product'. Known: $($products.PSObject.Properties.Name -join ', ')" }

$repo = (Resolve-Path (Join-Path $PSScriptRoot $config.repo)).Path
$toolsPackage = (Resolve-Path (Join-Path $PSScriptRoot '../Packages/com.unity.asset-store-tools')).Path
$workRoot = Join-Path $PSScriptRoot '../test-local/asset-store-upload'
$outDir = Join-Path $workRoot 'out'
New-Item -ItemType Directory -Force $outDir | Out-Null
$workRoot = (Resolve-Path $workRoot).Path
$outDir = (Resolve-Path $outDir).Path

Update-Repo $repo
if (-not $Ref) {
    $Ref = Get-ReleaseTag $repo
    Write-Host "   using $Ref, the tip of the release branch"
}
$commit = git -C $repo rev-parse --short "$Ref^{commit}"
if ($LASTEXITCODE -ne 0) { throw "Ref '$Ref' not found in $repo." }

if (-not $Unity) { $Unity = @($config.unity) }
$slots = @($config.unity) + $Unity | Select-Object -Unique

$failed = $false

foreach ($target in $Unity) {
    $unityExe = Find-Editor $target

    $project = Join-Path $workRoot "$Product-$target"
    $assets = Join-Path $project 'Assets'
    $pristine = Join-Path $project 'Source'
    foreach ($dir in $assets, $pristine) { if (Test-Path $dir) { Remove-Item $dir -Recurse -Force } }
    New-Item -ItemType Directory -Force $assets, $pristine, (Join-Path $project 'Packages') | Out-Null

    $tar = Join-Path $workRoot "$Product-$target.tar"
    git -C $repo archive --format=tar -o $tar $Ref -- $config.root "$($config.root).meta"
    if ($LASTEXITCODE -ne 0) { throw "git archive of '$($config.root)' at $Ref failed." }
    foreach ($destination in $project, $pristine) {
        tar -xf $tar -C $destination
        if ($LASTEXITCODE -ne 0) { throw 'Extracting the source archive failed.' }
    }
    Remove-Item $tar

    Copy-Item (Join-Path $PSScriptRoot 'Editor') (Join-Path $assets 'AssetStoreUploadCli') -Recurse

    $package = Get-Content (Join-Path $project "$($config.root)/package.json") -Raw | ConvertFrom-Json
    $version = $package.version

    $dependencies = [ordered]@{}
    $uploadDeps = @()
    foreach ($dep in $package.dependencies.PSObject.Properties) {
        if ($config.gitDependencies.($dep.Name)) { continue }
        $dependencies[$dep.Name] = $dep.Value
        $uploadDeps += $dep.Name
    }
    foreach ($git in $config.gitDependencies.PSObject.Properties) {
        $gitVersion = $package.dependencies.($git.Name)
        if (-not $gitVersion) { $gitVersion = (Get-ReleaseTag (Join-Path $PSScriptRoot $git.Value.repo)).TrimStart('v') }
        $dependencies[$git.Name] = $git.Value.url.Replace('{version}', $gitVersion)
        Write-Host "   compiling against $($git.Name) $gitVersion"
    }
    $extras = $compileOnly[$target.Substring(0, 4)]
    if ($extras) { foreach ($name in $extras.Keys) { if (-not $dependencies.Contains($name)) { $dependencies[$name] = $extras[$name] } } }
    foreach ($module in $modules) { $dependencies["com.unity.modules.$module"] = '1.0.0' }
    $packagesDir = Join-Path $project 'Packages'
    $dependencies['com.unity.asset-store-tools'] = 'file:' + (Slash ([IO.Path]::GetRelativePath($packagesDir, $toolsPackage)))

    @{ dependencies = $dependencies } | ConvertTo-Json | Set-Content (Join-Path $packagesDir 'manifest.json')
    Remove-Item (Join-Path $packagesDir 'packages-lock.json') -ErrorAction SilentlyContinue

    $fileName = "${Product}_${version}_unity$target.unitypackage"
    $output = Join-Path $outDir $fileName
    $result = Join-Path $project 'result.json'
    $log = Join-Path $project 'unity.log'
    Remove-Item $output, $result, $log -ErrorAction SilentlyContinue

    $params = @(
        '-asuRoot', $config.root,
        '-asuOutput', "../out/$fileName",
        '-asuResult', 'result.json',
        '-asuPristine', 'Source',
        '-asuPackageId', $config.packageId,
        '-asuSlots', ($slots -join ',')
    )
    if ($uploadDeps) { $params += '-asuDeps', ($uploadDeps -join ',') }
    if ($Upload) { $params += '-asuUpload' }
    if ($Validate) { $params += '-asuValidate' }
    if ($List) { $params += '-asuList' }
    if ($Offline) { $params += '-asuOffline' }
    if ($params | Where-Object { $_ -match '\s' }) { throw "Unity parameters must not contain whitespace: $($params -join ' ')" }

    Write-Host "== $Product $version ($Ref @ $commit) on Unity $target$(if ($Upload) { ' [UPLOAD]' } else { ' [dry run]' })"

    $unityArgs = @(
        '-batchmode', '-nographics', '-projectPath', "`"$project`"", '-logFile', "`"$log`"",
        '-executeMethod', 'AssetStoreTools.Cli.AssetStoreUploadCli.Run'
    ) + $params
    $process = Start-Process $unityExe -ArgumentList $unityArgs -PassThru
    $null = $process.Handle
    $process.WaitForExit()

    if (Test-Path $log) {
        Select-String -Path $log -Pattern '^\[asset-store-upload\]' | ForEach-Object { Write-Host "   $($_.Line.Substring(21))" }
    }

    if (-not (Test-Path $result)) {
        $failed = $true
        Write-Host "   Unity exited with code $($process.ExitCode) before the upload script ran. Log: $log"
        if (Test-Path $log) {
            Select-String -Path $log -Pattern 'error CS\d+|Scripts have compiler errors|No valid Unity Editor license' |
                Select-Object -First 20 | ForEach-Object { Write-Host "   $($_.Line)" }
        }
        continue
    }

    $summary = Get-Content $result -Raw | ConvertFrom-Json
    if ($process.ExitCode -ne 0 -or $summary.error) {
        $failed = $true
        Write-Host "   FAILED: $($summary.error)"
    }
    Write-Host "   result: $result"
}

if ($failed) { exit 1 }
