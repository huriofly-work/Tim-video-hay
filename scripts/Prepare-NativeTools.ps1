param([string]$FfprobePath)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskTools = Join-Path $taskRoot 'tools'
New-Item -ItemType Directory -Path $taskTools -Force | Out-Null
$manifestPath = Join-Path $taskRoot 'packaging/tool-manifest.public.json'
$manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
if (-not $FfprobePath) {
    if (Test-Path -LiteralPath (Join-Path $taskTools 'ffprobe.exe')) { $FfprobePath = Join-Path $taskTools 'ffprobe.exe' }
    else { $FfprobePath = (Get-Command ffprobe.exe -ErrorAction Stop).Source }
}
$resolvedProbe = (Get-Item -LiteralPath $FfprobePath).FullName
if ((Get-FileHash -LiteralPath $resolvedProbe -Algorithm SHA256).Hash.ToLowerInvariant() -ne $manifest.ffprobe.sha256) { throw 'ffprobe differs from the reviewed source-pinned version. Review/update packaging/tool-manifest.public.json before changing tools.' }
$destProbe = Join-Path $taskTools 'ffprobe.exe'
if ($resolvedProbe -ne $destProbe) { Copy-Item -LiteralPath $resolvedProbe -Destination $destProbe -Force }
$probeOutput = @(& $destProbe -version)
if ($LASTEXITCODE -ne 0) { throw 'ffprobe cannot start.' }
$licenseDir = Join-Path $taskTools 'licenses'; New-Item -ItemType Directory -Path $licenseDir -Force | Out-Null
$licensePath = Join-Path $licenseDir 'FFmpeg-COPYING.GPLv3'
if (-not (Test-Path -LiteralPath $licensePath)) { Invoke-WebRequest 'https://raw.githubusercontent.com/FFmpeg/FFmpeg/n9.0.2/COPYING.GPLv3' -OutFile $licensePath }
Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $taskTools 'tool-manifest.json') -Force
Write-Host 'Pinned ffprobe checksum verified. No yt-dlp or private tool-origin path is required.'