---
name: release
description: Phát hành bản mới (tag + GitHub Release), cài bản mới lên máy, hoặc khôi phục bản cài từ GitHub Release cho app Tìm video hay. Chỉ dùng khi người dùng giao việc release, cài đặt hoặc khôi phục.
---

# Release và cài bản mới

Release dùng để quản lý phiên bản và sao lưu bản cài ngoài máy. Repo public nên file đính kèm và
release notes ai cũng xem được.
Chỉ chạy release thật (bước 1 không có `-DryRun`) và cài đặt (bước 2) khi được giao, vì chúng
tạo tag, upload và động vào bản cài thật.

## 1. Phát hành
- Điều kiện: đang ở `main` sạch, trùng `origin/main`; `VERSION` đã tăng; `CHANGELOG.md` có mục cho phiên bản đó.
- Chạy thử trước:
  `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Publish-Release.ps1 -DryRun`
- Thử đạt thì chạy lại không có `-DryRun`. Script tự build, test, nén zip kèm `.sha256`, gắn tag
  `v<VERSION>` và tạo GitHub Release; nó từ chối khi thiếu điều kiện, nên đọc thông báo lỗi và sửa
  đúng điều kiện đó.
- Dừng giữa chừng sau khi đã push tag: chạy lại script, nó tiếp tục từ tag đó.
- Mỗi lần chạy (kể cả dry-run) để lại vài trăm MB trong `artifacts/release-*`; xoá thư mục cũ sau khi
  release đã lên GitHub, khi người dùng đồng ý.

## 2. Cài trên máy
- `./scripts/Build-Native.ps1` build vào `artifacts/windows-x64-<VERSION>`, rồi
  `./packaging/Install-Local.ps1` cài vào `%LOCALAPPDATA%\Programs\TimVideoHay\<VERSION>`; bản cũ được giữ.
- Build lại cùng `VERSION`: xoá `artifacts/windows-x64-<VERSION>` cũ trước, vì script từ chối ghi đè
  và `Install-Local.ps1` chỉ tìm thư mục mặc định.

## 3. Khôi phục (máy mới hoặc cài lại Windows)
- Tải zip từ GitHub Release, so SHA-256, giải nén rồi chạy `Cai-dat.cmd`; kết nối Google lại qua wizard.
- Release không chứa OAuth JSON và không sao lưu hồ sơ người dùng (`%LOCALAPPDATA%\TimVideoHay`,
  mã hoá DPAPI theo user Windows).

## Gotchas
- `Test-ReleasePackage.ps1` chỉ chặn ID project Google và email riêng khi máy có
  `packaging/personal-markers.txt` (ignore, không commit). Thiếu file thì báo `0 personal markers`:
  dừng và hỏi người dùng trước khi release thật.
