$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$legacyRoot = [IO.Path]::GetFullPath((Join-Path $taskRoot 'legacy/browser-v022'))
$legacyScripts = Join-Path $legacyRoot 'scripts'
$legacyDocs = Join-Path $legacyRoot 'docs'
foreach ($destination in @($legacyRoot, $legacyScripts, $legacyDocs)) {
    if (-not $destination.StartsWith($taskRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Archive outside workspace.' }
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
}
foreach ($relative in @('src','public','extension','test','package.json','node_modules','.runtime')) {
    $source = [IO.Path]::GetFullPath((Join-Path $taskRoot $relative))
    $target = [IO.Path]::GetFullPath((Join-Path $legacyRoot $relative))
    if (-not $source.StartsWith($taskRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or -not $target.StartsWith($legacyRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid archive target.' }
    if (Test-Path -LiteralPath $source) {
        if (Test-Path -LiteralPath $target) { throw "Archive target already exists: $relative" }
        Move-Item -LiteralPath $source -Destination $target
    }
}
$oldTest = Join-Path $taskRoot 'scripts/test.mjs'
if (Test-Path -LiteralPath $oldTest) { Move-Item -LiteralPath $oldTest -Destination $legacyScripts }
foreach ($name in @('README.md','SPEC.md','VALIDATION.md','REBUILD-PLAN.md','Start.ps1','Mo-ung-dung.cmd')) {
    Copy-Item -LiteralPath (Join-Path $taskRoot $name) -Destination (Join-Path $legacyDocs $name) -Force
}
Write-Host 'Browser implementation archived; no user media or data/state.json moved.'
