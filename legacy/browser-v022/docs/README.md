# Tìm video hay 0.2.2

Thiết kế cho bản tiếp theo: app Windows C#/.NET 10 + WPF, Google API/OAuth, chọn bảng/kênh không nhập link, bỏ Node/web frontend/extension/AI: [API-ARCHITECTURE.md](API-ARCHITECTURE.md). Bản 0.2.2 bên dưới vẫn chạy luồng browser; chưa có bản Windows mới.

Nhập **một link Douyin, tên video và ngách**. Chương trình kiểm tra đăng nhập/kênh, điền bốn cột Google Sheets, tải video rồi đăng qua YouTube Studio ở chế độ **không công khai**.

Ứng dụng dùng tiện ích trong **đúng trình duyệt và profile đang mở trang ứng dụng**. Link kênh do người dùng cấu hình, không cố định trong mã. Không cần Google Cloud, OAuth, API key, Apps Script hoặc mô hình AI.

## Cách mở và sử dụng

1. Chạy `Mo-ung-dung.cmd` hoặc `Start.ps1`. Địa chỉ ứng dụng: `http://127.0.0.1:43127/`. Có thể tự mở địa chỉ này bằng trình duyệt/profile muốn sử dụng.
2. Trong profile đó, mở trang quản lý tiện ích của Chrome, Edge hoặc Cốc Cốc, bật chế độ nhà phát triển và **Load unpacked / Tải tiện ích đã giải nén** từ thư mục `extension`. Nếu đã cài bản cũ, tải lại tiện ích **0.2.1**, chấp nhận quyền mới và F5 trang ứng dụng.
3. Đăng nhập Google Sheets và YouTube Studio trong cùng profile. Chương trình sẽ thông báo và chờ khi thiếu đăng nhập, quyền sửa bảng, quyền đăng kênh hoặc cần xác minh.
4. Trong mục **Google Sheets**, điền link bảng và tên tab rồi bấm **Lưu bảng Google Sheets**. Điền link kênh YouTube, chọn thư mục lưu video và lưu cài đặt. Mọi video luôn được chọn **Không dành cho trẻ em**, không có mục lựa chọn đối tượng người xem.
5. Nhập link Douyin, tên video và ngách, bấm **Điền bảng · Tải · Đăng YouTube**. Giữ tab ứng dụng mở. Khi cần xử lý thủ công, làm trong tab chương trình đã mở rồi bấm **Tiếp tục**.

Website local cần tiện ích để điều khiển các tab và chọn file video. Trình duyệt/profile chưa có tiện ích phù hợp sẽ báo chưa kết nối; ứng dụng không tự chuyển sang profile khác. Đăng nhập Google do người dùng thực hiện, chương trình không lưu mật khẩu hay xuất cookie.

## Chỉ tải video

Mở phần **Chỉ tải video** (có liên kết ở đầu trang), nhập link Douyin, tên video và thư mục lưu riêng, rồi bấm **Chỉ tải video về máy**. Không cần ngách, cấu hình kênh hoặc đăng nhập Google. Chế độ này chỉ đọc đúng video Douyin và tải/kiểm tra file; không mở Sheets hoặc Studio, không tải bìa/phụ đề.

Có trạng thái, lịch sử, nút dừng/tiếp tục riêng. Kết quả upload đang chờ đối soát không cản tải riêng, và dừng tải riêng không dừng luồng điền bảng/đăng. Tiện ích vẫn cần kết nối trong đúng profile để đọc nguồn Douyin. Các thao tác đọc tab và ghi file được xếp lần lượt để không tranh cùng file/checkpoint. Thư mục tải riêng được nhớ riêng, không đổi nơi lưu của luồng đăng.

## Những quy tắc được giữ

- Có thể đổi bảng/tab trong mục Google Sheets; mặc định giữ cấu hình cũ. Bảng phải có bốn cột `Link video mẫu`, `Tên video`, `Ngách`, `Loại video`. Không thêm link YouTube/trạng thái vào bảng.
- Lưu link/tab chỉ lưu cấu hình local; đăng nhập, quyền sửa và cột thật được kiểm tra khi chạy. Không cần cấu hình kênh hoặc kết nối tiện ích để lưu bảng. Chặn đổi khi luồng đăng đang chạy; công việc đang dừng/chờ giữ bảng đã chụp lúc bắt đầu, bảng mới áp dụng cho công việc mới. Tải riêng không bị ảnh hưởng.
- Tên nhập thủ công, tối đa 30 ký tự sau chuẩn hóa cho tên file Windows. Ngách nhập thủ công.
- **≤ 240 giây: Short; > 240 giây: Long**, không làm tròn. Dùng nhãn `Video ngắn` / `Video dài` khi bảng đang dùng nhãn đó. Đây là phân loại trong Sheets; không ép định dạng video trên YouTube.
- Tải bản video chất lượng cao nhất truy cập được và kiểm tra file bằng `ffprobe`; giữ nguyên byte, không biên tập hoặc chuyển mã. File phát Douyin có thể khác file gốc người đăng đã tải lên.
- Giữ tiêu đề Studio tự điền từ file nếu có. Không nhập mô tả/hashtag, bìa, phụ đề, playlist hoặc lịch đăng. Trường bắt buộc còn thiếu sẽ yêu cầu người dùng xử lý.
- Chỉ hoàn tất khi đã xác minh Sheets lưu, file tải hợp lệ và đúng video/kênh ở chế độ không công khai, không dành cho trẻ em.

Tiện ích cần quyền điều khiển tab (`debugger`) và clipboard để thao tác Studio/đọc ô Sheets. Trong lúc ghi bảng, clipboard sẽ thay đổi; tránh sao chép, sửa ô hoặc điều khiển cùng các tab đang chạy. Đóng DevTools trên các tab này nếu tiện ích báo không gắn được quyền điều khiển.

## Dừng và tiếp tục

Có checkpoint riêng cho Sheets, tải file và YouTube. Tiếp tục bước lỗi không tự ghi lại bảng hoặc tải lại file đã xác minh. Công thức và dữ liệu khác được kiểm tra trước khi ghi; xung đột khiến công việc dừng.

Khi upload gián đoạn, Studio có thể đã tạo video/bản nháp. Ứng dụng giữ ID nếu đã thấy; nếu chưa có ID, yêu cầu nhập link video đã tạo hoặc xác nhận đã kiểm tra chưa có video/bản nháp. Chương trình không tự upload lại khi kết quả chưa rõ. Khi đã có ID, tiếp tục kiểm tra video đó; nếu còn riêng tư, tự đổi sang không công khai trong Studio rồi tiếp tục.

## Tình trạng kiểm tra và bản sao

Đã có kiểm thử tự động về luồng, dữ liệu, bảo vệ profile, ghi ô, media và upload giả lập. **Chưa nghiệm thu Douyin → Sheets → Studio trên tài khoản thật**. Công cụ Computer Use đã dừng kiểm tra Cốc Cốc vì chưa hỗ trợ thực thi chính sách URL cho trình duyệt Windows hiện tại. Xem `VALIDATION.md` để biết phạm vi kiểm tra.

Mã cũ và state trước chuyển đổi: `data/rebuild-backups/before-v2-20261003-111233.zip`. Runtime/model AI đã chuyển khỏi đường chạy vào `data/rebuild-backups/unused-runtime-20261003`, vẫn giữ để khôi phục, chưa giải phóng dung lượng. Video cũ, checkpoint và thông tin kết nối mã hóa không bị xóa. Khi mở bản mới lần đầu, state cũ được sao chép nguyên vẹn thành `data/state-before-v2-*.json`; giữ cấu hình bảng/thư mục, lịch sử tìm kiếm cũ nằm trong bản sao, không nhập vào lịch sử công việc mới.

Chạy kiểm thử bằng `.runtime\node.exe scripts\test.mjs`. Chương trình dùng Node ≥ 22 và `ffprobe` có trên PATH. Không cần Python, Ollama, Whisper hoặc package Node bên ngoài.
