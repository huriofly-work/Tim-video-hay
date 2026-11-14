$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$nodeCandidates = @((Join-Path $projectRoot '.runtime\node.exe'), (Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe'))
$nodeExe = $nodeCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $nodeExe) { $nodeExe = (Get-Command node -ErrorAction Stop).Source }
$existingApp = $null
try { $existingApp = Invoke-WebRequest -Uri 'http://127.0.0.1:43127/' -TimeoutSec 2 -UseBasicParsing } catch {}
if ($existingApp -and $existingApp.Headers['X-Video-App-Version'] -ne '0.2.2') {
  throw 'Bản cũ đang dùng cổng 43127. Đóng tiến trình chương trình cũ rồi mở lại bản 0.2.2.'
}
if (-not $existingApp) {
  Start-Process -FilePath $nodeExe -ArgumentList @('src/server-v2.mjs') -WorkingDirectory $projectRoot -WindowStyle Hidden -RedirectStandardOutput (Join-Path $projectRoot 'app.log') -RedirectStandardError (Join-Path $projectRoot 'app-error.log')
  $ready = $false
  for ($attempt = 0; $attempt -lt 20; $attempt++) {
    Start-Sleep -Milliseconds 500
    try {
      $startedApp = Invoke-WebRequest -Uri 'http://127.0.0.1:43127/' -TimeoutSec 2 -UseBasicParsing
      if ($startedApp.Headers['X-Video-App-Version'] -eq '0.2.2') { $ready = $true; break }
    } catch {}
  }
  if (-not $ready) { throw 'Không khởi động được chương trình. Xem app-error.log trong thư mục chương trình.' }
}
Start-Process 'http://127.0.0.1:43127/'
