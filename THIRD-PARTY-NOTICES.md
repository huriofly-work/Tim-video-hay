# Thư viện và công cụ

App 0.3 dùng .NET 10/WPF (Microsoft, MIT), Microsoft.Data.Sqlite (Microsoft, MIT, kèm SQLitePCLRaw và SQLite), System.Security.Cryptography.ProtectedData (Microsoft, MIT). Phiên bản chính xác và các dependency nằm trong packages.lock.json của từng project. Nguồn .NET: https://github.com/dotnet/runtime và https://github.com/dotnet/wpf; SQLite: https://sqlite.org/; SQLitePCLRaw: https://github.com/ericsink/SQLitePCL.raw.

Từ bản 0.3.11, app không chạy hoặc đóng gói yt-dlp; dùng bộ tải HTTP nội bộ.

ffprobe được sao chép từ bản standalone đã có trên máy phát triển: FFmpeg 9.0.2 full_build của https://www.gyan.dev/ffmpeg/builds/. Bản này sử dụng GPLv3. Nguồn FFmpeg phiên bản tương ứng: https://ffmpeg.org/releases/ffmpeg-9.0.2.tar.xz; thông tin build của nhà cung cấp: https://www.gyan.dev/ffmpeg/builds/. App chỉ chạy ffprobe thành tiến trình để đọc metadata, không link thư viện FFmpeg và không chuyển mã.

Giấy phép công cụ nằm trong tools/licenses ở bản đóng gói; tool-manifest.json ghi phiên bản/hash. Bản portable hiện dành cho kiểm tra local. Trước khi phân phối chính thức, cần hoàn tất tập hợp notices/nguồn tương ứng cho toàn bộ binary và dependency đóng gói và kiểm tra trên Windows sạch. Chữ ký số tùy chọn; không bắt buộc cho bản portable thử nghiệm.

Microsoft.Web.WebView2 SDK 1.0.4258.31 (Microsoft): https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.4258.31. Bản gói kèm nguyên văn LICENSE/NOTICE từ NuGet trong tools/licenses/WebView2-LICENSE.md và WebView2-NOTICE.md. WebView2 Evergreen Runtime do Microsoft cung cấp/cập nhật riêng, không nằm trong ZIP của app. Không có Chromium fixed runtime hoặc extension đi kèm.