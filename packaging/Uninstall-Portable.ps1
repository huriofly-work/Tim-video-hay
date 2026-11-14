[CmdletBinding(SupportsShouldProcess=$true, ConfirmImpact='High')]
param([switch]$RemoveUserData, [switch]$NoShortcuts)
$ErrorActionPreference = 'Stop'
function Assert-NoLink([string]$Path, [switch]$Tree) {
    $cursor = [IO.Path]::GetFullPath($Path)
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            if ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Không gỡ tại đường dẫn liên kết/junction.' }
        }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
    if ($Tree -and (Test-Path -LiteralPath $Path)) {
        $pending = New-Object 'Collections.Generic.Stack[string]'; $pending.Push($Path)
        while ($pending.Count -gt 0) {
            foreach ($item in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
                if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Có liên kết trong thư mục cần gỡ; hãy kiểm tra thủ công.' }
                if ($item.PSIsContainer) { $pending.Push($item.FullName) }
            }
        }
    }
}
$sourceManifest = Join-Path $PSScriptRoot 'release-manifest.json'; Assert-NoLink $sourceManifest
$source = [IO.File]::ReadAllText($sourceManifest) | ConvertFrom-Json
if ($source.version -notmatch '^\d+\.\d+\.\d+$') { throw 'Phiên bản gỡ không hợp lệ.' }
$localRoot = [IO.Path]::GetFullPath($env:LOCALAPPDATA).TrimEnd('\')
$installRoot = [IO.Path]::GetFullPath((Join-Path $localRoot 'Programs/TimVideoHay'))
$installDir = [IO.Path]::GetFullPath((Join-Path $installRoot $source.version))
if (-not $installDir.StartsWith($installRoot+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Đích gỡ nằm ngoài thư mục chương trình.' }
$dataDir = [IO.Path]::GetFullPath((Join-Path $localRoot 'TimVideoHay'))
Assert-NoLink $installDir -Tree
if ($RemoveUserData) { Assert-NoLink $dataDir -Tree }
$appPath = Join-Path $installDir 'TimVideoHay.exe'
foreach ($process in @(Get-Process TimVideoHay -ErrorAction SilentlyContinue)) {
    if ($RemoveUserData -or -not $process.Path -or $process.Path.Equals($appPath,[StringComparison]::OrdinalIgnoreCase)) {
        throw 'App đang chạy. Thoát từ khay hệ thống rồi gỡ lại; công việc đang chạy không bị tự dừng hoặc xóa.'
    }
}
$files = @()
$installedManifest = Join-Path $installDir 'release-manifest.json'
if ((Test-Path -LiteralPath $installDir) -and -not (Test-Path -LiteralPath $installedManifest) -and (Test-Path -LiteralPath $appPath)) {
    throw 'Bản cài thiếu manifest. Chưa xóa file; hãy kiểm tra thư mục phiên bản theo INSTALLATION.md.'
}
if (Test-Path -LiteralPath $installedManifest) {
    $release = [IO.File]::ReadAllText($installedManifest) | ConvertFrom-Json
    if ($release.version -ne $source.version) { throw 'Bản cài không khớp phiên bản cần gỡ.' }
    $seen = @{}
    foreach ($entry in $release.files) {
        if (-not $entry.path -or $entry.path -match '(^/|\\|:|(^|/)\.\.(/|$))' -or $seen.ContainsKey($entry.path)) { throw 'Danh sách file gỡ không hợp lệ.' }
        $target = [IO.Path]::GetFullPath((Join-Path $installDir $entry.path))
        if (-not $target.StartsWith($installDir+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'File gỡ nằm ngoài bản cài.' }
        if ((Test-Path -LiteralPath $target) -and (Get-Item -LiteralPath $target -Force).PSIsContainer) { throw 'Danh sách gỡ chứa thư mục thay vì file.' }
        $seen[$entry.path] = $true; $files += $target
    }
    if (-not $seen.ContainsKey('TimVideoHay.exe')) { throw 'Không nhận diện được manifest của app.' }
    $files += $installedManifest
}
$description = 'Gỡ file chương trình; giữ lịch sử, cookie, kết nối Google và toàn bộ video/ảnh đã tải'
if ($RemoveUserData) { $description = 'Gỡ chương trình và XÓA hồ sơ mặc định (lịch sử, cài đặt, cookie, token); giữ video/ảnh đã tải' }
if (-not $PSCmdlet.ShouldProcess($installDir,$description)) { return }
foreach ($file in $files) {
    Assert-NoLink $file
    if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file -Force }
}
# Only empty directories are removed. Unlisted user files are preserved.
if (Test-Path -LiteralPath $installDir) {
    Get-ChildItem -LiteralPath $installDir -Directory -Recurse -Force | Sort-Object { $_.FullName.Length } -Descending | ForEach-Object {
        if (@(Get-ChildItem -LiteralPath $_.FullName -Force).Count -eq 0) { Remove-Item -LiteralPath $_.FullName -Force }
    }
    if (@(Get-ChildItem -LiteralPath $installDir -Force).Count -eq 0) { Remove-Item -LiteralPath $installDir -Force }
    else { Write-Host "Đã gỡ app; giữ các file không thuộc bản cài tại: $installDir" }
}
if (-not $NoShortcuts) {
    $shell = New-Object -ComObject WScript.Shell
    $shortcuts = @(
        @{ Path = (Join-Path ([Environment]::GetFolderPath('Programs')) 'Tìm video hay.lnk'); Target = $appPath },
        @{ Path = (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Tìm video hay.lnk'); Target = $appPath }
    )
    foreach ($item in $shortcuts) {
        if (Test-Path -LiteralPath $item.Path) {
            Assert-NoLink $item.Path
            if ($shell.CreateShortcut($item.Path).TargetPath.Equals($item.Target,[StringComparison]::OrdinalIgnoreCase)) { Remove-Item -LiteralPath $item.Path -Force }
        }
    }
    $uninstallLink = Join-Path ([Environment]::GetFolderPath('Programs')) ("Gỡ Tìm video hay " + $source.version + '.lnk')
    if (Test-Path -LiteralPath $uninstallLink) {
        Assert-NoLink $uninstallLink
        $shortcut = $shell.CreateShortcut($uninstallLink)
        $expected = '-NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $installDir 'Uninstall.ps1') + '"'
        if ($shortcut.Arguments -eq $expected) { Remove-Item -LiteralPath $uninstallLink -Force }
    }
}
if ($RemoveUserData -and (Test-Path -LiteralPath $dataDir)) {
    # Verify the exact dedicated data target before recursive removal.
    if ($dataDir -ne [IO.Path]::GetFullPath((Join-Path $localRoot 'TimVideoHay')) -or $dataDir -eq $localRoot) { throw 'Đích dữ liệu không hợp lệ.' }
    Assert-NoLink $dataDir -Tree
    $browserDir = [IO.Path]::GetFullPath((Join-Path $dataDir 'douyin-browser'))
    if ($browserDir -ne (Join-Path $dataDir 'douyin-browser') -or -not $browserDir.StartsWith($dataDir + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Đích phiên Douyin không hợp lệ.' }
    if (Test-Path -LiteralPath $browserDir) {
        Assert-NoLink $browserDir -Tree
        $userMedia = @(Get-ChildItem -LiteralPath $browserDir -File -Recurse -Force | Where-Object { $_.Extension -in @('.mp4','.mov','.mkv','.webm','.part','.png','.jpg','.jpeg','.webp') })
        if ($userMedia.Count -eq 0) { Remove-Item -LiteralPath $browserDir -Recurse -Force }
        else { Write-Host 'Giữ thư mục phiên Douyin vì có video/ảnh bên trong. Muốn xóa phiên, hãy chuyển các file này ra ngoài rồi chạy gỡ/xóa hồ sơ lại.' }
    }
    foreach ($file in Get-ChildItem -LiteralPath $dataDir -File -Recurse -Force) {
        $relative = $file.FullName.Substring($dataDir.Length+1).Replace('\','/')
        # Delete only recognized state, never downloaded media or unrelated files.
        if ($relative -match '^(jobs\.db(?:-wal|-shm)?|secrets/[A-Za-z0-9_-]+\.dpapi(?:\.[a-f0-9]{32}\.tmp)?|logs/jobs\.log(?:\.[1-4])?|download-state/[A-Fa-f0-9]+\.owner)$') {
            Assert-NoLink $file.FullName; Remove-Item -LiteralPath $file.FullName -Force
        }
    }
    Get-ChildItem -LiteralPath $dataDir -Directory -Recurse -Force | Sort-Object { $_.FullName.Length } -Descending | ForEach-Object {
        if (@(Get-ChildItem -LiteralPath $_.FullName -Force).Count -eq 0) { Remove-Item -LiteralPath $_.FullName -Force }
    }
    if (@(Get-ChildItem -LiteralPath $dataDir -Force).Count -eq 0) { Remove-Item -LiteralPath $dataDir -Force }
    else { Write-Host "Giữ media/backup/file khác trong hồ sơ: $dataDir" }
}
Write-Host ('Đã gỡ Tìm video hay ' + $source.version + '. Video/ảnh, dòng Sheets và video YouTube không bị xóa.')
if (-not $RemoveUserData) { Write-Host 'Giữ nguyên hồ sơ app để cài lại và dùng tiếp. Muốn xóa hồ sơ mặc định: xem INSTALLATION.md.' }
Write-Host 'Quyền đã cấp trên Google vẫn còn; muốn thu hồi, mở https://myaccount.google.com/connections.'
