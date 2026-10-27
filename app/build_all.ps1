# One-click release build: Windows .exe, Android .apk + .aab locally, iOS .ipa on GitHub Actions
# (a Mac is required for iOS). Everything lands in <repo>\release\. Run via build_all.bat.
param([switch]$NoIos)

$ErrorActionPreference = 'Stop'
$app = $PSScriptRoot
$repo = Split-Path $app -Parent
$out = Join-Path $repo 'release'
$workflow = 'build-ios.yml'

function Step($msg) { Write-Host ''; Write-Host "=== $msg ===" -ForegroundColor Cyan }
function Run($exe) {
    & $exe @args
    if ($LASTEXITCODE -ne 0) { throw "$exe $args failed (exit $LASTEXITCODE)" }
}

Set-Location $app
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory $out | Out-Null
$failed = @()

# --- iOS: start the cloud build first so it runs while Windows/Android build here ---
$iosRunId = $null
if (-not $NoIos) {
    Step 'iOS: starting cloud build (GitHub Actions)'
    try {
        if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'GitHub CLI (gh) not installed' }
        git -C $repo fetch -q origin
        $ahead = [int](git -C $repo rev-list --count origin/main..HEAD)
        $dirty = git -C $repo status --porcelain --untracked-files=no
        if ($dirty) { Write-Host 'Note: uncommitted changes are NOT included in the iOS build.' -ForegroundColor Yellow }
        if ($ahead -gt 0) {
            Write-Host "Local main is $ahead commit(s) ahead of GitHub; the iOS build uses the code on GitHub." -ForegroundColor Yellow
            $ans = Read-Host 'Push main to GitHub now (repo is PUBLIC)? [y/N]'
            if ($ans -match '^[yY]') { Run git -C $repo push origin HEAD:main }
            else { Write-Host 'Building iOS from the older code on GitHub.' -ForegroundColor Yellow }
        }
        $since = (Get-Date).ToUniversalTime().AddSeconds(-5)
        Run gh workflow run $workflow --ref main
        for ($i = 0; $i -lt 30 -and -not $iosRunId; $i++) {
            Start-Sleep -Seconds 2
            $runs = gh run list --workflow $workflow --event workflow_dispatch -L 5 --json databaseId,createdAt | ConvertFrom-Json
            $run = $runs | Where-Object { [datetime]::Parse($_.createdAt).ToUniversalTime() -ge $since } | Select-Object -First 1
            if ($run) { $iosRunId = $run.databaseId }
        }
        if (-not $iosRunId) { throw 'could not find the started workflow run' }
        Write-Host "iOS run $iosRunId started."
    } catch {
        Write-Host "iOS skipped: $_" -ForegroundColor Red
        $failed += 'ipa'
    }
}

Step 'flutter pub get'
Run flutter pub get

Step 'Windows .exe'
try {
    Run flutter build windows --release
    $win = Join-Path $out 'WarAlarm-Windows'
    Copy-Item (Join-Path $app 'build\windows\x64\runner\Release') $win -Recurse
    Compress-Archive -Path "$win\*" -DestinationPath (Join-Path $out 'WarAlarm-Windows.zip')
} catch { Write-Host $_ -ForegroundColor Red; $failed += 'exe' }

Step 'Android .apk'
try {
    Run flutter build apk --release
    Copy-Item (Join-Path $app 'build\app\outputs\flutter-apk\app-release.apk') (Join-Path $out 'WarAlarm.apk')
} catch { Write-Host $_ -ForegroundColor Red; $failed += 'apk' }

Step 'Android .aab'
try {
    Run flutter build appbundle --release
    Copy-Item (Join-Path $app 'build\app\outputs\bundle\release\app-release.aab') (Join-Path $out 'WarAlarm.aab')
} catch { Write-Host $_ -ForegroundColor Red; $failed += 'aab' }

if ($iosRunId) {
    Step 'iOS .ipa: waiting for the cloud build'
    try {
        gh run watch $iosRunId --exit-status --interval 15
        if ($LASTEXITCODE -ne 0) { throw "run failed: gh run view $iosRunId --log-failed" }
        Run gh run download $iosRunId -n WarAlarm-ipa -D $out
    } catch { Write-Host "iOS failed: $_" -ForegroundColor Red; $failed += 'ipa' }
}

Step 'Done'
Get-ChildItem $out | Format-Table Name, @{ n = 'MB'; e = { if (-not $_.PSIsContainer) { [math]::Round($_.Length / 1MB, 1) } } } -AutoSize
if ($failed) {
    Write-Host "FAILED: $($failed -join ', ')" -ForegroundColor Red
    exit 1
}
Write-Host "All builds in $out" -ForegroundColor Green
