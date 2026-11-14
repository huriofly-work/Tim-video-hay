param([string]$FixtureRoot)
# Một lệnh build + test chuẩn của repo: kiểm tra script PowerShell, restore/build Release hai project, chạy console test runner trên fixture mới có đường dẫn ngắn.
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$env:DOTNET_CLI_HOME = Join-Path $taskRoot 'tools/dotnet-home'
$env:NUGET_PACKAGES = Join-Path $taskRoot 'tools/nuget-packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
if (-not (Test-Path -LiteralPath (Join-Path $taskRoot 'tools/ffprobe.exe'))) { throw 'Thiếu tools/ffprobe.exe. Chạy ./scripts/Prepare-NativeTools.ps1 -FfprobePath <ffprobe-đúng-hash> trước (3 ca test cần ffprobe).' }
if (-not $FixtureRoot) { $FixtureRoot = 'test-output/t-' + [guid]::NewGuid().ToString('N').Substring(0, 12) }
$fixturePath = [IO.Path]::GetFullPath((Join-Path $taskRoot $FixtureRoot))
if ($fixturePath.Length -gt 120) { throw "Đường dẫn fixture quá dài ($($fixturePath.Length) ký tự), SQLite có thể báo lỗi 14. Dùng -FixtureRoot ngắn hơn." }
Push-Location $taskRoot
try {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Test-WindowsLaunchers.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Kiểm tra script PowerShell 5.1 thất bại.' }
    if (Get-Command pwsh.exe -ErrorAction SilentlyContinue) {
        & pwsh.exe -NoProfile -File (Join-Path $PSScriptRoot 'Test-WindowsLaunchers.ps1')
        if ($LASTEXITCODE -ne 0) { throw 'Kiểm tra script PowerShell 7 thất bại.' }
    }
    foreach ($name in 'Desktop', 'Tests') {
        $project = "native/TimVideoHay.$name"
        dotnet restore $project --locked-mode --configfile native/NuGet.Config
        if ($LASTEXITCODE -ne 0) { throw "Restore thất bại: $project" }
        dotnet build $project -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
        if ($LASTEXITCODE -ne 0) { throw "Build thất bại: $project" }
    }
    dotnet run --project native/TimVideoHay.Tests -c Release --no-build --no-restore -- $fixturePath
    if ($LASTEXITCODE -ne 0) { throw "Test native thất bại. Fixture: $fixturePath" }
    Write-Host "PASS build Release và test native. Fixture: $fixturePath"
} finally { Pop-Location }
