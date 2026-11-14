# Kiểm tra bản 0.2.2

Ngày: 03/10/2026.

## Phạm vi đã kiểm tra

Bộ kiểm thử `test/v2` gồm **56 test chức năng**, chạy bằng `.runtime\node.exe scripts\test.mjs` với thư mục tạm trong workspace. Bằng chứng lần chạy cuối: `test-output/v2-sheets-tests.txt`.

- Biên 239,999 / 240 / 240,001 giây, tên file Windows, link kênh và redirect Douyin.
- Migration giữ bản sao state nguyên vẹn, giữ bảng/thư mục và bỏ cấu hình AI khỏi schema hoạt động.
- Profile/session/lease, kết quả muộn, dừng/khởi động lại, mất worker và snapshot cài đặt.
- Luồng kiểm tra đăng nhập/kênh trước ghi; phục hồi không lặp bước đã hoàn thành; upload chưa rõ bị chặn gửi lại.
- HTTP cookie, Host/Origin, bridge token và liên kết profile.
- Chỉ ghi bốn ô không liền nhau, nhãn loại Việt, công thức trả rỗng, dữ liệu người khác sửa và ghi dở.
- Chọn chất lượng từ nguồn quan sát, đối chiếu độ phân giải thực, fallback có cảnh báo, file không bị chuyển mã và checkpoint được kiểm tra lại.
- Studio: quyền kênh, ô chọn file duy nhất trong dialog, đánh dấu trước gửi file, không điền tiêu đề, chọn Unlisted và Không dành cho trẻ em trước Save kể cả mặc định kênh khác; Private hoặc dành cho trẻ em chưa được coi hoàn tất. Tiện ích cũ không được nhận/hoàn tất job.
- Đổi bảng/tab: chuẩn hóa URL/tab, cookie/Origin, lưu độc lập kênh/tiện ích, không tác động tải riêng, chặn đổi giữa luồng đăng, job dừng vẫn giữ bảng cũ, cấu hình lỗi không ghi dở.

Các test trình duyệt sử dụng giả lập; test tải sử dụng nguồn/probe giả lập. Kiểm tra cú pháp không chứng minh selector phù hợp giao diện thật hoặc Google đã chấp nhận thao tác.

Máy chủ bản mới đã khởi động trên cổng 43127, HTTP 200 và header phiên bản 0.2.2. State schema 2 giữ dữ liệu cũ, cấu hình audience nay cố định `not-kids`. Đã kiểm tra trong IAB mục Google Sheets với link/tên tab/nút lưu, không còn selector đối tượng người xem, vẫn có phần Chỉ tải video độc lập; không có lỗi JavaScript được ghi nhận. Trang báo đúng chưa kết nối tiện ích 0.2.1 ở profile này và khóa nút chạy; nút lưu bảng vẫn dùng được. Đây chỉ là kiểm tra giao diện local, không phải thao tác Google hoặc tải Douyin thật.

## Giới hạn hiện tại

Đã bổ sung 7 test tải riêng: không cần Google/kênh/ngách; giữ nguyên upload đang chờ đối soát; dừng/tiếp tục đúng job; phục hồi sau restart; API/bridge định tuyến riêng; tuần tự hóa checkpoint chung; task bị dừng trong hàng chờ không bắt đầu ghi file. Các test này dùng nguồn/media giả lập, không phải tải Douyin thật. Tiện ích nay yêu cầu phiên bản 0.2.1 để xác minh đối tượng người xem cố định.

Thêm test tên file: cùng nguồn nhưng tên khác tạo file theo tên vừa nhập, giữ nguyên file trước đó. Checkpoint phân biệt thư mục/ID/tên để lượt tải riêng không lấy nhầm tên của lượt điền bảng/đăng.

Chưa nghiệm thu Douyin/Sheets/Studio thật, chưa ghi bảng hoặc upload video để kiểm tra. Computer Use đã dừng thao tác Cốc Cốc vì chưa hỗ trợ thực thi chính sách URL cho trình duyệt Windows hiện tại. Không dùng cách điều khiển khác để vượt giới hạn đó.

Các selector, clipboard, tín hiệu lưu Sheets, lựa chọn file và các bước Studio phải được kiểm tra trong profile đã đăng nhập. Khi không nhận diện được UI, chương trình chờ và báo cần xử lý. Khả năng tương thích Chrome/Edge/Cốc Cốc là mục tiêu của extension Chromium, chưa phải bằng chứng chạy trên cả ba trình duyệt.

Không có khóa giao dịch Sheets giữa nhiều người cùng sửa; không bảo đảm khôi phục đúng byte upload Studio khi mất phiên. Không có bằng chứng file phát Douyin là file gốc người đăng đã upload.

## Bảo toàn và chuyển đổi

- Mã/state trước chuyển đổi: `data/rebuild-backups/before-v2-20261003-111233.zip`.
- Bản sửa riêng mốc 4 phút: `data/rebuild-backups/boundary-20261003-110646`.
- Runtime/model không còn sử dụng: `data/rebuild-backups/unused-runtime-20261003`.
- Lần mở bản mới đầu tiên sao chép state trước schema 2 thành `data/state-before-v2-*.json`.
- Không chuyển lịch sử tìm/duyệt cũ vào lịch sử công việc mới; có thể tra cứu/khôi phục từ bản sao. Không xóa video, checkpoint cũ hoặc `google.dpapi`.

Bộ test 208 của bản 0.1.19 chỉ là baseline trước chuyển đổi, không dùng làm số kiểm thử cho bản mới.

