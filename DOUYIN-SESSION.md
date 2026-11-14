# Nguồn dữ liệu và phiên Douyin

## Thứ tự truy cập

App ưu tiên dữ liệu không cần đăng nhập: nguồn feed công khai, API web với cookie khách, rồi trang Douyin trong WebView khách. “Công khai” nói về dữ liệu; “khách” là phiên chưa đăng nhập. Nguồn công khai có thể không cần cookie hoặc dùng cookie khách, đều không dùng tài khoản của bạn.

Cookie khách ttwid được tạo tự động qua endpoint đăng ký khách của Douyin. App dùng lại bản còn hạn tối đa 24 giờ, kể cả bản guest-douyin từ phiên bản cũ. Chỉ đọc bản khách riêng; không lấy ttwid từ bản sao phiên đăng nhập. Cookie khách được mã hóa DPAPI CurrentUser, không cần bạn xuất/nhập. Lỗi đăng ký khách không khiến app đăng ký liên tục; nguồn feed vẫn có thể dùng được.

Bộ đọc khách dùng WebView2 InPrivate và hồ sơ riêng douyin-public-browser, không mở hồ sơ tài khoản. Khi có ttwid khách còn hạn, app đưa riêng cookie khách đó vào cửa sổ này. Dữ liệu xác minh do trang và WebView xử lý trong phiên đang mở. Không xuất cookie tài khoản hoặc giả lập chữ ký trình duyệt. Xác minh WebView không bảo đảm request HTTP sau đó cũng được chấp nhận; khi cần, app nhận dữ liệu trực tiếp từ trang đã xác minh.

## Xác minh khi chưa đăng nhập

Đọc thông tin video ưu tiên HTTP với phiên khách ttwid, web detail, web Select và feed. Nếu web trả rỗng, app thử làm mới khách một lần/ngày sau các nguồn dự phòng; không đăng ký phiên mới để chữa UIFID, 403 hay 429. Từ 0.3.42, nếu HTTP không có dữ liệu đúng video, app tự mở bộ đọc WebView khách. Khách chưa đủ dữ liệu bắt buộc sau lượt đọc giới hạn thì app tự dùng phiên đã đăng nhập và thông báo, không hỏi bạn duyệt. Máy chủ yêu cầu đăng nhập rõ ràng thì dùng phiên tài khoản trực tiếp. Video đề xuất sai ID không được nhận thay video nguồn.

Cửa sổ đọc chạy ẩn, chỉ hiện khi cần bạn đăng nhập hoặc xác minh; hoàn tất thì tự ẩn và tiếp tục. App tự đóng lời mời đăng nhập có thể bỏ qua, có giới hạn. Không đăng nhập vào cửa sổ khách. CAPTCHA đang chờ được giữ cho bạn xử lý, không chuyển tài khoản để tránh CAPTCHA. Nếu bạn đóng hoặc dừng cửa sổ đọc, công việc dừng và giữ checkpoint; không tự đổi phiên để chạy tiếp. Bộ đọc kênh nguồn cũng dùng khách trước rồi tài khoản nếu thiếu dữ liệu bắt buộc. Thiếu ảnh bìa tùy chọn không kích hoạt đổi phiên.

Lỗi thiếu UIFID không đồng nghĩa cookie hết hạn hay tài khoản bị khóa. App không tự tạo UIFID/chữ ký hoặc đăng ký khách lặp lại. Việc chuyển bộ đọc là tự động; app không tự giải CAPTCHA hoặc dùng dịch vụ giải CAPTCHA.

## Giới hạn truy cập và tài khoản

HTTP 429 là giới hạn tần suất, khác CAPTCHA. App giữ công việc, dừng truy vấn tự động, không chuyển sang tài khoản để thử tiếp ngay. Tra cứu HTTP chờ ít nhất 15 phút hoặc lâu hơn theo Retry-After; đây là lựa chọn thận trọng của app, không phải mức thời gian an toàn được Douyin cam kết. Nếu bộ đọc bị giới hạn hoặc bạn đóng cửa sổ khi CAPTCHA còn hiển thị, app cũng tạm nghỉ các lượt tra cứu kế tiếp. Sau khi nghỉ, dùng **Tiếp tục** trên dòng công việc.

Yêu cầu HTTP metadata được chạy lần lượt, cách nhau ít nhất 2 giây; dữ liệu đúng video được tái sử dụng trong 2 phút. Cửa sổ khách thử số đường dẫn/lần cuộn giới hạn. Kiểm tra xem bạn đã xác minh chưa là đọc DOM cục bộ, không phải gọi API liên tục. Các giới hạn này giảm truy xuất do app tạo ra; tài nguyên mà trang Douyin tự tải vẫn do WebView quản lý.

Không có cam kết tránh mọi hạn chế hay khóa tài khoản. Cookie khách vẫn là mã nhận diện và truy cập khách vẫn dùng mạng/IP của bạn. Douyin có quy định về truy cập tự động; xem [thỏa thuận người dùng](https://www.douyin.com/draft/douyin_agreement/douyin_agreement_user.html?id=6773906068725565448).

## Đăng nhập Douyin trong app

1. Mở **Kết nối & cài đặt → Nguồn dữ liệu Douyin → Mở Douyin trong app / đăng nhập trước**.

2. Đăng nhập bằng giao diện Douyin. Nếu có CAPTCHA, tự hoàn tất tại đây.

3. Đóng cửa sổ khi xong. WebView2 giữ phiên để dùng khi thực sự cần; app chỉ xác nhận đăng nhập khi nhận phản hồi tài khoản hợp lệ.

4. Công việc dừng có thể chạy lại bằng **Tiếp tục** trên dòng đó.

Không cần xuất cookie, extension hoặc API trả phí. Đây là phiên riêng của app, khác Chrome/Edge thường dùng. Với video, app tự dùng phiên tài khoản khi nguồn khách chưa đủ dữ liệu bắt buộc hoặc máy chủ yêu cầu đăng nhập rõ ràng; app thông báo, không hỏi duyệt. Với kênh nguồn, phiên tài khoản vẫn là phương án cuối khi khách không đủ dữ liệu. Nếu phiên tài khoản cần đăng nhập/xác minh, cửa sổ tự hiện để bạn xử lý, không đóng vì hết thời gian chờ; hoàn tất thì tự ẩn.

## Video, ảnh bìa và kênh nguồn

Chỉ điền bảng đọc thời lượng đúng ID video, không tải media/bìa. Khi tải, app giữ các rendition chất lượng cao và kiểm tra chất lượng thực trước tải. Thiếu bìa ngang/dọc chỉ thử nguồn bìa công khai; không dùng tài khoản chỉ để tìm đủ bìa. Có thể bỏ qua bìa hoặc bổ sung sau.

Kênh nguồn đọc tác giả đúng video, 粉丝 và 10 video mới nhất bằng bộ đọc khách trước. Thiếu dữ liệu bắt buộc thì tự thử phiên tài khoản; không ghi số đoán vào bảng. Video báo **你要观看的视频不存在** sẽ dừng, không nhận video đề xuất thay thế và không chuyển tài khoản để tìm video khác.

Bộ tải CDN không gửi cookie tài khoản. File lớn không giới hạn tổng thời gian tải. Khi CDN trả đúng Range và ETag mạnh, file đủ lớn được tải bằng tối đa bốn kết nối với các phần 4 MiB; tối đa 16 MiB chờ trong RAM. Chỉ phần liên tục đã ghi vào file .part được tính vào checkpoint. Thiếu ETag mạnh hoặc CDN không hỗ trợ Range thì dùng luồng tuần tự (đoạn tối đa 32 MiB). Lỗi mạng giảm 4 → 2 → 1 kết nối; HTTP 429 vẫn tạm nghỉ theo máy chủ. Khi tổng tốc độ dưới 384 KiB/giây ít nhất 10 giây, đóng phản hồi và mở lại từ phần đã lưu, tối đa hai lần mỗi lượt tải; chỉ áp dụng khi có thể nối an toàn. Các request video đóng kết nối sau phản hồi để lượt tiếp theo có thể nhận tuyến CDN mới. Tải tiếp vẫn cần validator/dung lượng khớp; không đổi bản chất lượng hoặc chuyển mã.

**Tiếp tục tải:** app lưu link video/bìa và thông tin format dưới dạng mã hóa DPAPI, gắn với đúng công việc, nguồn, lượt tải và thư mục. Đóng/mở app vẫn dùng lại được. Bấm Tiếp tục sẽ thử link đã lưu và nối phần tải dở; không mở lại trang chỉ để lấy metadata. Link bị CDN từ chối bằng 401/403/404/410 thì tự lấy lại đúng video một lần, yêu cầu cùng format và kiểm tra validator/dung lượng để tránh nối nhầm. HTTP 429 vẫn dừng, giữ partial và không đổi phiên để thử ngay. Nguồn thiếu validator hoặc nội dung đã đổi có thể phải tải lại từ đầu.

Công việc từ bản cũ chưa có metadata lưu, cache hỏng hoặc link tải hết hạn thì cần đọc nguồn một lần. Khi xóa file/thư mục thủ công rồi bổ sung tải lại, app dùng metadata của lượt tải trước nếu receipt khớp đúng video; lượt mới vẫn tải từ byte 0, không dùng offset cũ. Sau đó thông tin được lưu cho lần tiếp theo. Metadata riêng không chứa cookie/page state, không xuất JSON vào thư mục video; xóa kết quả tải cũng xóa metadata này.

## Hồ sơ, đổi tài khoản và chuyển máy

Phiên tài khoản: `%LocalAppData%\TimVideoHay\douyin-browser`. Muốn đổi tài khoản, đăng xuất trên trang Douyin trong app rồi đăng nhập lại. App không xuất/sao chép/lưu snapshot cookie tài khoản riêng. Bản cookie tài khoản và tham chiếu cookie thủ công từ phiên bản cũ không được bộ đọc mới sử dụng; kết quả Google, lịch sử và checkpoint tải được giữ.

Không gửi hồ sơ, secrets hoặc database khi chia sẻ app. Máy khác đăng nhập lại và tự thiết lập Google. App cần Microsoft Edge WebView2 Evergreen Runtime; xem INSTALLATION.md.

Mo-ung-dung.cmd mở bản 0.3.35 trong artifacts/windows-x64-0.3.36. Nếu dùng --data-dir khác, đó là hồ sơ khác. Chẩn đoán logs/douyin-session-last.json chỉ có trạng thái/đường dẫn hồ sơ/phiên bản, không có cookie, token, tên/ID tài khoản hay ảnh màn hình. Không có phép thử tự động bằng tài khoản thật trong kiểm tra bản này.


### Tải lại (0.3.31)

Nguồn web bị xác minh vẫn cho thử feed công khai độc lập, không gửi cookie tài khoản sang feed. Thiếu thời lượng từ xa không kích hoạt tài khoản khi chỉ cần tải và đã có URL media hợp lệ; file tải vẫn được ffprobe kiểm tra. Khi ghi bảng, thời lượng nguồn vẫn bắt buộc. Phiên tài khoản vẫn là phương án cuối nếu công khai và khách không cung cấp đủ dữ liệu. Không bảo đảm mọi video đều tải được bằng khách. HTTP 429 vẫn dừng theo thời gian chờ, không đổi phiên để thử tiếp.


## Bản 0.3.31: đọc phiên khách và cuộn chi tiết

App nhận diện cả dấu × dạng SVG không có nhãn của lời mời đăng nhập Douyin. Lời mời có thể mở lại khi chuyển đường dẫn; app tự đóng có giới hạn (tối đa 6 lần trong lượt đọc), rồi giữ cửa sổ cho người dùng nếu vẫn bị che. Không coi lời mời chưa đóng là bằng chứng bắt buộc dùng tài khoản. CAPTCHA vẫn chờ người dùng và không tự chuyển phiên.

Cửa sổ đọc chạy ngoài màn hình, không lấy focus/không hiện trên thanh tác vụ; WebView vẫn hoạt động để trang tiếp tục tạo yêu cầu. Chỉ đưa cửa sổ về màn hình khi cần thao tác. Đóng app/công việc vẫn đóng bộ đọc.

Cuộn chuột trong Chi tiết tiến trình và kết quả cuộn phần này trước; ở mép hoặc nội dung vừa khung thì cuộn trang ngoài. Thanh cuộn riêng rộng 20 px, chỉ hiện khi nội dung dài hơn vùng hiển thị cố định 170 px; nội dung ngắn ẩn thanh cuộn và giữ nguyên chiều cao khung.

Nguồn công khai có thể trả 403 thiếu UIFID hoặc trả video đề xuất khác ID. App không nhận video đề xuất thay video nguồn. Phiên khách cũng có thể bị Douyin từ chối dù SDK đã gửi định danh; vẫn giữ tài khoản là phương án cuối cho dữ liệu bắt buộc. Không cam kết mọi link đều tải được bằng khách.

## Kiểm tra thực tế 11/10/2026 — 0.3.32

HTTP với phiên khách mới trên link 7685660682389310755 và link ô tô 7673782911702748458 vẫn bị web từ chối UIFID; hai feed trả video khác ID. App từ chối lấy nhầm. Chưa xác nhận tải đầy đủ hai video bằng HTTP sau thay đổi. Không đọc phiên tài khoản hoặc gọi Google trong phép thử này. Việc phương thức cũ từng hoạt động không bảo đảm endpoint Douyin hiện vẫn chấp nhận mọi link.
