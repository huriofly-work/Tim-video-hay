# PostToolUse hook: sau khi Claude ghi hoặc sửa file .ps1, kiểm tra UTF-8 BOM và CRLF theo AGENTS.md.
# Lỗi thì exit 2 và ghi stderr để Claude thấy và sửa ngay; file khác hoặc hợp lệ thì exit 0.
$ErrorActionPreference = 'Stop'
[Console]::InputEncoding = [Text.Encoding]::UTF8
[Console]::OutputEncoding = [Text.Encoding]::UTF8
try {
    $path = ([Console]::In.ReadToEnd() | ConvertFrom-Json).tool_input.file_path
} catch { exit 0 }
if (-not $path -or [IO.Path]::GetExtension($path) -ine '.ps1' -or -not (Test-Path -LiteralPath $path)) { exit 0 }

$bytes = [IO.File]::ReadAllBytes($path)
$problems = @()
if (-not ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)) {
    $problems += 'missing UTF-8 BOM'
}
# Latin-1 giữ nguyên từng byte, nên đếm được LF không có CR đứng trước.
$lfOnly = [regex]::Matches([Text.Encoding]::GetEncoding(28591).GetString($bytes), '(?<!\r)\n').Count
if ($lfOnly -gt 0) { $problems += "$lfOnly LF-only line endings (need CRLF)" }

if ($problems.Count -gt 0) {
    [Console]::Error.WriteLine("$path : $($problems -join '; '). AGENTS.md requires .ps1 files to use UTF-8 with BOM and CRLF line endings; rewrite this file with that encoding.")
    exit 2
}
exit 0
