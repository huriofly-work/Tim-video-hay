# Đặc tả 0.2.2

## Đầu vào và cấu hình

Mỗi công việc nhận một link Douyin trực tiếp hoặc chia sẻ ngắn, tên video và ngách nhập thủ công. Chưa hỗ trợ YouTube/Bilibili làm nguồn. Link kênh YouTube và thư mục tải là cấu hình người dùng; không cố định kênh hoặc trình duyệt/profile.

Bảng mặc định giữ cấu hình đã lưu từ bản trước; người dùng có thể thay bằng link Google Sheets và tên tab. Bốn cột: `Link video mẫu`, `Tên video`, `Ngách`, `Loại video`.

Tên được chuẩn hóa Unicode, loại ký tự không phù hợp tên file, tối đa 30 ký tự; tên thiết bị Windows được đổi an toàn. Ngách tối đa 100 ký tự, từ chối tiền tố công thức. Loại: thời lượng thực ≤ 240 giây là `Shorts`, > 240 là `Long`; nhãn tiếng Việt được dùng theo dữ liệu bảng. Phân loại này chỉ áp dụng cho bảng.

## Bộ điều phối

Chế độ **Chỉ tải video** nhận link Douyin, tên và thư mục riêng; không yêu cầu ngách, kênh hoặc đăng nhập Google. Dùng `detail → download → complete`, bỏ toàn bộ preflight Google/Sheets/YouTube. Có `currentDownloadJobId`, trạng thái và lịch sử riêng; các job cũ không có mode được coi thuộc luồng đăng. API `/api/download/start`, `/stop`, `/resume` đi tới bộ điều phối tải riêng; bridge định tuyến theo ID/lease của job. Dừng và lỗi của hai chế độ không làm thay đổi trạng thái nhau. Ghi media dùng hàng đợi chung để tránh tranh cùng checkpoint/file; task đã dừng khi đang chờ không bắt đầu I/O.

`src/workflow.mjs` quản lý một công việc: `preflight → detail → sheet → download → youtube → complete`. Phục hồi có bước `youtube-confirm`. Trạng thái: chạy, chờ người dùng, lỗi, dừng, hoàn tất. Kết quả từng bước được lưu độc lập.

Preflight kiểm tra đăng nhập/quyền sửa Sheets và nhận diện đúng kênh Studio trước khi ghi hoặc tải. Douyin cần đúng ID, thời lượng dương và địa chỉ media. Chỉ đi tiếp sau khi xác minh bước trước.

Mỗi profile có browserId lưu trong tiện ích; mỗi tab ứng dụng có sessionId. Job trình duyệt có lease duy nhất. Profile/phiên khác hoặc kết quả muộn không được điều khiển job. Worker không phản hồi trong 45 giây chuyển sang chờ tiếp tục; upload mất phản hồi có kết quả chưa xác định. Tải Node có AbortController độc lập.

Cài đặt được chụp khi tạo job. Không đổi cài đặt khi đang chạy. Video đã có kết quả hoặc upload chưa rõ trên cùng kênh bị chặn đăng lại; nhận diện kênh theo ID để chống trùng qua URL handle/URL channel khác nhau.

## Tích hợp trình duyệt

Chromium MV3: `background-v2.js`, `browser-driver.js`, `sheets-browser.js`, `youtube-studio.js`. Điều khiển giao diện qua `chrome.debugger`; mỗi lệnh kiểm tra origin và job còn hoạt động. Không thực hiện đăng nhập, giải CAPTCHA, gọi Google/YouTube API hay endpoint nội bộ để ghi/upload.

Link kênh được chuẩn hóa/kiểm tra; kênh dạng handle được nhận diện từ trang kênh, rồi xác minh quyền trong Studio. Khi sai kênh, thiếu quyền, thiếu đăng nhập hoặc không nhận diện được giao diện, chờ người dùng. Không tự chọn kênh đầu tiên.

## Google Sheets

`POST /api/sheets` chuẩn hóa link https của docs.google.com (bảng thường, không nhận link bảng xuất bản), kiểm tra tên tab 1–100 ký tự và lưu riêng ID/tab. Cần cookie/Origin local, không cần ghép tiện ích hoặc cấu hình kênh. Không truy cập/ghi Google khi lưu cấu hình. Chặn đổi khi luồng đăng đang chạy; job cũ giữ snapshot bảng/tab và checkpoint cũ, bảng mới áp dụng khi tạo job mới. Không chặn tải riêng.

Đọc vùng bảng qua thao tác copy và clipboard; đọc thanh công thức trước khi sửa ô. Xác định bốn vị trí theo tiêu đề thật trong 30 hàng đầu, giới hạn A:CV và tối đa 100.000 hàng. Không xác định được thì dừng.

Dùng hàng cuối trống bốn cột trong vùng dữ liệu hoặc hàng tiếp theo. Link đã có được dùng lại khi cả bốn ô hiện hữu hợp lệ; không ghi đè tên/ngách cũ. Công việc ghi dở giữ số hàng, kiểm tra lại giá trị và chỉ ghi ô còn trống. Công thức trả rỗng vẫn được coi đã dùng. Chỉ ghi bốn ô, không dán cả hàng hoặc sửa dropdown/cột khác. Xác minh lại bốn ô và chỉ báo thành công khi có tín hiệu lưu vào Drive.

Không có khóa giao dịch khi nhiều người cùng sửa bảng; phát hiện thay đổi thì dừng. Dropdown từ chối ngách mới cần xử lý thủ công; không tự sửa quy tắc toàn cột.

## Tải và YouTube

Đọc media do trang Douyin đã cung cấp, chọn độ phân giải thực tế cao nhất truy cập được, kiểm tra audio/video/thời lượng bằng ffprobe. Giữ nguyên nội dung, không chuyển mã. Không tải bìa/phụ đề; không gọi file phát là bản upload gốc đã xác minh.

Studio: xác minh kênh trước khi chọn file; đánh dấu upload chưa xác định trước khi gửi file; giữ ID ngay khi xuất hiện. Không nhập/sửa tiêu đề hoặc trường tùy chọn. Luôn chọn và xác minh Không dành cho trẻ em, kể cả mặc định kênh đang là dành cho trẻ em; không có lựa chọn audience trong UI. Chọn Unlisted, xác minh trước khi Save và kiểm tra lại trang chỉnh sửa đúng ID/kênh/chế độ/audience. Thiếu xác minh thì chờ xử lý trên video đã có, không gửi lại file. Backend chỉ nhận hoàn tất với audience `not-kids`. Yêu cầu tiện ích 0.2.1 cho hợp đồng này; tiện ích cũ không được nhận hoặc hoàn tất job.

Nếu đã có video/bản nháp khi gián đoạn, không gửi lại file. Đã có ID thì chỉ kiểm tra video đó. Chưa rõ ID thì người dùng đối soát kết quả. Không coi Private là thành công; không cam kết tiếp tục đúng byte của upload đã gián đoạn.

## Máy chủ và lưu trữ

`src/server-v2.mjs` chỉ bind loopback; kiểm tra Host, cookie phiên trang, Origin của mutation; bridge dùng token và cặp profile/session. Không còn API tìm kiếm, AI, Apps Script hoặc OAuth.

`src/store.mjs` lưu schema 2, sao chép nguyên state cũ trước migration; giữ cấu hình bảng/thư mục. Lịch sử cũ được lưu trong backup. `src/media.mjs` giữ checkpoint riêng, kiểm tra lại file trước khi tái sử dụng. File người dùng đã tải không bị xóa khi chuyển đổi.

`source-douyin.mjs` chỉ giải link Douyin và kiểm tra từng redirect. Thêm nguồn sau này cần bổ sung bộ giải link/đọc video theo platform; bản đầu không có tùy chọn nguồn chưa hoạt động.
