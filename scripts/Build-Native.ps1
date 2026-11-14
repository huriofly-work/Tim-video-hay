# Một bản build duy nhất cho máy mình và cho release: không nhúng Google OAuth. Hồ sơ app giữ OAuth client đã nhập (DPAPI).
param([switch]$SkipTests, [switch]$SkipPublish, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$env:DOTNET_CLI_HOME = Join-Path $taskRoot 'tools/dotnet-home'
$env:NUGET_PACKAGES = Join-Path $taskRoot 'tools/nuget-packages'
$appVersion = [IO.File]::ReadAllText((Join-Path $taskRoot 'VERSION')).Trim()
if ($appVersion -notmatch '^\d+\.\d+\.\d+$') { throw "VERSION phải dạng MAJOR.MINOR.PATCH (Install-Portable chỉ nhận 3 số): '$appVersion'" }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
# Bản build cá nhân cũ (đến 0.3.45) từng ghi bản sao OAuth client ở đây; xoá để không còn secret thừa trên máy.
$staleBundle = Join-Path $taskRoot 'tools/dotnet-home/google-oauth.bundle.json'
if (Test-Path -LiteralPath $staleBundle) { Remove-Item -LiteralPath $staleBundle -Force }
Push-Location $taskRoot
try {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Test-WindowsLaunchers.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Windows PowerShell launcher checks failed.' }
    dotnet restore native/TimVideoHay.Desktop --locked-mode --configfile native/NuGet.Config
    if ($LASTEXITCODE -ne 0) { throw 'Restore desktop failed.' }
    dotnet build native/TimVideoHay.Desktop -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw 'Build desktop failed.' }
    if (-not $SkipTests) {
        dotnet restore native/TimVideoHay.Tests --locked-mode --configfile native/NuGet.Config
        if ($LASTEXITCODE -ne 0) { throw 'Restore tests failed.' }
        dotnet build native/TimVideoHay.Tests -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
        if ($LASTEXITCODE -ne 0) { throw 'Build tests failed.' }
        dotnet run --project native/TimVideoHay.Tests -c Release --no-build --no-restore -- 'test-output/native-tests'
        if ($LASTEXITCODE -ne 0) { throw 'Native tests failed.' }
    }
    if (-not $SkipPublish) {
        if (-not (Test-Path -LiteralPath 'tools/ffprobe.exe')) { throw 'Run scripts/Prepare-NativeTools.ps1 first.' }
        $toolManifest = [IO.File]::ReadAllText((Join-Path $taskRoot 'packaging/tool-manifest.public.json')) | ConvertFrom-Json
        if ((Get-FileHash -LiteralPath 'tools/ffprobe.exe' -Algorithm SHA256).Hash.ToLowerInvariant() -ne $toolManifest.ffprobe.sha256) { throw 'ffprobe does not match the source-pinned tool manifest.' }
        $output = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $taskRoot "artifacts/windows-x64-$appVersion" }
        if (Test-Path -LiteralPath $output) { throw 'Use a new output directory. Publishing over old/user files is forbidden.' }
        New-Item -ItemType Directory -Path $output | Out-Null
        dotnet publish native/TimVideoHay.Desktop -c Release -r win-x64 --self-contained true -o $output -m:1 -nr:false -p:UseSharedCompilation=false "-p:RestoreConfigFile=$taskRoot/native/NuGet.Config" -p:RestoreLockedMode=true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false
        if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
        # SDK IntelliSense documentation is not required by the running app.
        foreach ($name in @('Microsoft.Web.WebView2.Core.xml','Microsoft.Web.WebView2.Wpf.xml','Microsoft.Web.WebView2.WinForms.xml')) {
            $sdkDoc = [IO.Path]::GetFullPath((Join-Path $output $name))
            if (-not $sdkDoc.StartsWith($output+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid SDK documentation target.' }
            if (Test-Path -LiteralPath $sdkDoc) { Remove-Item -LiteralPath $sdkDoc }
        }
        New-Item -ItemType Directory -Path (Join-Path $output 'tools/licenses') -Force | Out-Null
        Copy-Item -LiteralPath 'tools/ffprobe.exe' -Destination (Join-Path $output 'tools')
        Copy-Item -LiteralPath 'README.md','INSTALLATION.md','GOOGLE-SETUP.md','DOUYIN-SESSION.md','THIRD-PARTY-NOTICES.md','SECURITY.md','SOURCE-CHANNEL-PLAN.md' -Destination $output
        Copy-Item -LiteralPath 'packaging/tool-manifest.public.json' -Destination (Join-Path $output 'tools/tool-manifest.json')
        Copy-Item -LiteralPath 'tools/licenses/FFmpeg-COPYING.GPLv3' -Destination (Join-Path $output 'tools/licenses')
        Copy-Item -LiteralPath 'packaging/licenses/WebView2-LICENSE.md','packaging/licenses/WebView2-NOTICE.md' -Destination (Join-Path $output 'tools/licenses')
        Copy-Item -LiteralPath 'packaging/Install-Portable.ps1' -Destination (Join-Path $output 'Install.ps1')
        Copy-Item -LiteralPath 'packaging/Uninstall-Portable.ps1' -Destination (Join-Path $output 'Uninstall.ps1')
        Copy-Item -LiteralPath 'packaging/Cai-dat.cmd','packaging/Go-cai-dat.cmd' -Destination $output
        dotnet run --project native/TimVideoHay.Tests -c Release --no-build --no-restore -- 'test-output/release-bundle' --check-bundle (Join-Path $output 'TimVideoHay.dll')
        if ($LASTEXITCODE -ne 0) { throw 'Build must not embed Google OAuth configuration.' }
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Test-ReleasePackage.ps1') -PackageDirectory $output
        if ($LASTEXITCODE -ne 0) { throw 'Release privacy/integrity gate failed. Do not distribute this output.' }
        $entries = @(Get-ChildItem -LiteralPath $output -File -Recurse | Sort-Object FullName | ForEach-Object {
            @{ path = $_.FullName.Substring($output.Length + 1).Replace('\','/'); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
        })
        @{ version = $appVersion; googleConfigurationBundled = $false; signed = $false; files = $entries } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'release-manifest.json') -Encoding UTF8
        Write-Host "Published: $output\TimVideoHay.exe"
    }
} finally { Pop-Location }
