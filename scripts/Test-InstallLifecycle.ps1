$ErrorActionPreference = 'Stop'
$taskWorkspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskTestRoot = Join-Path $taskWorkspace ('test-output/install-lifecycle/' + [Guid]::NewGuid().ToString('N'))
$taskPackage = Join-Path $taskTestRoot 'package'
New-Item -ItemType Directory -Path $taskPackage -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $taskWorkspace 'packaging/Install-Portable.ps1') -Destination (Join-Path $taskPackage 'Install.ps1')
Copy-Item -LiteralPath (Join-Path $taskWorkspace 'packaging/Uninstall-Portable.ps1') -Destination (Join-Path $taskPackage 'Uninstall.ps1')
[IO.File]::WriteAllText((Join-Path $taskPackage 'TimVideoHay.exe'),'installation test fixture, not an executable')
[IO.File]::WriteAllText((Join-Path $taskPackage 'README.md'),'test documentation')
$files = @(Get-ChildItem -LiteralPath $taskPackage -File | ForEach-Object { @{path=$_.Name;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()} })
@{version='0.3.11';files=$files} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $taskPackage 'release-manifest.json') -Encoding UTF8
$taskOldLocal = $env:LOCALAPPDATA
$env:LOCALAPPDATA = Join-Path $taskTestRoot 'localappdata'
$taskInstalled = Join-Path $env:LOCALAPPDATA 'Programs/TimVideoHay/0.3.11'
$taskData = Join-Path $env:LOCALAPPDATA 'TimVideoHay'
$taskProcesses = @()
# Mock only process enumeration, never inspect or stop a user's real app.
function Get-Process { param($Name, $ErrorAction) return $taskProcesses }
$taskPassed = 0
function Assert([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw ('FAIL ' + $Name) }
    $script:taskPassed++; Write-Host ('PASS ' + $Name)
}
function Reject([scriptblock]$Action, [string]$Name) {
    $rejected = $false
    try { & $Action } catch { $rejected = $true }
    Assert $rejected $Name
}
function Install-Fixture {
    & (Join-Path $taskPackage 'Install.ps1') -NoShortcuts
}
function Uninstall-Fixture {
    & (Join-Path $taskPackage 'Uninstall.ps1') -NoShortcuts -Confirm:$false
}
try {
    Install-Fixture
    Assert (Test-Path -LiteralPath (Join-Path $taskInstalled 'Uninstall.ps1')) 'install copies uninstaller'
    Reject { Install-Fixture } 'existing version cannot be overwritten'
    & (Join-Path $taskPackage 'Uninstall.ps1') -NoShortcuts -WhatIf
    Assert (Test-Path -LiteralPath (Join-Path $taskInstalled 'TimVideoHay.exe')) 'WhatIf preserves program'
    New-Item -ItemType Directory -Path $taskData -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $taskData 'jobs.db'),'state fixture')
    $taskSibling = Join-Path $env:LOCALAPPDATA 'Programs/TimVideoHay/9.9.9'
    New-Item -ItemType Directory -Path $taskSibling -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $taskSibling 'keep.txt'),'sibling version')
    [IO.File]::WriteAllText((Join-Path $taskInstalled 'my-video.mp4'),'unlisted personal file')
    $taskProcesses = @([pscustomobject]@{ Path=(Join-Path $taskInstalled 'TimVideoHay.exe') })
    Reject { Uninstall-Fixture } 'running target blocks uninstall'
    Assert (Test-Path -LiteralPath (Join-Path $taskInstalled 'TimVideoHay.exe')) 'running guard preserves files'
    $taskProcesses = @()
    $taskInstalledManifest = Join-Path $taskInstalled 'release-manifest.json'
    $taskManifestText = [IO.File]::ReadAllText($taskInstalledManifest)
    $taskBad = $taskManifestText | ConvertFrom-Json; $taskBad.version = '9.9.9'
    $taskBad | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $taskInstalledManifest -Encoding UTF8
    Reject { Uninstall-Fixture } 'mismatched installed version blocks deletion'
    [IO.File]::WriteAllText($taskInstalledManifest,$taskManifestText,[Text.UTF8Encoding]::new($true))
    $taskBad = $taskManifestText | ConvertFrom-Json; $taskBad.files[0].path = '../../keep.txt'
    $taskBad | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $taskInstalledManifest -Encoding UTF8
    Reject { Uninstall-Fixture } 'manifest path traversal blocks deletion'
    [IO.File]::WriteAllText($taskInstalledManifest,$taskManifestText,[Text.UTF8Encoding]::new($true))
    Uninstall-Fixture
    Assert (-not (Test-Path -LiteralPath (Join-Path $taskInstalled 'TimVideoHay.exe'))) 'default uninstall removes app'
    Assert (Test-Path -LiteralPath (Join-Path $taskData 'jobs.db')) 'default uninstall preserves profile'
    Assert (Test-Path -LiteralPath (Join-Path $taskInstalled 'my-video.mp4')) 'unlisted video in install folder preserved'
    Assert (Test-Path -LiteralPath (Join-Path $taskSibling 'keep.txt')) 'other version preserved'
    Uninstall-Fixture
    Assert (Test-Path -LiteralPath (Join-Path $taskInstalled 'my-video.mp4')) 'repeat uninstall preserves unlisted files'
    # The remaining directory belongs to this test only; check exact target before deleting.
    $taskExpected = [IO.Path]::GetFullPath((Join-Path $taskTestRoot 'localappdata/Programs/TimVideoHay/0.3.11'))
    if ([IO.Path]::GetFullPath($taskInstalled) -ne $taskExpected -or -not $taskExpected.StartsWith($taskTestRoot+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid fixture cleanup target' }
    Remove-Item -LiteralPath (Join-Path $taskInstalled 'my-video.mp4')
    Remove-Item -LiteralPath $taskInstalled
    Install-Fixture
    $taskSelf = Join-Path $taskInstalled 'Uninstall.ps1'
    & $taskSelf -NoShortcuts -Confirm:$false
    Assert (-not (Test-Path -LiteralPath $taskInstalled)) 'uninstaller can remove its own installed package'
    Install-Fixture
    foreach ($name in @('secrets','logs','download-state','videos','cover-backups','douyin-browser')) { New-Item -ItemType Directory -Path (Join-Path $taskData $name) -Force | Out-Null }
    [IO.File]::WriteAllText((Join-Path $taskData 'douyin-browser/Cookies'),'browser credential fixture')
    [IO.File]::WriteAllText((Join-Path $taskData 'secrets/google-1.dpapi'),'encrypted fixture placeholder')
    [IO.File]::WriteAllText((Join-Path $taskData 'logs/jobs.log'),'log fixture')
    [IO.File]::WriteAllText((Join-Path $taskData 'download-state/ABCD.owner'),'owner fixture')
    [IO.File]::WriteAllText((Join-Path $taskData 'videos/keep.mp4'),'keep media fixture')
    [IO.File]::WriteAllText((Join-Path $taskData 'cover-backups/keep.jpg'),'keep cover fixture')
    [IO.File]::WriteAllText((Join-Path $taskData 'secrets/keep.mp4'),'keep even inside state folder')
    $taskProcesses = @([pscustomobject]@{Path='D:\OtherVersion\TimVideoHay.exe'})
    Reject { & (Join-Path $taskPackage 'Uninstall.ps1') -NoShortcuts -RemoveUserData -Confirm:$false } 'profile cleanup blocks while another app version is running'
    $taskProcesses = @()
    & (Join-Path $taskPackage 'Uninstall.ps1') -NoShortcuts -RemoveUserData -WhatIf
    Assert (Test-Path -LiteralPath (Join-Path $taskData 'secrets/google-1.dpapi')) 'WhatIf preserves credentials'
    & (Join-Path $taskPackage 'Uninstall.ps1') -NoShortcuts -RemoveUserData -Confirm:$false
    Assert (-not (Test-Path -LiteralPath (Join-Path $taskData 'jobs.db'))) 'explicit profile cleanup removes database'
    Assert (-not (Test-Path -LiteralPath (Join-Path $taskData 'secrets/google-1.dpapi'))) 'explicit profile cleanup removes credentials'
    Assert (-not (Test-Path -LiteralPath (Join-Path $taskData 'logs/jobs.log'))) 'explicit profile cleanup removes logs'
    Assert (-not (Test-Path -LiteralPath (Join-Path $taskData 'download-state/ABCD.owner'))) 'explicit profile cleanup removes ownership markers'
    Assert (-not (Test-Path -LiteralPath (Join-Path $taskData 'douyin-browser'))) 'explicit profile cleanup removes Douyin browser credentials'
    Assert (Test-Path -LiteralPath (Join-Path $taskData 'videos/keep.mp4')) 'profile cleanup preserves media'
    Assert (Test-Path -LiteralPath (Join-Path $taskData 'cover-backups/keep.jpg')) 'profile cleanup preserves cover backups'
    Assert (Test-Path -LiteralPath (Join-Path $taskData 'secrets/keep.mp4')) 'cleanup preserves unexpected media inside state folder'
    # Junction must be rejected before any program/state deletion.
    Install-Fixture
    $taskLink = Join-Path $taskInstalled 'external'
    New-Item -ItemType Junction -Path $taskLink -Target $taskSibling | Out-Null
    Reject { Uninstall-Fixture } 'junction blocks uninstall before deletion'
    Assert (Test-Path -LiteralPath (Join-Path $taskInstalled 'TimVideoHay.exe')) 'junction guard preserves program'
    Assert (Test-Path -LiteralPath (Join-Path $taskSibling 'keep.txt')) 'junction guard preserves external files'
    [IO.Directory]::Delete($taskLink) # Only remove this fixture junction, not its target.
    Uninstall-Fixture
    @{passed=$taskPassed;failed=0;profile=$taskTestRoot;processEnumeration='mock';shortcutsCreated=$false;realUserDataTouched=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskTestRoot 'result.json') -Encoding UTF8
    Write-Host "Install lifecycle: $taskPassed passed, 0 failed. $taskTestRoot"
} finally { $env:LOCALAPPDATA = $taskOldLocal }
