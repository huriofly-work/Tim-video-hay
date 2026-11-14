param([switch]$DryRun)
# Tạo release để quản lý phiên bản và sao lưu bản cài: build gói -ForSharing (không nhúng Google OAuth),
# kiểm tra gói, nén zip + SHA-256, gắn tag v<VERSION> rồi tạo GitHub Release. Repo public: release ai cũng tải được.
# -DryRun chạy mọi bước trừ gắn/push tag và tạo release. Không bao giờ đưa hồ sơ, OAuth JSON hay log lên GitHub.
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$appVersion = [IO.File]::ReadAllText((Join-Path $taskRoot 'VERSION')).Trim()
if ($appVersion -notmatch '^\d+\.\d+\.\d+$') { throw "VERSION phải dạng MAJOR.MINOR.PATCH: '$appVersion'" }
$tag = "v$appVersion"
# PowerShell 5.1 biến stderr của lệnh native bị chuyển hướng thành lỗi khi ErrorActionPreference=Stop; chỉ lấy exit code.
function Test-NativeSucceeds([scriptblock]$Command) {
    # Đặt trước để lệnh không tồn tại (CommandNotFound không cập nhật exit code) không đọc nhầm exit code cũ.
    $global:LASTEXITCODE = 1
    $previous = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try { & $Command *> $null; return ($LASTEXITCODE -eq 0) } finally { $ErrorActionPreference = $previous }
}
# Chạy lệnh native, gom stdout+stderr và exit code (không để stderr thành lỗi dừng script).
function Invoke-NativeCapture([scriptblock]$Command) {
    $global:LASTEXITCODE = 1
    $previous = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try { $text = (& $Command 2>&1 | Out-String).Trim(); return [pscustomobject]@{ Exit = $LASTEXITCODE; Text = $text } } finally { $ErrorActionPreference = $previous }
}
function ConvertTo-GitHubSlug([string]$Url) {
    $m = [regex]::Match($Url, '^(?:https://github\.com/|git@github\.com:|ssh://git@github\.com/)([A-Za-z0-9_.-]+)/([A-Za-z0-9_.-]+?)(?:\.git)?/?$')
    if ($m.Success) { return "$($m.Groups[1].Value)/$($m.Groups[2].Value)" }
    return $null
}
function Get-RepoPermission([string]$RepoArg) {
    $r = Invoke-NativeCapture { gh repo view $RepoArg --json viewerPermission -q .viewerPermission }
    if ($r.Exit -ne 0) { return '' }
    return $r.Text
}
# Điều kiện Git: release thật dừng ngay; -DryRun chỉ cảnh báo để thử được trên nhánh trước khi merge.
function Assert-ReleaseReady([bool]$Ok, [string]$Message) {
    if ($Ok) { return }
    if ($DryRun) { Write-Warning "DRY RUN: release thật sẽ dừng ở đây: $Message" } else { throw $Message }
}
# Kiểm tra thuần trước mọi lệnh git: thiếu ghi chú release thì dừng sớm.
$changelog = [IO.File]::ReadAllText((Join-Path $taskRoot 'CHANGELOG.md'))
$section = [regex]::Match($changelog, '(?ms)^## \[' + [regex]::Escape($appVersion) + '\][^\r\n]*\r?\n(.*?)(?=^## \[|\z)')
if (-not $section.Success -or -not $section.Groups[1].Value.Trim()) { throw "CHANGELOG.md chưa có mục [$appVersion]." }
Push-Location $taskRoot
try {
    $branch = (git rev-parse --abbrev-ref HEAD).Trim()
    Assert-ReleaseReady ($branch -eq 'main') "Chỉ tạo release từ main (đang ở '$branch')."
    Assert-ReleaseReady (-not (git status --porcelain)) 'Working tree còn thay đổi chưa commit. Commit hoặc dọn trước khi release.'
    git fetch --quiet origin main --tags
    if ($LASTEXITCODE -ne 0) { throw 'Không fetch được origin.' }
    $head = (git rev-parse HEAD).Trim()
    Assert-ReleaseReady ($head -eq (git rev-parse origin/main).Trim()) 'HEAD khác origin/main. Pull hoặc merge PR trước khi release.'
    # Tag đã trỏ đúng HEAD (lượt trước dừng sau khi gắn/push tag) thì chạy tiếp; tag ở commit khác thì phải tăng VERSION.
    $resumeTag = $false
    if (Test-NativeSucceeds { git rev-parse --verify --quiet "refs/tags/$tag" }) {
        $tagCommit = (git rev-parse "$tag^{commit}").Trim()
        Assert-ReleaseReady ($tagCommit -eq $head) "Tag $tag đã có ở commit $tagCommit, không phải HEAD $head. Tăng VERSION trước khi release."
        if ($tagCommit -eq $head) { $resumeTag = $true; Write-Host "(resume) Tag $tag đã trỏ đúng HEAD ${head}: bỏ qua gắn tag, chỉ push tag nếu thiếu và tạo release còn thiếu." }
    }
    # Repo đích lấy từ origin và luôn truyền --repo cho gh, không để GH_REPO hay gh default chọn hộ; chỉ release vào repo mình quản trị.
    # -DryRun cũng chạy các kiểm tra gh này, chỉ cảnh báo thay vì dừng.
    $originUrl = (git remote get-url origin).Trim()
    $repo = ConvertTo-GitHubSlug $originUrl
    # Luôn kèm host github.com để GH_HOST không chuyển lệnh gh sang host khác.
    $repoArg = if ($repo) { "github.com/$repo" } else { $null }
    # URL có thể chứa tên đăng nhập và token đặt trước dấu @ của host: che phần đó trước khi in ra console hay log.
    Assert-ReleaseReady ([bool]$repo) "origin không phải repo GitHub: $($originUrl -replace '://[^/@]+@', '://***@')"
    if ($repo) {
        # Push và fetch phải cùng một repo, nếu không tag sẽ lên repo khác với repo đã kiểm tra.
        $pushUrl = (git remote get-url --push origin).Trim()
        $pushRepo = ConvertTo-GitHubSlug $pushUrl
        Assert-ReleaseReady ($pushRepo -and $pushRepo -ieq $repo) "URL push của origin ($($pushUrl -replace '://[^/@]+@', '://***@')) khác repo fetch $repo."
    }
    $ghInstalled = [bool](Get-Command gh -ErrorAction SilentlyContinue)
    Assert-ReleaseReady $ghInstalled 'gh chưa cài. Cài GitHub CLI rồi chạy gh auth login.'
    if ($repo -and $ghInstalled) {
        Assert-ReleaseReady (Test-NativeSucceeds { gh auth status --hostname github.com }) 'gh chưa đăng nhập github.com. Chạy gh auth login.'
        $permission = Get-RepoPermission $repoArg
        Assert-ReleaseReady ($permission -eq 'ADMIN') "Tài khoản gh không phải admin của $repo (quyền: '$permission'). Chỉ release vào repo của chính mình."
        # 404 là chưa có release; lỗi gh khác (mạng, quyền, rate limit) không được đọc thành "chưa có".
        $existing = Invoke-NativeCapture { gh api --hostname github.com "repos/$repo/releases/tags/$tag" }
        if ($existing.Exit -eq 0) { Assert-ReleaseReady $false "GitHub Release $tag đã có trong $repo." }
        elseif ($existing.Text -notmatch 'HTTP 404') { Assert-ReleaseReady $false "Không kiểm tra được release $tag (gh api lỗi, không phải 404): $($existing.Text)" }
    }

    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $releaseDir = Join-Path $taskRoot "artifacts/release-$appVersion-$stamp"
    $packageDir = Join-Path $releaseDir "sharing-windows-x64-$appVersion"
    New-Item -ItemType Directory -Path $releaseDir | Out-Null
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Build-Native.ps1') -ForSharing -OutputDirectory $packageDir
    if ($LASTEXITCODE -ne 0) { throw 'Build/test/publish gói chia sẻ thất bại.' }

    $zipName = "TimVideoHay-$appVersion-windows-x64.zip"
    $zipPath = Join-Path $releaseDir $zipName
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    # CreateFromDirectory trên .NET Framework (PowerShell 5.1) ghi '\' trong tên entry; chuẩn zip dùng '/'.
    $packageFiles = @(Get-ChildItem -LiteralPath $packageDir -File -Recurse -Force)
    $archive = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in $packageFiles) {
            $entryName = $file.FullName.Substring($packageDir.Length + 1).Replace('\', '/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entryName, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $archive.Dispose() }
    $fileCount = $packageFiles.Count
    $zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try { $entryCount = $zip.Entries.Count; $backslashEntries = @($zip.Entries | Where-Object { $_.FullName.Contains('\') }).Count } finally { $zip.Dispose() }
    if ($entryCount -ne $fileCount) { throw "Zip có $entryCount file, gói có $fileCount file." }
    if ($backslashEntries) { throw "Zip có $backslashEntries entry dùng '\' trong đường dẫn." }
    # Bản sao lưu chỉ có giá trị khi chính file zip giải nén ra một gói hợp lệ: kiểm tra lại privacy, manifest và checksum trên bản giải nén.
    $zipCheckDir = Join-Path $releaseDir 'zip-check'
    [IO.Compression.ZipFile]::ExtractToDirectory($zipPath, $zipCheckDir)
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Test-ReleasePackage.ps1') -PackageDirectory $zipCheckDir
    if ($LASTEXITCODE -ne 0) { throw 'Zip giải nén ra không qua kiểm tra gói (Test-ReleasePackage). Không release.' }
    # Dọn thư mục kiểm tra là việc phụ: Defender có thể còn giữ file vừa giải nén, không được làm hỏng release đã qua kiểm tra.
    Remove-Item -LiteralPath $zipCheckDir -Recurse -Force -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $zipCheckDir) { Write-Warning "Không xóa được $zipCheckDir (file đang bị giữ); xóa tay sau." }
    $zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $shaPath = "$zipPath.sha256"
    [IO.File]::WriteAllText($shaPath, "$zipHash  $zipName`n", [Text.UTF8Encoding]::new($false))
    $notesPath = Join-Path $releaseDir 'release-notes.md'
    $notes = $section.Groups[1].Value.Trim() + "`n`n---`nCommit: $head`nSHA-256 ($zipName): $zipHash`nGói -ForSharing, không nhúng Google OAuth. Sau khi cài, kết nối Google lại qua wizard. Hồ sơ người dùng không nằm trong release.`n"
    [IO.File]::WriteAllText($notesPath, $notes, [Text.UTF8Encoding]::new($false))
    # Build mất vài phút: nếu cây code, HEAD hoặc VERSION đổi trong lúc đó thì zip không còn khớp commit sẽ gắn tag.
    Assert-ReleaseReady (-not (git status --porcelain)) 'Working tree đã đổi trong lúc build; zip có thể không khớp commit. Chạy lại.'
    Assert-ReleaseReady ((git rev-parse HEAD).Trim() -eq $head) 'HEAD đã đổi trong lúc build; zip có thể không khớp commit. Chạy lại.'
    Assert-ReleaseReady ([IO.File]::ReadAllText((Join-Path $taskRoot 'VERSION')).Trim() -eq $appVersion) 'VERSION đã đổi trong lúc build. Chạy lại.'
    # Quyền hoặc tài khoản gh có thể đổi trong lúc build vài phút: kiểm tra lại ngay trước khi gắn tag/tạo release.
    if ($repo -and $ghInstalled) {
        $permissionNow = Get-RepoPermission $repoArg
        Assert-ReleaseReady ($permissionNow -eq 'ADMIN') "Tài khoản gh không còn là admin của $repo sau khi build (quyền: '$permissionNow'). Không release."
    }
    Write-Host "Gói: $packageDir ($fileCount file)"
    Write-Host "Zip: $zipPath ($((Get-Item -LiteralPath $zipPath).Length) byte, SHA-256 $zipHash)"

    if ($DryRun) {
        $tagStep = if ($resumeTag) { "dùng lại tag $tag đã có" } else { "gắn tag $tag tại $head" }
        Write-Host "DRY RUN: bỏ qua $tagStep, push tag và tạo GitHub Release."
        return
    }
    if (-not $resumeTag) {
        git tag -a $tag -m "Release $tag" $head
        if ($LASTEXITCODE -ne 0) { throw "Không tạo được tag $tag." }
    }
    git push origin "refs/tags/$tag"
    if ($LASTEXITCODE -ne 0) { throw "Không push được tag $tag. Tag local vẫn trỏ HEAD; sửa lỗi rồi chạy lại script này, nó sẽ tiếp tục từ tag đã có." }
    gh release create $tag $zipPath $shaPath --repo $repoArg --verify-tag --title $tag --notes-file $notesPath
    if ($LASTEXITCODE -ne 0) { throw "Tag $tag đã push nhưng tạo GitHub Release thất bại. Sửa lỗi rồi chạy lại script này, nó sẽ tiếp tục từ tag đã có." }
    Write-Host "PASS release $tag tại $head"
} finally { Pop-Location }
