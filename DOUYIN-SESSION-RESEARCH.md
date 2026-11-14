# Đơn giản hóa kết nối Douyin — 03/10/2026

## Bổ sung bìa khi dùng nguồn dự phòng — 09/10/2026, bản 0.3.13

Feed thường (aid=1128) thiếu các trường poster rõ ràng; feed Douyin Select trên cùng hai endpoint (aid=568863) trả y_cover và y_cover_hor. App lấy đúng aweme_id rồi chỉ ghép các trường bìa vào metadata đã chọn, không thay rendition hoặc endpoint tải nguồn. Áp dụng cả khi dùng mobile fallback và khi web chính có video nhưng web Select thất bại. Không cần cookie tài khoản, bên thứ ba, API trả phí, AI hoặc trình duyệt bổ sung.

- Thử trực tiếp bằng vault khách riêng trên 7673782911702748458, 7659702794928147739 và 7673535793037577472: cả ba tải được hai poster tĩnh đúng ID.
- Đã xem cả sáu ảnh. Ô tô và bọ ngựa đúng thiết kế đã xác nhận; bìa ngang trùng hash với bằng chứng web trước đây. Video mới trả hai thiết kế riêng về đám mây Oort; chưa có ảnh tham chiếu của người dùng để đối chiếu video này.
- Select trả bìa dọc JPEG 1078×1437; đây là bản do Douyin cung cấp, khác bytes/bản WebP 1078×1440 nguồn web trước đây. Bìa ngang ô tô 1440×1080, bọ ngựa 1438×1079, video mới 1080×810. App không tự cắt, đổi định dạng hoặc phóng to; không tuyên bố đây là file ảnh gốc tác giả tải lên.
- Ưu tiên giữ poster nguồn web trong danh sách trước poster feed; khi đã có đủ hai nhóm bìa thì không gọi thêm. Tối đa hai nút, 8 giây/nút và tổng 12 giây cho bước bổ sung. Sai ID/trạng thái, JSON hỏng/quá lớn, HTTP/mạng/timeout không xóa dữ liệu video hoặc bìa có sẵn. Hủy người dùng vẫn dừng ngay.
- Bằng chứng: test-output/douyin-covers-fallback-20261009/{car,mantis,new}/live-covers-result.json; chỉ tải ảnh, không tải lại video, không đọc cookie người dùng, không ghi Sheets/upload YouTube.

Các thử nghiệm web thêm header x-tt-argus đơn lẻ vẫn trả UIFID Not Found; không đưa cách này vào app. Kết quả feed Select được kiểm tra trực tiếp, không suy đoán từ mã bên ngoài. Endpoint chưa phải hợp đồng API công khai dành cho nhà phát triển, nên vẫn có thể thay đổi; nếu thiếu poster thì báo rõ và tiếp tục video hợp lệ.

## Sửa lỗi phiên web — 09/10/2026, bản 0.3.12

- Tái hiện trên video 7673535793037577472 và video ô tô mẫu: endpoint đăng ký khách vẫn cấp ttwid mới (HTTP 200), nhưng web detail/Select trả HTTP 403 với lỗi `Blocked by ArgusSecurityPlugin Uifid Not Found`. Vì vậy lỗi này không được giải quyết chỉ bằng việc thay phiên khách hết hạn.
- Bổ sung nguồn feed công khai của Douyin, qua đúng hai địa chỉ HTTPS cố định `api5-normal-c-hl.amemv.com/aweme/v1/feed/` và `aweme.snssdk.com/aweme/v1/feed/`. Chỉ nhận item có aweme_id khớp tuyệt đối; bỏ các video đề xuất khác, phản hồi lỗi, quá lớn hoặc JSON không hợp lệ. Không dùng dịch vụ bên thứ ba, browser automation hoặc cookie tài khoản cho nguồn này.
- Web có dữ liệu thì vẫn dùng toàn bộ rendition/bìa web. Khi web thiếu UIFID, giữ phiên khách hợp lệ và chuyển nguồn thay vì đăng ký ttwid lặp lại. Nguồn feed được thử một lần qua tối đa hai nút sau các lần thử phiên, trước cookie thủ công, kể cả khi đăng ký ttwid thất bại. Phiên khách hết hạn/hỏng vẫn được thay tự động như trước.
- Feed thường ghi rendition preview 720p. App vẫn tạo hai endpoint tải nguồn ratio=default và kiểm tra luồng thực tế trước khi chọn; probe video mới cho 1920×1080 H.264 + AAC, probe video bọ ngựa cũ vẫn cho 3840×2160 H.264 + PCM float. Không coi metadata 720p là giới hạn thật của file nguồn.
- Feed chưa trả đúng hai bìa do tác giả chọn trong các mẫu đã thử. App không dùng cover/origin_cover là frame thay thế; giữ bìa đã có nếu hợp lệ hoặc báo thiếu bìa rồi tiếp tục video.
- Lỗi cuối phân biệt thiếu UIFID, HTTP từ chối/giới hạn, lỗi mạng và dữ liệu không đúng video. Chỉ lưu/phát thông báo cố định; không đưa body, token, cookie hoặc URL CDN có chữ ký vào log.
- Hồi quy cuối: 149 passed, 0 failed, gồm thay phiên hết hạn, hủy, feed dự phòng, sai ID, giới hạn phản hồi, preview/bản nguồn, giữ file dở và bảo vệ junction. UI WPF bản chia sẻ mở/đóng thành công trong hồ sơ riêng, đọc được hướng dẫn, tải riêng không cần Google; không kết nối Google thật trong phép thử.
- Bằng chứng: `test-output/douyin-session-fix-20261009/`. Tải thật video mới vượt 1 GB từ file nguồn 2.987.596.477 byte rồi dừng chủ động riêng bài thử, giữ phần dở. Chưa tải đủ hoặc kiểm tra hash toàn bộ gần 3 GB; không ghi Sheets/upload YouTube. Chi tiết và giới hạn nghiệm thu được ghi trong VALIDATION.md.

Nguồn tham khảo xác định thay đổi giao thức: [mã MediaCrawler](https://github.com/NanmiCoder/MediaCrawler/blob/main/media_platform/douyin/client.py) và [mã Media-Parser](https://github.com/ucmao/media-parser/blob/main/src/parsers/douyin_parser.py). Kết luận lỗi và khả năng truy cập feed được kiểm tra trực tiếp trên máy này; không dựa vào cam kết của các dự án đó.

## Kết quả thử trực tiếp

- Gọi web detail và Select detail không cookie: hai video mẫu trả HTTP 200 với body rỗng.
- HTTP client mới truy cập trang Douyin/share để nhận cookie tự nhiên: chỉ nhận __ac_nonce, chưa lấy được metadata.
- Chỉ dùng ttwid từ cookie đã lưu, không gửi các cookie đăng nhập: lấy được đúng aweme_id, danh sách rendition cao và y_cover_hor của hai video mẫu.
- Tạo một phiên khách hoàn toàn mới qua POST https://ttwid.bytedance.com/ttwid/union/register/ (không đọc credential người dùng), nhận ttwid từ phản hồi, dùng trên domain Douyin: cả web detail (aid=6383) và Select detail (aid=568863) lấy đúng hai video mẫu.

Phiên mới trả danh sách đến 2160p cho video 7659702794928147739 và 1080p cho 7673782911702748458; Select trả y_cover_hor của cả hai. Đây là kiểm tra metadata, không tải lại toàn bộ video hay ảnh và không xác nhận mọi video đều dùng được với phiên khách.

## Triển khai 0.3.4

Đã triển khai DouyinSession: tự tạo/lưu phiên khách local làm mặc định, chỉ nhận ttwid từ endpoint HTTPS cố định không theo redirect. DPAPI CurrentUser, tái sử dụng tối đa 24 giờ, thử làm mới một lần khi không lấy được metadata. Lỗi tạo phiên không lặp request đăng ký ngay. Tiếp tục dùng pipeline chọn chất lượng và bìa hiện tại, không thay bằng danh sách chất lượng thấp của share page. Cookie tài khoản nhập thủ công chỉ được đọc khi phiên khách thất bại; không đọc database trình duyệt. Không thêm WebView2/extension/browser automation/AI.

Nếu thử nhiều video thực tế sau này cho thấy thường xuyên phải kết nối tương tác, xem xét cửa sổ Kết nối Douyin trong app với profile riêng, thay thao tác xuất cookie thủ công. Khả năng Douyin chạy trong WebView2 chưa được nghiệm thu.

Bản 0.3.3 chỉ nghiên cứu metadata; bản 0.3.4 triển khai vào bộ tải thật. Kiểm tra giao thức: tạo/lưu/tái sử dụng phiên, không đọc cookie dự phòng khi thành công, làm mới có giới hạn, fallback khi offline, thay phiên hết hạn/hỏng, từ chối token không hợp lệ và truyền hủy người dùng. Kết quả tải/ảnh thực tế trong VALIDATION.md.

Kiểm tra bộ tải thật bằng vault thử riêng chỉ chứa phiên khách mới: video 7673782911702748458 Complete, MP4 1920×1080 H.264 + AAC 277.138.676 byte, bìa dọc 1078×1440 và ngang 1440×1080 đúng hash ảnh đã đối chiếu. Video 7659702794928147739 sửa đủ hai bìa bằng phiên khách mới, giữ nguyên hash MOV 4K có sẵn; không tải lại MOV trong phép thử này. Cả hai 0 ghi bảng/0 upload; không tham số user-data/cookie-key, không đọc credential người dùng. CDN có lúc chậm; chưa bảo đảm mọi video.

## Đọc cookie trình duyệt

yt-dlp có --cookies-from-browser, nhưng đọc trực tiếp database Chrome trên Windows dễ vướng App-Bound Encryption. Không chọn đây làm đường kết nối mặc định và không tắt cơ chế bảo vệ trình duyệt. Nguồn chính: [Chrome Security Team](https://security.googleblog.com/2024/07/improving-security-of-chrome-cookies-on.html).

Máy hiện có WebView2 Runtime 154.0.4258.48 qua khóa registry do Microsoft công bố. Việc runtime có sẵn không chứng minh kết nối Douyin qua WebView2 thành công.
