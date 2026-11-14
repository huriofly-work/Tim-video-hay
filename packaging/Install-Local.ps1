param([switch]$DesktopShortcut, [switch]$NoShortcuts)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$appVersion = [IO.File]::ReadAllText((Join-Path $taskRoot 'VERSION')).Trim()
$sourceDir = Join-Path $taskRoot "artifacts/windows-x64-$appVersion"
$installer = Join-Path $sourceDir 'Install.ps1'
if (-not (Test-Path -LiteralPath $installer)) { throw 'Chưa có bản đóng gói đầy đủ. Chạy scripts/Build-Native.ps1 trước.' }
$arguments = @('-NoProfile','-ExecutionPolicy','Bypass','-File',$installer)
if ($DesktopShortcut) { $arguments += '-DesktopShortcut' }
if ($NoShortcuts) { $arguments += '-NoShortcuts' }
& powershell.exe @arguments
if ($LASTEXITCODE -ne 0) { throw 'Cài đặt chưa hoàn tất. Kiểm tra thông báo bên trên.' }