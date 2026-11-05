# OrbusWeaponAdjustments -- build locally using generated BepInEx interop,
# then publish a stable DLL release from the EXISTING GitHub repository.
# Usage: .\release.ps1   |   .\release.ps1 -BuildOnly   |   .\release.ps1 -GameDir "C:\...\OrbusInstance"
param(
    [string]$GameDir = '',
    [switch]$BuildOnly
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$repo = 'Hor1zonVR/OrbusWeaponAdjustments'
$root = $PSScriptRoot
$project = Join-Path $root 'src\OrbusWeaponAdjustments\OrbusWeaponAdjustments.csproj'
$plugin = Join-Path $root 'src\OrbusWeaponAdjustments\Plugin.cs'
$dll = Join-Path $root 'src\OrbusWeaponAdjustments\bin\Release\net6.0\OrbusWeaponAdjustments.dll'

function CheckExit([string]$step) {
    if ($LASTEXITCODE -ne 0) { throw "$step failed (exit $LASTEXITCODE)." }
}

try {
    Write-Host ''
    Write-Host '====================================================' -ForegroundColor Cyan
    Write-Host ' OrbusWeaponAdjustments -> GitHub Releases -> RM' -ForegroundColor Cyan
    Write-Host ' v0.2.1 stable character scope and native preset visibility fix' -ForegroundColor Yellow
    Write-Host '====================================================' -ForegroundColor Cyan
    Write-Host ''

    if (!(Test-Path $project) -or !(Test-Path $plugin)) {
        throw 'Run this from the full repository clone. Missing project files.'
    }
    foreach ($command in @('dotnet','git')) {
        if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
            throw "Missing command: $command"
        }
    }

    $source = Get-Content -LiteralPath $plugin -Raw
    $match = [regex]::Match($source, 'BepInPlugin\(\s*"[^"]+"\s*,\s*"[^"]+"\s*,\s*"(?<version>\d+\.\d+\.\d+)"')
    if (-not $match.Success) { throw 'Cannot parse version from BepInPlugin attribute.' }
    $tag = 'v' + $match.Groups['version'].Value

    if ([string]::IsNullOrWhiteSpace($GameDir)) {
        $base = Join-Path $env:LOCALAPPDATA 'OrbusRebornModdingInstances'
        if (!(Test-Path $base)) { throw "No RM instances found: $base" }
        $dirs = @(Get-ChildItem -LiteralPath $base -Directory)
        $ordered = @()
        foreach ($name in @('My Orbus Renewed','Modded Orbus')) {
            $ordered += @($dirs | Where-Object { $_.Name -eq $name })
        }
        $ordered += @($dirs | Where-Object { $_.Name -notin @('My Orbus Renewed','Modded Orbus') })
        $candidate = $ordered | Where-Object {
            Test-Path (Join-Path $_.FullName 'BepInEx\interop\UnityEngine.InputModule.dll')
        } | Select-Object -First 1
        if ($null -eq $candidate) { throw "No instance has BepInEx generated interop. Launch an RM instance once or supply -GameDir." }
        $GameDir = $candidate.FullName
    }
    $GameDir = [System.IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($GameDir.Trim().Trim('"')))
    foreach ($file in @(
        'BepInEx\core\BepInEx.Core.dll',
        'BepInEx\core\BepInEx.Unity.IL2CPP.dll',
        'BepInEx\core\Il2CppInterop.Runtime.dll',
        'BepInEx\core\0Harmony.dll',
        'BepInEx\interop\Il2Cppmscorlib.dll',
        'BepInEx\interop\UnityEngine.CoreModule.dll',
        'BepInEx\interop\UnityEngine.InputModule.dll'
    )) {
        if (!(Test-Path (Join-Path $GameDir $file) -PathType Leaf)) {
            throw "Missing compile dependency in selected Orbus instance: $file"
        }
    }

    Write-Host "Building $tag against: $GameDir" -ForegroundColor Cyan
    & dotnet build $project -c Release --no-incremental "-p:GameDir=$GameDir"
    CheckExit 'Building weapon preset DLL'
    if (!(Test-Path $dll -PathType Leaf)) { throw "Compiled DLL missing: $dll" }
    Write-Host "Compiled: $dll" -ForegroundColor Green

    if ($BuildOnly) {
        Write-Host 'BuildOnly selected: not publishing a GitHub release.' -ForegroundColor Yellow
        return
    }

    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw "GitHub CLI missing. Install gh, then run gh auth login."
    }
    & gh auth status
    CheckExit 'GitHub authentication'
    $login = (& gh api user --jq '.login').Trim()
    CheckExit 'Checking GitHub account'
    if ($login -ne 'Hor1zonVR') {
        throw "GitHub CLI is logged in as '$login' instead of Hor1zonVR."
    }

    Push-Location $root
    try {
        if (!(Test-Path '.git' -PathType Container)) {
            throw 'Not a git clone. Run gh repo clone Hor1zonVR/OrbusWeaponAdjustments first.'
        }
        $origin = (& git remote get-url origin).Trim()
        CheckExit 'Reading origin remote'
        if ($origin -notmatch '[/\\:]Hor1zonVR/OrbusWeaponAdjustments(\.git)?$') {
            throw "Wrong GitHub origin: $origin"
        }
        $branch = (& git branch --show-current).Trim()
        CheckExit 'Checking branch'
        if ($branch -ne 'main') { throw "Publish from main, not $branch." }

        & git fetch origin main --tags
        CheckExit 'Fetching GitHub history'
        & git add .gitignore README.md release.ps1 'Publish to RM.cmd' src
        CheckExit 'Staging mod sources'
        $files = @(& git diff --cached --name-only)
        CheckExit 'Checking staged files'
        $unsafe = @($files | Where-Object {
            $_ -match '(?i)(^|/)(BepInEx|interop|\.build-refs)/|\.(dll|zip|pem|key|env)$'
        })
        if ($unsafe.Count -gt 0) {
            & git reset
            throw ('Refusing to commit binaries/secrets: ' + ($unsafe -join ', '))
        }
        if ($files.Count -gt 0) {
            & git commit -m "WeaponAdjustments sources $tag"
            CheckExit 'Committing updated mod sources'
        }
        & git push origin main
        CheckExit 'Pushing mod sources to GitHub'

        # Publishing is idempotent: a failed GitHub API call may nevertheless
        # have created a release or a draft. Always check before another
        # create attempt, and leave existing assets untouched.
        $publishedTags = @(& gh release list --repo $repo --limit 100 --json tagName --jq '.[].tagName')
        CheckExit 'Checking existing GitHub release tags'
        $releaseExists = $publishedTags -contains $tag
        if (-not $releaseExists) {
            $notes = 'First native PowerUI Weapon Presets test: named per-class/controller/character presets, protected first-run grip backups, durable storage outside RM instances, confirmation before Trash deletion, Rename, Restore, manual backup export and safe Load/Undo through verified native grip keys. Legacy diagnostics remain available. THIS IS THE FIRST UNVERIFIED LIVE TEST BUILD; use Backup All Presets and verify your existing grip before loading.'
            $commit = (& git rev-parse HEAD).Trim()
            CheckExit 'Reading source commit'
            $maxAttempts = 4
            for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
                # A previous HTTP 500 can occur AFTER GitHub created the
                # release. Detect that before making another POST.
                $tagsNow = @()
                $listOkay = $false
                try {
                    $tagsNow = @(& gh release list --repo $repo --limit 100 --json tagName --jq '.[].tagName')
                    $listOkay = ($LASTEXITCODE -eq 0)
                }
                catch {
                    Write-Host "Release listing failed on attempt $attempt." -ForegroundColor Yellow
                }
                if ($listOkay -and ($tagsNow -contains $tag)) {
                    $releaseExists = $true
                    Write-Host "GitHub has already created release $tag; resuming asset verification." -ForegroundColor Yellow
                    break
                }
                if ($listOkay) {
                    Write-Host "Publishing $tag (attempt $attempt/$maxAttempts)..." -ForegroundColor Cyan
                    try {
                        & gh release create $tag $dll --repo $repo --target $commit --title "OrbusWeaponAdjustments $tag - Native Presets Test" --notes $notes
                        if ($LASTEXITCODE -eq 0) {
                            $releaseExists = $true
                            break
                        }
                    }
                    catch {
                        Write-Host ("Release attempt failed: " + $_.Exception.Message) -ForegroundColor Yellow
                    }
                }
                if ($attempt -lt $maxAttempts) {
                    $pause = [int][math]::Pow(2, $attempt)
                    Write-Host "GitHub release API failed; retrying after $pause seconds..." -ForegroundColor Yellow
                    Start-Sleep -Seconds $pause
                }
            }
            if (-not $releaseExists) {
                # Last check for a late server-side success.
                $lastTags = @()
                try {
                    $lastTags = @(& gh release list --repo $repo --limit 100 --json tagName --jq '.[].tagName')
                    if ($LASTEXITCODE -eq 0 -and ($lastTags -contains $tag)) {
                        $releaseExists = $true
                    }
                }
                catch { }
            }
            if (-not $releaseExists) {
                throw "GitHub release API did not publish $tag after $maxAttempts attempts. Source and DLL build are safe; retry .\release.ps1 when GitHub is available."
            }
        }
        else {
            Write-Host "GitHub release $tag already exists; verifying its DLL..." -ForegroundColor Yellow
        }

        $stateJson = & gh release view $tag --repo $repo --json isPrerelease,assets,url
        CheckExit 'Reading release metadata'
        $state = $stateJson | ConvertFrom-Json
        if ($state.isPrerelease) {
            throw "Release $tag is a prerelease, but RM needs a stable release."
        }
        $assets = @($state.assets | Where-Object {
            $_.name -eq 'OrbusWeaponAdjustments.dll' -and $_.size -gt 0
        })
        if ($assets.Count -eq 0) {
            Write-Host 'Uploading weapon preset DLL...' -ForegroundColor Cyan
            & gh release upload $tag $dll --repo $repo
            CheckExit 'Uploading release DLL'
        }

        # GitHub can create a draft and attach the DLL before returning 500.
        # RM only sees published stable releases; finalize orphaned drafts.
        $draftJson = & gh release view $tag --repo $repo --json isDraft
        CheckExit 'Checking GitHub release draft status'
        $draftStatus = $draftJson | ConvertFrom-Json
        if ($draftStatus.isDraft) {
            Write-Host "Publishing recovered draft $tag for RebornManager..." -ForegroundColor Yellow
            & gh release edit $tag --repo $repo --draft=false --prerelease=false
            if ($LASTEXITCODE -ne 0) {
                $verifyDraftJson = & gh release view $tag --repo $repo --json isDraft
                CheckExit 'Verifying publish after GitHub error'
                $verifyDraft = $verifyDraftJson | ConvertFrom-Json
                if ($verifyDraft.isDraft) {
                    throw "Release $tag remains a draft. Run gh release edit $tag --repo $repo --draft=false --prerelease=false."
                }
            }
        }

        $checkJson = & gh release view $tag --repo $repo --json isDraft,isPrerelease,assets,url
        CheckExit 'Verifying published release'
        $check = $checkJson | ConvertFrom-Json
        if ($check.isDraft -or $check.isPrerelease -or
            @($check.assets | Where-Object { $_.name -eq 'OrbusWeaponAdjustments.dll' -and $_.size -gt 0 }).Count -ne 1) {
            throw 'Release verification failed: expected one nonempty DLL on a stable release.'
        }
        Write-Host ''
        Write-Host "SUCCESS: $($check.url)" -ForegroundColor Green
        Write-Host 'Refresh RM to install OrbusWeaponAdjustments.' -ForegroundColor Green
    }
    finally { Pop-Location }
}
catch {
    Write-Host ''
    Write-Host "RELEASE STOPPED: $($_.Exception.Message)" -ForegroundColor Red
    throw
}
