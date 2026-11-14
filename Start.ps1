$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$versionFile = Join-Path $taskRoot 'VERSION'
if (-not (Test-Path -LiteralPath $versionFile)) { throw 'Thiếu file VERSION cạnh Start.ps1.' }
$appVersion = [IO.File]::ReadAllText($versionFile).Trim()
$nativeCandidates = @(
    (Join-Path $taskRoot "artifacts/windows-x64-$appVersion/TimVideoHay.exe"),
    (Join-Path $env:LOCALAPPDATA "Programs/TimVideoHay/$appVersion/TimVideoHay.exe")
)
$nativeExe = $nativeCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $nativeExe) { throw 'Chưa có app Windows đóng gói. Chạy scripts/Build-Native.ps1 trên máy có .NET 10 SDK hoặc dùng bản portable đầy đủ.' }
# Cửa sổ tương tác do người dùng mở chủ động; không khởi động server/helper.
Start-Process -FilePath $nativeExe -WorkingDirectory (Split-Path -Parent $nativeExe)
