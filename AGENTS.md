# Quy ước làm việc · Tìm video hay

## Phạm vi
- App chỉ để chủ repo tự dùng trên máy mình; repo public, chưa cấp license.
- Commit, Issue, PR và release notes đều công khai: không ghi email, đường dẫn máy, ID project Google
  hay chi tiết tài khoản thật.

## Bắt đầu
- Đọc [docs/HANDOFF.md](docs/HANDOFF.md) trước.

## Code
- Phát triển bản C#/.NET 10 WPF trong `native/`; `legacy/browser-v022/` chỉ để đối chiếu hoặc khôi phục.
- Khi sửa luồng job, bảo toàn snapshot đích, checkpoint, khả năng tiếp tục và kiểm tra trùng.
- Phiên bản duy nhất ở `VERSION`; việc tồn đọng ghi ở `TODOS.md`.
- File `.ps1` dùng UTF-8 BOM và CRLF.
- Đổi bản ffprobe thì sửa cả `packaging/tool-manifest.public.json` lẫn `FFMPEG_*` trong `.github/workflows/ci.yml`.

## Build và test
- Lệnh build + test chuẩn, chạy từ gốc repo trên Windows x64 có .NET SDK 10:
  `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Native.ps1`
- Cần `tools/ffprobe.exe` đúng hash trong `packaging/tool-manifest.public.json`; clone mới chạy
  `./scripts/Prepare-NativeTools.ps1 -FfprobePath <ffprobe-đúng-hash>` trước.
- Giữ đường dẫn fixture ngắn; đường dẫn khoảng 250 ký tự làm SQLite báo lỗi 14.
- Chỉ kiểm tra gói: `./scripts/Build-Native.ps1 -OutputDirectory <thư-mục-mới>` rồi
  `./scripts/Test-ReleasePackage.ps1 -PackageDirectory <thư-mục-đó>`.
- Smoke UI: chạy exe đã build với `--data-dir <profile-fixture> --smoke-test <ảnh.png> --smoke-wizard`.
  Không chạy launcher mặc định khi test vì nó dùng profile thật.
- Các ca HTTP/CDN/Google trong runner dùng giả lập; báo kết quả fixture tách khỏi nghiệm thu trên tài khoản hay đích thật.

## Dữ liệu riêng tư
- Chỉ dùng tài khoản, bảng, kênh hoặc hồ sơ thật khi có chỉ dẫn riêng.
- Không đưa `artifacts/`, hồ sơ, log, OAuth JSON, token, cookie hay URL CDN ký lên Git hoặc GitHub.
- Chỉ có một bản build, không nhúng Google OAuth, dùng chung cho máy mình và release; OAuth client chỉ nhập
  trong app và lưu DPAPI trong hồ sơ. Không thêm lại đường nhúng vào build.
