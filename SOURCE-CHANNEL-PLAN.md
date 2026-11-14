# Kênh nguồn và chống trùng — 0.3.18

## Quy tắc

- Cùng file Sheets, tab Kênh nguồn, cột Link kênh / Tên kênh / Đăng ký / Ngách / Dạng nội dung. Giữ các cột khác.
- Khóa sec_uid, link chuẩn https://www.douyin.com/user/{sec_uid}; kênh đã có cập nhật cùng dòng và báo rõ cập nhật, chưa có thì thêm mới. Không dùng tên làm khóa.
- Đăng ký là follower_count (粉丝) từ profile đúng kênh; không lấy tổng likes hoặc số 0 placeholder trong feed video.
- Tên ngoài Latinh dịch nghĩa bằng Google Translate miễn phí, không key/phí. Tên Latinh gồm tiếng Việt có dấu giữ nguyên. Dịch lỗi thì dừng; chỉ gửi tên công khai, không cookie/OAuth.
- Ngách dùng ngách công việc. Dạng nội dung dựa trên tối đa 10 video công khai mới nhất theo thời gian đăng, bỏ bài ảnh/carousel; ghim cũ không thay mẫu mới. Kênh thực sự ít hơn 10 video khi đã đọc hết trang thì dùng toàn bộ video có. Không có video/thiếu duration thì dừng.
- Mặc định ≤300000 ms Short; lớn hơn Long; từ 0.3.18 dùng mốc được lưu trong công việc; có cả hai All. Không tải 10 file để đo, không quyết định eligibility YouTube Shorts.
- Xóa dòng video giữ dòng kênh; không tự bổ sung vào công việc cũ đã hoàn tất.

## Nguồn và phiên

Bộ đọc kênh nguồn lấy 粉丝 và 10 video mới nhất qua trang kênh, dùng khách trước và phiên tài khoản cuối khi cần; phiên được giữ trong app. Riêng thông tin/bìa video ưu tiên HTTP với ttwid khách và nguồn feed, không dùng tài khoản chỉ vì thiếu metadata. WebView video chỉ tham gia khi có yêu cầu xác minh hoặc đăng nhập rõ ràng; bộ tải CDN không gửi cookie tài khoản. Không extension, không nhập/xuất cookie thủ công, không lấy profile Chrome/Edge, không API trả phí.

Xác thực video ID, sec_uid của từng bài, 粉丝, thời gian đăng, phân trang liên tiếp, ghim, trùng ID và duration. Rỗng/bị chặn không coi là kênh không có video. JSON tối đa 4 MB, tối đa 5 trang. Không OCR/nhận dạng giọng nói. Đăng nhập không bảo đảm vượt mọi chặn của Douyin.

## Luồng và ghi

1. Kiểm tra ID nguồn trong lịch sử trước xếp hàng. Theo phần chọn, trước tải/ghi/đăng kiểm tra thư mục cùng tên kể cả rỗng, video trong tab video và tiêu đề hiện tại trên YouTube. Trùng thì dừng và báo đích.
2. YouTube đọc playlist uploads bằng OAuth chủ kênh, phân trang và lấy title hiện tại, gồm private/unlisted; không dùng search. Chuẩn hóa Unicode, trim, không phân biệt hoa/thường. Kiểm tra lại trước mở upload mới; session/ID đang gửi tiếp tục đúng lượt cũ.
3. Lượt chọn Bảng đọc thời lượng và kênh/dịch tên trước tải, lưu snapshot checkpoint. Thiếu dữ liệu thì chưa ghi hoặc upload.
4. Kiểm tra cả hai tab/header/dropdown/bảo vệ/ô gộp/công thức; kênh có nhiều dòng thì dừng. Đọc lại trước ghi. Một spreadsheets.batchUpdate ghi 4 ô video và 5 ô kênh nguyên tử; Đăng ký là số, các ô khác literal string. Không đổi cột khác.
5. Xác minh cả hai dòng; mất phản hồi dùng intent/snapshot tìm kết quả cũ, không append lại. Tránh sửa/sắp xếp bảng lúc app ghi vì API không khóa chỉnh sửa của mọi người.

## Nâng cấp và bằng chứng

Schema 7 giữ checkpoint/đích/kết quả cũ. Intent bốn ô đã bắt đầu trước nâng cấp hoàn tất theo snapshot cũ, không bất ngờ thêm kênh. Bổ sung/ghi lại đã xóa là lượt mới có kênh; binary cũ từ chối schema mới.

Ngày 10/10/2026: profile mẫu ô tô trả 28.344 粉丝; Google Translate dịch được tên. Mở trực tiếp đúng kênh bằng WebView2 và phiên đã đăng nhập đọc được đủ 10 video, tất cả dưới 5 phút: Short. Lượt mở video mẫu cũ 7673782911702748458 chuyển sang video đề xuất, không nhận diện được kênh; không coi đây là bằng chứng chặn danh sách kênh. Người dùng đã xác nhận thông báo video không tồn tại của chính link này; app thử thật nhận thông báo và dừng sớm. Link mới nhất 7694474149682580782 mở đúng trang và toàn bộ luồng đọc kênh thành công (lần đọc API tiếp theo trả 28.343 粉丝). Chưa nghiệm thu ghi hai tab Google thật. Các kiểm tra HTTP/logic dùng mock tách rõ với phép thử thật tại VALIDATION.md.
### Cấu hình từ bản 0.3.18

Tab nguồn và tab video được chọn riêng trong cùng file; năm cột nguồn được gán theo vị trí trong Chọn cột kênh nguồn, không bắt tên tiêu đề cố định. Mốc Short/Long chỉnh trong Phân loại nội dung, mặc định 5 phút; dùng cùng mốc đã lưu trong công việc cho video và các video mới nhất của kênh. Đổi mốc/cột cài đặt không làm đổi checkpoint của công việc cũ.
