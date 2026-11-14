# Chuyển sang Windows/API

> Tài liệu lịch sử của giai đoạn chuyển sang bản Windows (khoảng 0.3.0–0.3.5), không cập nhật theo mã. Từ 0.3.11 app không dùng yt-dlp; từ 0.3.46 chỉ còn một bản build không nhúng Google client. Trạng thái hiện tại xem [README.md](README.md) và [docs/HANDOFF.md](docs/HANDOFF.md).

Đường chạy mặc định WPF/.NET 10, source native, build scripts/Build-Native.ps1, portable artifacts/windows-x64. Node/web/extension/AI đã bỏ khỏi chương trình phát triển. Mã/runtime cũ ở legacy/browser-v022 phục vụ khôi phục, không xóa data/video.

Đã triển khai form/lịch sử, SQLite checkpoint, DPAPI, OAuth PKCE/Picker, Sheets, resumable upload, yt-dlp/ffprobe, tray/single instance, per-user install script. Xem API-ARCHITECTURE.md/SPEC.md.

0.3.5 đã thêm wizard bảng/tab → kênh → xác nhận, hai tài khoản độc lập, hủy dọn draft và đóng gói Desktop client không chứa token. Google Cloud project/client thật đã tạo và nhúng, bật đủ bốn API, thêm email thử nghiệm đã được xác nhận. Còn nghiệm thu OAuth/Sheets/upload trên đích người dùng chọn và chuẩn bị hồ sơ audit upload khi cần Unlisted. Douyin mặc định tự tạo/lưu phiên khách; cookie nhập thủ công là dự phòng khi phiên khách bị chặn. Nghiệm thu theo VALIDATION.md trước khi xóa legacy/ký mã/phát hành. Kiểm tra installer Windows sạch và GPL binary notices/source trước public release.
