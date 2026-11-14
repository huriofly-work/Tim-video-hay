# Tìm video hay

@AGENTS.md

## Testing

Lệnh build + test chuẩn, chạy từ gốc repo trên Windows x64 có .NET SDK 10:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Native.ps1
```

- Đạt khi exit 0 và runner báo toàn bộ ca đạt.
- Cần `tools/ffprobe.exe` đúng hash trong `packaging/tool-manifest.public.json`. Clone mới chạy
  `./scripts/Prepare-NativeTools.ps1 -FfprobePath <ffprobe-đúng-hash>` trước.
- Giữ đường dẫn fixture ngắn; đường dẫn khoảng 250 ký tự làm SQLite báo lỗi 14.
- Chỉ kiểm tra gói: `./scripts/Build-Native.ps1 -OutputDirectory <thư-mục-mới>`
  rồi `./scripts/Test-ReleasePackage.ps1 -PackageDirectory <thư-mục-đó>`.
- Smoke UI: chạy exe đã build với `--data-dir <profile-fixture> --smoke-test <ảnh.png> --smoke-wizard`.
  Không chạy launcher mặc định khi test vì nó dùng profile thật.
- Các ca HTTP/CDN/Google trong runner dùng giả lập; kết quả fixture không thay cho nghiệm thu trên tài khoản hay đích thật.

## Release và cài bản mới
- Dùng skill `release`.
