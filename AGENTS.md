# Quy ước làm việc · Tìm video hay

## Phạm vi
- App chỉ để chủ repo tự dùng trên máy mình; repo public, chưa cấp license.
- Release (tag + GitHub Release) dùng để quản lý phiên bản và sao lưu bản cài; repo public nên ai cũng tải được file đính kèm.
- Mọi thứ commit, Issue, PR và release notes đều công khai: không ghi email, đường dẫn máy, ID project Google hay chi tiết tài khoản thật.
- Chỉ tạo tag/release và thao tác Git khi được giao.
- Ruleset chặn push thẳng vào `main` và sửa/xoá tag `v*`: mọi thay đổi đi qua nhánh → PR → squash merge.

## Bắt đầu
- Đọc [docs/HANDOFF.md](docs/HANDOFF.md) trước.
- Kiểm tra `git status --short --branch -uall` và `git worktree list`; giữ thay đổi có sẵn.

## Code
- Phát triển bản C#/.NET 10 WPF trong `native/`. `legacy/browser-v022/` chỉ dùng để đối chiếu hoặc khôi phục.
- UI tiếng Việt, giữ thông tin thao tác cần thiết.
- Khi sửa luồng job, bảo toàn snapshot đích, checkpoint, khả năng tiếp tục và kiểm tra trùng.
- Phiên bản duy nhất ở `VERSION`, việc tồn đọng ở `TODOS.md`, lịch sử ở `CHANGELOG.md`.
- File `.ps1` dùng UTF-8 BOM và CRLF.

## Build và test
- Build/test bằng một lệnh `scripts/Test-Native.ps1` (chi tiết ở mục Testing của `CLAUDE.md` và mục 4 của HANDOFF).
- Clone mới cần chuẩn bị `tools/ffprobe.exe` và thư mục fixture có đường dẫn ngắn (mục 1 của HANDOFF).
- Dùng lock file NuGet và test runner console; `./scripts/Test-WindowsLaunchers.ps1` để kiểm tra PowerShell.
- Tách kết quả fixture khỏi nghiệm thu thực tế.

## Dữ liệu riêng tư
- Kiểm tra app với `--data-dir` trỏ tới fixture riêng. Chỉ dùng tài khoản, bảng, kênh hoặc hồ sơ thật khi có chỉ dẫn riêng.
- Bảo vệ OAuth JSON, token, cookie, URL CDN ký, hồ sơ, log và gói cá nhân.
- Bản cài trên máy build bằng `./scripts/Build-Native.ps1` (có thể nhúng OAuth cá nhân); file đính kèm release
  chỉ là gói `-ForSharing` do `./scripts/Publish-Release.ps1` tạo.
- Mọi build ghi vào thư mục mới. Không đưa `artifacts/`, hồ sơ hay OAuth JSON lên Git hoặc GitHub.
