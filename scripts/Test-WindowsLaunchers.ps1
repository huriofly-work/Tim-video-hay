$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$paths = @('Start.ps1','scripts/Build-Native.ps1','scripts/Prepare-NativeTools.ps1','scripts/Archive-BrowserVersion.ps1','packaging/Install-Local.ps1','packaging/Install-Portable.ps1','packaging/Uninstall-Portable.ps1','scripts/Test-ReleasePackage.ps1','scripts/Test-InstallLifecycle.ps1','scripts/Test-Native.ps1','scripts/Publish-Release.ps1')
foreach ($path in $paths) {
    $fullPath = Join-Path $taskRoot $path
    $bytes = [IO.File]::ReadAllBytes($fullPath)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    if (-not $hasBom) { throw "Windows PowerShell scripts must use UTF-8 BOM: $path" }
    $tokens = $null
    $errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($fullPath,[ref]$tokens,[ref]$errors) | Out-Null
    if ($errors.Count -gt 0) { throw "PowerShell syntax check failed: $path" }
    Write-Host "PASS PowerShell $($PSVersionTable.PSVersion): $path"
}

# Value: protects=build and release refuse a VERSION the installer rejects (not MAJOR.MINOR.PATCH) before any build, git or upload step; fails_when=the 3-part check is loosened or moved after side effects; why_new=parser checks above never run the scripts; seam=none (copy of the real script in a temp repo root); no_claim=a valid VERSION builds and releases (covered by real builds and Publish-Release -DryRun)
$probeRoot = Join-Path ([IO.Path]::GetTempPath()) ('tvh-version-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
try {
    New-Item -ItemType Directory -Path (Join-Path $probeRoot 'scripts') -Force | Out-Null
    foreach ($script in 'Build-Native.ps1', 'Publish-Release.ps1') {
        $copy = Join-Path $probeRoot "scripts/$script"
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $script) -Destination $copy
        foreach ($badVersion in '1.2.3.4', '0.3', '', 'v0.3.44') {
            [IO.File]::WriteAllText((Join-Path $probeRoot 'VERSION'), "$badVersion`n")
            $previous = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
            try { $output = (& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $copy 2>&1 | Out-String); $exitCode = $LASTEXITCODE } finally { $ErrorActionPreference = $previous }
            if ($exitCode -eq 0 -or $output -notmatch 'MAJOR\.MINOR\.PATCH') { throw "$script accepted VERSION '$badVersion' or failed for another reason (exit $exitCode): $output" }
            Write-Host "PASS ${script} rejects VERSION '$badVersion'"
        }
    }
} finally { Remove-Item -LiteralPath $probeRoot -Recurse -Force -ErrorAction SilentlyContinue }

# Value: protects=a real release is refused from a wrong branch, a dirty tree, an unpushed HEAD, a missing CHANGELOG section or a tag on another commit, before any build or artifacts, and resumes when the tag already points at HEAD; fails_when=a guard is removed, weakened to a warning in real mode, or moved after the build; why_new=only the VERSION guard ran before; seam=none (real git repo with a local bare origin, copy of the real script); no_claim=gh auth, tag push and gh release create against GitHub (manual release only)
function Invoke-ReleaseFixture([string]$State, [switch]$DryRun) {
    $root = Join-Path ([IO.Path]::GetTempPath()) ('tvh-release-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    $repo = Join-Path $root 'repo'; $bare = Join-Path $root 'origin.git'
    $previous = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try {
        # Không phụ thuộc cấu hình git toàn cục của máy (ký commit/tag, hook).
        function G { & git -C $repo -c user.name=test -c user.email=test@example.invalid -c commit.gpgsign=false -c tag.gpgsign=false -c core.hooksPath=NUL @args 2>&1 | Out-Null; if ($LASTEXITCODE -ne 0) { throw "git $args failed in release fixture" } }
        New-Item -ItemType Directory -Path (Join-Path $repo 'scripts') -Force | Out-Null
        & git init --quiet --bare $bare 2>&1 | Out-Null
        & git init --quiet -b main $repo 2>&1 | Out-Null
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Publish-Release.ps1') -Destination (Join-Path $repo 'scripts/Publish-Release.ps1')
        [IO.File]::WriteAllText((Join-Path $repo 'VERSION'), "9.9.9`n")
        $section = if ($State -eq 'no-changelog') { '9.9.8' } else { '9.9.9' }
        [IO.File]::WriteAllText((Join-Path $repo 'CHANGELOG.md'), "# Changelog`n`n## [$section] - 2026-01-01`n`n- entry`n")
        [IO.File]::WriteAllText((Join-Path $repo '.gitignore'), "artifacts/`n")
        G add -A; G commit --quiet -m init; G remote add origin $bare; G push --quiet origin main
        $tagCommit = $null
        switch ($State) {
            'branch' { G switch --quiet -c feature }
            'dirty' { [IO.File]::WriteAllText((Join-Path $repo 'note.txt'), 'x') }
            'unpushed' { [IO.File]::WriteAllText((Join-Path $repo 'note.txt'), 'x'); G add -A; G commit --quiet -m extra }
            'tag-other' { G tag -a v9.9.9 -m old; $tagCommit = (& git -C $repo rev-parse HEAD).Trim(); [IO.File]::WriteAllText((Join-Path $repo 'note.txt'), 'x'); G add -A; G commit --quiet -m extra; G push --quiet origin main }
            'tag-head' { G tag -a v9.9.9 -m partial; G push --quiet origin refs/tags/v9.9.9 }
        }
        $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $repo 'scripts/Publish-Release.ps1'))
        if ($DryRun) { $arguments += '-DryRun' }
        $output = (& powershell.exe @arguments 2>&1 | Out-String); $exitCode = $LASTEXITCODE
        # PowerShell ngắt dòng thông báo lỗi theo độ rộng console, có khi giữa một SHA: nối lại trước khi so khớp.
        [pscustomobject]@{ Exit = $exitCode; Output = ($output -replace '\r?\n', ''); Artifacts = (Test-Path -LiteralPath (Join-Path $repo 'artifacts')); Head = (& git -C $repo rev-parse HEAD).Trim(); TagCommit = $tagCommit }
    } finally { $ErrorActionPreference = $previous; Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }
}
$refusals = [ordered]@{ 'branch' = "'feature'"; 'dirty' = 'Working tree'; 'unpushed' = 'origin/main\. Pull'; 'no-changelog' = 'CHANGELOG\.md.*\[9\.9\.9\]'; 'tag-other' = $null; 'clean' = 'repo GitHub: ' }
foreach ($state in $refusals.Keys) {
    $result = Invoke-ReleaseFixture $state
    $expected = if ($state -eq 'tag-other') { "Tag v9\.9\.9 .*$($result.TagCommit).*$($result.Head)" } else { $refusals[$state] }
    if ($result.Exit -eq 0 -or $result.Artifacts -or $result.Output -notmatch $expected -or $result.Output -match 'Build-Native\.ps1') { throw "Publish-Release did not refuse '$state' before building (exit $($result.Exit), artifacts $($result.Artifacts)): $($result.Output)" }
    Write-Host "PASS Publish-Release refuses a real release: $state"
}
$resume = Invoke-ReleaseFixture 'tag-head' -DryRun
if ($resume.Output -notmatch "\(resume\) Tag v9\.9\.9 .*$($resume.Head)" -or ([regex]::Matches($resume.Output, 'WARNING: DRY RUN: ')).Count -ne 1 -or $resume.Output -notmatch 'WARNING: DRY RUN: .{0,60}repo GitHub: ' -or $resume.Output -notmatch 'Build-Native\.ps1') { throw "Publish-Release did not resume from a tag already at HEAD: $($resume.Output)" }
Write-Host 'PASS Publish-Release resumes from a tag already at HEAD'
