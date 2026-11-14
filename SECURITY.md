# Bảo mật và chia sẻ Tìm video hay 0.3.36

## Bản nào được chia sẻ?

Chỉ có một bản build: thư mục `windows-x64-<phiên-bản>` hoặc ZIP trên GitHub Release tạo từ thư mục đó. Không gửi thư mục mã nguồn, thư mục dữ liệu app, tệp OAuth JSON hoặc bộ công cụ phát triển. Bản này không nhúng OAuth client/project Google, không chứa token, cookie, lịch sử, cấu hình bảng/kênh hay đường dẫn máy của người đóng gói. Mỗi người tự thiết lập Google theo GOOGLE-SETUP.md; tải Douyin riêng không cần Google.

Bản hiện tại chưa ký số. Người nhận nên lấy trực tiếp từ người gửi đã biết; Windows có thể cảnh báo. File release-manifest.json và Install.ps1 kiểm tra tính toàn vẹn để phát hiện file hỏng/thiếu/thay đổi. Nếu kẻ khác thay cả gói lẫn manifest, hash không xác thực được nhà phát hành. Không tắt Defender hoặc SmartScreen để cài. MSIX/chữ ký số là tùy chọn tương lai, không bắt buộc cho bản portable thử nghiệm này.

## Cài trên máy khác

1. Giải nén nguyên thư mục vào vị trí riêng; không chép thêm dữ liệu từ máy người gửi.

2. Chạy TimVideoHay.exe để dùng portable hoặc Cai-dat.cmd để cài và tạo shortcut. Gỡ bằng Go-cai-dat.cmd/shortcut Gỡ, mặc định giữ dữ liệu; xóa hồ sơ phải chọn riêng. Chỉ chạy script từ nguồn đã xác nhận. Không cần quyền quản trị và không tự chạy app. Quy trình cài/cập nhật/gỡ đầy đủ: [INSTALLATION.md](INSTALLATION.md).

3. Chọn thư mục ở Xử lý video, chỉ tích Tải video để thử trước.

4. Muốn dùng Sheets/YouTube: làm mục 7–8 của GOOGLE-SETUP.md; tạo project của chính bạn, bật API, thiết lập External/Test users, tạo Desktop client, nhập JSON trong app rồi chọn bảng/kênh. Không cần gửi JSON cho người phát triển.

5. Đổi máy/tài khoản Windows: cài app sạch và kết nối Google lại. Không chuyển nguyên hồ sơ dữ liệu mã hóa sang máy khác. Video/ảnh có thể sao chép bình thường.

## Dữ liệu và quyền

- App chạy theo người dùng thường. Không có server trung gian của nhà phát triển hoặc chức năng gửi token/cookie về người đóng gói.

- OAuth dùng trình duyệt hệ thống, PKCE và kiểm tra state. Sheets dùng drive.file; YouTube xin quyền upload/đọc. Xóa YouTube cần cấp thêm quyền youtube.force-ssl; đây là quyền rộng của Google, không giới hạn riêng một video. Nút ngắt kết nối xóa token local; muốn thu hồi quyền trên Google, vào https://myaccount.google.com/connections.

- Token Google, phiên upload và payload jobs/settings được mã hóa DPAPI CurrentUser. Thư mục app, credential và tệp database được giới hạn quyền người dùng hiện tại/System. Database vẫn có ID công việc và thời gian ở dạng đọc được; file video/ảnh ở thư mục tải không được mã hóa.

- Phiên Douyin giữ trong douyin-browser riêng của WebView2 với ACL theo tài khoản Windows. Với video, app tự thử HTTP công khai, WebView khách rồi phiên tài khoản khi nguồn trước chưa đủ dữ liệu đúng video. App không hỏi duyệt chuyển phiên. Thiếu bìa tùy chọn không kích hoạt tài khoản. Không chuyển phiên để bỏ qua 429, CAPTCHA đang chờ, video được báo không tồn tại hoặc thao tác đóng/dừng của người dùng. Với kênh nguồn, tài khoản vẫn là phương án cuối để lấy dữ liệu bắt buộc. App thông báo khi dùng phiên tài khoản và không hỏi duyệt. Bộ đọc khách dùng InPrivate/hồ sơ riêng; chỉ nhận ttwid từ bản khách đã đăng ký, không từ bản sao tài khoản. App đọc phản hồi của trang, không xuất/nhập cookie tài khoản, không lưu snapshot phiên riêng hoặc đọc database Chrome/Edge. Cookie khách lưu DPAPI riêng để dùng lại tối đa 24 giờ. Bộ đọc không có host objects/web-message bridge, chặn popup/download/media và quyền thiết bị; chỉ điều hướng chính tới HTTPS www.douyin.com. Nội dung web/assets/iframe do Douyin tải theo cơ chế mạng của WebView2, không áp dụng toàn bộ chính sách DNS của bộ tải C#. Không sao chép thư mục phiên khi chia sẻ; đăng xuất trên trang Douyin trong app.

- Nâng schema 4 lên 5 mã hóa jobs/settings trong một giao dịch, giữ đích và checkpoint; thu gọn database/WAL sau khi chuyển đổi. Không đảm bảo xóa sạch dấu vết trong backup cũ, ổ SSD hoặc bản sao do hệ điều hành tạo. Sau nâng cấp không mở hồ sơ đó bằng app cũ.

- DPAPI/ACL không chống được quản trị viên hoặc mã độc chạy cùng tài khoản Windows. Không chia sẻ hồ sơ Windows hoặc dữ liệu app. Không có cam kết an toàn tuyệt đối hay tuyên bố đã qua kiểm toán bảo mật độc lập.

## Mạng, công cụ và thao tác file

HTTP tải video/bìa do app kiểm soát: HTTPS/443, danh sách miền Douyin/CDN, kiểm tra từng chuyển hướng, chặn địa chỉ IP nội bộ/loopback và kiểm tra địa chỉ DNS ngay khi kết nối. Không tự dùng proxy hệ thống cho Douyin; môi trường bắt buộc proxy có thể không tải được. HTTP tải media không dùng cookie tài khoản; token Google và dữ liệu phiên Douyin không được chuyển sang bộ tải CDN.

ffprobe được kiểm tra SHA-256 theo manifest nhúng trong binary trước mỗi lần chạy. Đọc file local chỉ cho protocol file/pipe; đọc metadata video từ xa đi qua cầu nối loopback tạm có mã ngẫu nhiên, URI nguồn cố định và cùng chính sách mạng. Đây là kiểm tra công cụ con, chưa thay thế chữ ký của toàn bộ app. Bản mới không cần yt-dlp, có bộ đọc công khai/khách và WebView thay thế khi HTTP không đủ dữ liệu.

Bộ tải mở tối đa bốn kết nối song song, mỗi kết nối nhận một vùng tối đa 4 MiB khi máy chủ có validator mạnh và còn từ 8 MiB trở lên; lỗi mạng làm giảm số kết nối 4 → 2 → 1. Khi CDN không cho tải song song hoặc thiếu validator mạnh, app tải tuần tự từng đoạn 32 MiB. Cả hai cách đều thử tối đa ba lần cho lỗi mạng liên tiếp. Tải tiếp chỉ nối partial khi source/format/path và validator/dung lượng máy chủ khớp; lấy validator bằng HEAD (hoặc GET headers nếu HEAD không được hỗ trợ) khi CDN bỏ trường đó ở 206, gửi If-Range theo quy tắc HTTP. Mỗi lần chờ phản hồi hoặc gián đoạn đọc tối đa 60 giây. Không giới hạn tổng thời gian tải: file lớn vẫn được tải khi dữ liệu tiếp tục về, đến khi hoàn tất hoặc người dùng dừng. Sau ba lỗi mạng liên tiếp, app giữ phần tải dở và báo để Tiếp tục. Nguồn không có validator dùng lượt tải đầy đủ, không nối partial cũ. Partial cũ không đủ dữ liệu xác minh được tải lại từ đầu. Có file hoàn chỉnh thì probe/hash trước bước Google. Thư mục liên kết/junction bị từ chối khi tải hoặc xóa; lựa chọn xóa phải khớp checkpoint/kênh/bảng/thư mục sở hữu. Kiểm tra này giảm rủi ro, không bảo đảm chống mọi cuộc đua sửa file của tiến trình cùng tài khoản.

## Quy trình đóng gói và kiểm tra

Máy phát triển chạy `scripts/Build-Native.ps1` vào một thư mục đầu ra mới. Build không có cách nhúng Google OAuth; gate `--check-bundle` từ chối gói có manifest resource tên Google/OAuth.

Build khóa dependency theo packages.lock.json, bỏ file PDB và metadata origin trên máy phát triển, chỉ thêm tài liệu/công cụ đã chỉ định. Gate kiểm tra tên file nhạy cảm, tài liệu/binary của app có đường dẫn/tài khoản cá nhân, hash công cụ, OAuth resource rỗng và manifest toàn gói. Gate là kiểm tra theo chính sách cụ thể, không phải máy dò mọi loại secret hoặc chứng nhận an toàn.

Trước phát hành rộng rãi: kiểm tra trên Windows sạch và tài khoản khác; rà soát các dependency/lỗ hổng mới; hoàn thiện notices và nguồn tương ứng của ffprobe/dependency; kiểm tra Google thật, cài/nâng cấp/gỡ app và luồng tải sau gián đoạn. Bản portable thử nghiệm chưa được nghiệm thu máy sạch hoặc kiểm toán độc lập. Phần giấy phép công cụ cần hoàn thiện trước phát hành công khai.

Người dùng tự quản lý project Google và các yêu cầu Test users/xác minh OAuth/upload audit của project mình. Không cần chứng thư ký số hoặc project Google chung để bắt đầu sử dụng bản này.

## Hạn chế dùng tài khoản Douyin

HTTP tra cứu chạy lần lượt, cách ít nhất 2 giây; cache metadata đúng ID trong 2 phút. Xác minh được người dùng thực hiện tại cửa sổ khách trước; không tự dùng tài khoản để thay phiên đang gặp CAPTCHA. Khi khách không đủ dữ liệu video bắt buộc sau lượt đọc giới hạn, app tự dùng phiên tài khoản và thông báo, không yêu cầu duyệt. Với kênh nguồn, tài khoản vẫn là phương án cuối cho dữ liệu bắt buộc. CAPTCHA đang hiển thị được giữ để người dùng xử lý; không đổi phiên để tránh CAPTCHA. HTTP 429 dừng truy cập, không đổi danh tính để né hạn chế. CDN 401/403/404/410 chỉ làm mới link cùng bản video một lần; giữ partial và kiểm tra validator/dung lượng trước khi nối. Link nguồn được lưu mã hóa DPAPI theo công việc/lượt tải/thư mục, không kèm cookie hoặc page state; xóa kết quả tải cũng xóa metadata này. Các khoảng chờ là chính sách của app, không phải bảo đảm an toàn từ Douyin. Truy cập khách vẫn có thể được nhận diện qua IP/thiết bị; không thể cam kết không bị hạn chế hoặc khóa tài khoản.

Thư mục mất ở đường dẫn cũ không tự được coi là đã xóa. App tự tìm có giới hạn trước; người dùng chọn vị trí khi chưa tìm được. Chỉ nhận thư mục có video khớp dung lượng/SHA-256, bằng chứng sở hữu cũ và các file thuộc kết quả công việc. Không quét toàn ổ hoặc nhận thư mục có file lạ/junction. Xác nhận thư mục đã xóa thủ công chỉ cập nhật local; video YouTube không còn sau GET đúng kênh thành công tự cập nhật theo chính sách 0.3.35 bên dưới.

### Thay đổi 0.3.35

Theo lựa chọn người dùng, GET YouTube thành công trên đúng kênh nhưng không còn ID được coi là đã xóa thủ công và tự cập nhật local. Lỗi HTTP/OAuth/mạng/dữ liệu không hợp lệ không coi là xóa. Cơ chế này thay hướng xác nhận trong 0.3.21; không xin quyền xóa cho video đã mất.

Tìm thư mục dùng mã Windows (kiểm tra volume, ID, ngày tạo để hạn chế tái sử dụng ID), chỉ mục Windows hoặc duyệt đích đã dùng có giới hạn; chạy nền và hủy được, không cần admin/dịch vụ nền. Xác minh hash trước nhận quyền quản lý; không nhận junction/file lạ/thùng rác/nhiều bản giống nhau. Cache/ownership marker cập nhật theo đường dẫn đã đối chiếu. Mã định danh chỉ lưu trong payload công việc mã hóa, không xuất vào gói cài.

0.3.35 bổ sung đọc metadata đúng đường dẫn tên công việc ngay dưới gốc ổ cứng/USB sẵn sàng. Không duyệt toàn ổ, không dò ổ mạng; không nhận quyền quản lý nếu hash sai, có file lạ hay nhiều bản khớp.

