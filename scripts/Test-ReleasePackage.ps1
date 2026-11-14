param([Parameter(Mandatory=$true)][string]$PackageDirectory, [switch]$RequireSigned)
$ErrorActionPreference = 'Stop'
$packageRoot = [IO.Path]::GetFullPath($PackageDirectory).TrimEnd('\')
if (-not (Test-Path -LiteralPath (Join-Path $packageRoot 'TimVideoHay.exe'))) { throw 'Missing application executable.' }
$pending = New-Object 'Collections.Generic.Stack[string]'; $pending.Push($packageRoot)
$files = @()
while ($pending.Count -gt 0) {
    $directory = $pending.Pop()
    if ((Get-Item -LiteralPath $directory).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Package contains a directory link.' }
    foreach ($item in Get-ChildItem -LiteralPath $directory -Force) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Package contains a file link.' }
        if ($item.PSIsContainer) { $pending.Push($item.FullName) } else { $files += $item }
    }
}
$allowedJson = @('TimVideoHay.deps.json','TimVideoHay.runtimeconfig.json','release-manifest.json','tools/tool-manifest.json')
# Dấu riêng (ID project Google, email...) nằm trong file local không commit; thiếu file thì chỉ chạy kiểm tra chung.
$personalMarkers = @()
$markerFile = Join-Path $PSScriptRoot '../packaging/personal-markers.txt'
if (Test-Path -LiteralPath $markerFile) {
    $personalMarkers = @([IO.File]::ReadAllLines($markerFile) | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') })
}
foreach ($file in $files) {
    $relative = $file.FullName.Substring($packageRoot.Length + 1).Replace('\','/')
    if ($relative -match '(?i)(^|/)(secrets|temp|logs|data|download-state|cover-backups|douyin-browser)(/|$)|cookie.*\.txt$|token|client[-_.]?secret|google-oauth|\.dpapi$|\.db($|-)|\.log$|\.pdb$|\.key$|\.pem$|\.pfx$|\.part$|\.owner$') { throw "Forbidden release file: $relative" }
    if ($file.Extension -eq '.json' -and $allowedJson -notcontains $relative) { throw "Unexpected JSON in release: $relative" }
    if ($file.Extension -notin @('.exe','.dll','.json','.md','.ps1','.gplv3') -and $relative -notin @('tools/licenses/FFmpeg-COPYING.GPLv3','Cai-dat.cmd','Go-cai-dat.cmd')) { throw "Unexpected release file type: $relative" }
    if ($file.Extension -in @('.md','.json','.ps1','.cmd') -or $file.Name -in @('TimVideoHay.dll','TimVideoHay.Infrastructure.dll','TimVideoHay.Core.dll')) {
        $bytes = [IO.File]::ReadAllBytes($file.FullName)
        foreach ($text in @([Text.Encoding]::UTF8.GetString($bytes),[Text.Encoding]::Unicode.GetString($bytes))) {
            if ($text -match '(?i)[a-z]:[\\/]+Users[\\/]|[a-z]:[\\/]+Tools[\\/]|["''](?:access_token|refresh_token)["'']\s*:\s*["''][^"'']+') { throw "Personal path/account or credential content in: $relative" }
            foreach ($marker in $personalMarkers) { if ($text.IndexOf($marker, [StringComparison]::OrdinalIgnoreCase) -ge 0) { throw "Personal marker content in: $relative" } }
        }
    }
}
$manifest = [IO.File]::ReadAllText((Join-Path $packageRoot 'tools/tool-manifest.json')) | ConvertFrom-Json
$pinned = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '../packaging/tool-manifest.public.json')) | ConvertFrom-Json
$hash = (Get-FileHash -LiteralPath (Join-Path $packageRoot 'tools/ffprobe.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
if ($hash -ne $manifest.ffprobe.sha256 -or $hash -ne $pinned.ffprobe.sha256 -or $manifest.ffprobe.origin) { throw 'Tool verification failed or private origin was included.' }
$releaseManifest = Join-Path $packageRoot 'release-manifest.json'
if (Test-Path -LiteralPath $releaseManifest) {
    $release = [IO.File]::ReadAllText($releaseManifest) | ConvertFrom-Json
    $seen = @{}
    foreach ($entry in $release.files) {
        if ($entry.path -match '(^/|\\|:|(^|/)\.\.(/|$))' -or $seen.ContainsKey($entry.path)) { throw 'Invalid release manifest path.' }
        $seen[$entry.path] = $true
        $path = Join-Path $packageRoot $entry.path
        if (-not (Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256) { throw 'Release checksum mismatch.' }
    }
    if ($files.Count -ne $seen.Count + 1) { throw 'Unexpected or missing files in release manifest.' }
}
if ($RequireSigned) {
    foreach ($file in $files | Where-Object { $_.Extension -in @('.exe','.ps1') }) {
        if ((Get-AuthenticodeSignature -LiteralPath $file.FullName).Status -ne 'Valid') { throw "Valid signature required: $($file.Name)" }
    }
}
Write-Host "PASS release privacy, file policy and tool integrity ($($files.Count) files, $($personalMarkers.Count) personal markers). This is not a security audit or a trusted signature."
