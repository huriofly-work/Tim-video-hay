# Kế hoạch xây dựng lại — 03/10/2026

Định hướng tiếp theo: app Windows C#/.NET 10 + WPF, Google OAuth/Picker/API, không nhập link bảng/kênh và không giữ Node/web frontend/extension/AI. Xem [Thiết kế app Windows](API-ARCHITECTURE.md) cho kiến trúc đích, phần cần loại bỏ và điều kiện nghiệm thu. Nội dung bên dưới lưu lại việc triển khai bản browser 0.2.2, không còn là kế hoạch phát triển tiếp theo.

## Yêu cầu đã chốt

Một link Douyin + tên video + ngách → kiểm tra đăng nhập/kênh → điền bảng qua trình duyệt → tải nguyên video → đăng YouTube Studio không công khai. Dùng đúng trình duyệt/profile đang mở ứng dụng; người dùng đăng nhập sẵn và tự nhập link kênh để liên kết. Thiếu đăng nhập hoặc quyền thì thông báo và chờ.

Giữ bốn cột cũ, cho đổi link bảng/tên tab trong cài đặt. ≤ 4 phút là Short, > 4 phút là Long, chỉ dùng để phân loại trong bảng. Không nhập tiêu đề YouTube; giữ Studio mặc định. Không AI, bìa, phụ đề, biên tập, playlist, lịch đăng hoặc thêm cột kết quả vào bảng. Luôn chọn Không dành cho trẻ em và kiểm tra sau lưu; không có mục lựa chọn đối tượng. Chưa triển khai YouTube/Bilibili làm nguồn.

## Công việc và trạng thái

| Giai đoạn | Kết quả |
| --- | --- |
| Bảo toàn bản cũ | Đã sao lưu mã/state trước chuyển đổi vào ZIP; migration sao chép state nguyên vẹn trước thay schema |
| Bỏ luồng tìm kiếm | Đã gỡ khám phá, chiến lược, lọc/xếp hạng, duyệt/loại, AI đặt tên/chọn ngách và giao diện/API cũ |
| Đơn giản hóa kết nối | Đã gỡ Apps Script/OAuth/Google Cloud; dùng extension của đúng browser/profile và channel URL do người dùng nhập |
| Luồng công việc mới | Đã xây điều phối, checkpoint, dừng/tiếp tục, bảo vệ profile/lease và đối soát upload gián đoạn |
| Sheets và tải | Đã viết đọc/ghi/xác minh bốn ô qua UI, chống ghi đè; tải/kiểm tra chỉ video |
| YouTube Studio | Đã viết nhận diện kênh, chọn file, giữ mặc định, chọn Unlisted và kiểm tra kết quả |
| Dọn phụ thuộc/tài liệu | Đã bỏ script/cấu hình AI/ASR, không khởi động Ollama; runtime/model cũ chuyển vào backup; README/SPEC viết lại |
| Kiểm thử tự động | Đã có test cho luồng, migration, dữ liệu, HTTP, media và thao tác trình duyệt giả lập; xem VALIDATION.md |
| Nghiệm thu dịch vụ thật | Chưa hoàn tất: công cụ kiểm tra Cốc Cốc bị dừng do giới hạn chính sách URL của trình duyệt Windows |

## Thành phần đã loại khỏi chương trình

`src/app.mjs`, `analysis`, `discovery`, `strategy`, `topic`, `downloader`, `google`, máy chủ và helper extension cũ; giao diện tìm/duyệt, thiết lập Sheets/Apps Script, popup/content tìm kiếm; script Ollama, Whisper/transcribe, DPAPI setup và bộ kiểm thử riêng cho tính năng đã bỏ.

`.runtime/ollama`, `.venv`, `data/models` đã chuyển vào `data/rebuild-backups/unused-runtime-20261003`. Đây là bản lưu để khôi phục, không còn trên đường chạy và chưa xóa để giải phóng dung lượng. `.runtime/node.exe` được giữ. Video, checkpoint, bằng chứng kiểm tra cũ và thông tin mã hóa được bảo toàn; không xóa toàn bộ `data`.

## Cấu trúc mới

- `src/server-v2.mjs`: máy chủ local và API công việc.
- `src/workflow.mjs`: trạng thái, profile/session, checkpoint và phục hồi.
- `src/source-douyin.mjs`, `channels.mjs`: kiểm tra/chuẩn hóa nguồn và link kênh.
- `src/domain.mjs`, `media.mjs`, `store.mjs`, `setup.mjs`: dữ liệu, tải/kiểm tra file, lưu trạng thái và thư mục.
- `extension/background-v2.js`, `pair-v2.js`: kết nối đúng profile và chạy công việc.
- `extension/observe.js`, `douyin-v2.js`: đọc đúng video Douyin.
- `extension/browser-driver.js`, `sheet-policy.js`, `sheets-browser.js`, `youtube-studio.js`: điều khiển UI và xác minh.
- `public/*-v2.*`: ba đầu vào, cài đặt kênh/thư mục, tiến độ và lịch sử.

## Nghiệm thu tiếp theo

Bổ sung 0.2.1: giữ phần **Chỉ tải video** độc lập (link/tên/thư mục), không điền Sheets, không đăng YouTube, không cần cấu hình Google/kênh/ngách. Đã bổ sung trạng thái, lịch sử và dừng/tiếp tục riêng; nghiệm thu thêm trường hợp chưa cấu hình Google, đang có upload cần đối soát và hai chế độ cùng tải một nguồn/thư mục.

1. Tải lại extension 0.2.1 trong profile người dùng đang đăng nhập; xác minh thông báo khi chưa đăng nhập và khi thiếu quyền.
2. Chạy một link thật; kiểm tra đúng bảng/tab/hàng/bốn ô, công thức/cột khác giữ nguyên, nhãn dropdown hợp lệ.
3. Kiểm tra file tải, chất lượng/audio/thời lượng và byte không bị biên tập.
4. Kiểm tra đúng kênh/file, tiêu đề Studio mặc định, Unlisted và Không dành cho trẻ em sau lưu, ID đầu ra thực tế.
5. Thử gián đoạn Sheets và upload, xác minh tiếp tục không tạo hàng/video trùng; thử chuyển profile phải bị chặn.

Không coi bộ kiểm thử giả lập là nghiệm thu trên dịch vụ thật. Cần sửa selector hoặc luồng UI nếu thực tế khác; không đoán tọa độ hoặc tiếp tục ghi/đăng khi chưa xác minh.
