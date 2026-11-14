param([switch]$DesktopShortcut, [switch]$NoShortcuts)
$ErrorActionPreference = 'Stop'
$sourceRoot = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
function Assert-NoLink([string]$Path) {
    $cursor = [IO.Path]::GetFullPath($Path)
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            if ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked installation paths are forbidden.' }
        }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
}
Assert-NoLink $sourceRoot
Assert-NoLink (Join-Path $sourceRoot 'release-manifest.json')
$release = [IO.File]::ReadAllText((Join-Path $sourceRoot 'release-manifest.json')) | ConvertFrom-Json
if ($release.version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid version.' }
$seen = @{}
foreach ($entry in $release.files) {
    if (-not $entry.path -or $entry.path -match '(^/|\\|:|(^|/)\.\.(/|$))' -or $seen.ContainsKey($entry.path) -or $entry.sha256 -notmatch '^[a-f0-9]{64}$') { throw 'Invalid package path or checksum.' }
    $seen[$entry.path] = $true
    $file = Join-Path $sourceRoot $entry.path; Assert-NoLink $file
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256) { throw 'Package checksum mismatch. Obtain the complete original package.' }
}
if (-not $seen.ContainsKey('TimVideoHay.exe')) { throw 'Application is missing from manifest.' }
$installRoot = Join-Path $env:LOCALAPPDATA 'Programs/TimVideoHay'
$installDir = [IO.Path]::GetFullPath((Join-Path $installRoot $release.version)); Assert-NoLink $installDir
if (Test-Path -LiteralPath $installDir) { throw 'This version is already installed. Use it or remove that version manually before reinstalling; user data is separate.' }
New-Item -ItemType Directory -Path $installDir | Out-Null
foreach ($entry in $release.files) {
    $target = Join-Path $installDir $entry.path
    Assert-NoLink $target
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $sourceRoot $entry.path) -Destination $target
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256) { throw 'Installed file checksum mismatch.' }
}
Copy-Item -LiteralPath (Join-Path $sourceRoot 'release-manifest.json') -Destination $installDir
if (-not $NoShortcuts) {
$shell = New-Object -ComObject WScript.Shell
$shortcutPaths = @((Join-Path ([Environment]::GetFolderPath('Programs')) 'Tìm video hay.lnk'))
if ($DesktopShortcut) { $shortcutPaths += Join-Path ([Environment]::GetFolderPath('Desktop')) 'Tìm video hay.lnk' }
foreach ($shortcutPath in $shortcutPaths) {
    Assert-NoLink $shortcutPath
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = Join-Path $installDir 'TimVideoHay.exe'; $shortcut.WorkingDirectory = $installDir
    $shortcut.Save()
}
$uninstallShortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) ("Gỡ Tìm video hay " + $release.version + '.lnk')
Assert-NoLink $uninstallShortcutPath
$uninstall = $shell.CreateShortcut($uninstallShortcutPath)
$uninstall.TargetPath = Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
$uninstall.Arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $installDir 'Uninstall.ps1') + '"'
$uninstall.WorkingDirectory = $installDir
$uninstall.Save()
}
Write-Host "Installed for current Windows account: $installDir"
Write-Host 'Each recipient connects their own Google accounts. Checksums detect corruption, not a forged package whose manifest was also replaced.'
