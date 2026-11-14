# Tìm video hay 0.3.46 — app Windows

Bản 0.3.44 cải thiện tải video theo nguyên tắc chia đoạn và tái sử dụng kết nối: tối đa bốn luồng nhận phần mới ngay khi rảnh, ghi dữ liệu trong lúc tải và giữ từng vùng byte khi tạm dừng/mất mạng. Khi mở lại, app chỉ tải phần còn thiếu theo checkpoint đã xác minh. Tốc độ thực tế phụ thuộc mạng và CDN.

Bản 0.3.43 nhận thêm bìa nguồn khi Douyin chỉ trả `cover` và `dynamic_cover` cùng tài nguyên, khác `origin_cover`; vẫn kiểm tra ảnh tĩnh và ưu tiên bìa được chỉ định rõ. **Bổ sung → Tải lại ảnh bìa** lấy metadata mới thay vì dùng lại danh sách bìa rỗng/link cũ, không tải lại video.

Bản 0.3.42 tự khôi phục đọc nguồn khi HTTP Douyin thiếu UIFID, trả rỗng hoặc trả video đề xuất khác ID. App thử nguồn công khai, rồi WebView khách; khách vẫn thiếu dữ liệu bắt buộc thì tự dùng phiên Douyin đã đăng nhập và thông báo, không hỏi duyệt chuyển phiên. Luôn kiểm tra đúng ID, giữ checkpoint, không đổi phiên để bỏ qua HTTP 429, CAPTCHA đang chờ hoặc thao tác đóng/dừng của bạn. Thiếu ảnh bìa tùy chọn không kích hoạt phiên tài khoản.

Bộ tải dùng tối đa bốn kết nối khi CDN xác nhận dải byte và ETag mạnh. Mỗi yêu cầu tối đa 4 MiB, mỗi luồng dùng buffer 128 KiB và ghi ngay vào vị trí tương ứng trong file. Luồng rảnh nhận phần mới; kết nối hoạt động tốt được tái sử dụng. Lỗi mạng giảm còn hai rồi một kết nối; CDN bỏ Range thì giữ prefix liên tục đã tải và chuyển về tuần tự, kiểm tra lại phiên bản nguồn trước khi nối. Kết nối tải có dữ liệu nhưng chậm dưới 384 KiB/giây trong ít nhất 10 giây được mở lại tối đa hai lần mỗi lượt, giữ byte đã ghi; file lớn không giới hạn tổng thời gian tải. Chất lượng đã chọn và thời gian chờ HTTP 429 được giữ.

Bản 0.3.40 thêm cột **Link video** cạnh **Tên video** trong lịch sử công việc. Bấm vào tên hoặc link để sao chép toàn bộ nội dung; rê chuột để xem đầy đủ khi ô bị rút gọn. Link dùng địa chỉ nguồn đã xác định, hoặc link đã nhập khi chưa xác định được nguồn.

Từ bản 0.3.39, app tách xóa kết quả khỏi hàng chờ video. Xóa thư mục hoặc công việc trống bắt đầu ngay sau xác nhận; chỉ chờ chính công việc đó dừng và lưu checkpoint. Xóa Google, ghi bảng riêng và xác nhận YouTube chạy nền độc lập với tải/đăng. Khi xóa nhiều phần, file được xử lý trước; lỗi Google giữ phần chưa xóa và lịch sử, không khôi phục phần đã xóa. Hộp thoại Xóa/Bổ sung mở từ dữ liệu đã lưu; dùng **Kiểm tra lại kết quả Google** khi cần cập nhật kết quả đã sửa/xóa bên ngoài app. Các nút mở kết quả, lọc/làm mới lịch sử và nhập video mới không bị khóa bởi một lượt đọc cài đặt chậm.

Bản 0.3.38 giữ ngách chủ đề đã chọn sau khi thêm công việc. Video tiếp theo dùng cùng ngách và có thể bắt đầu ngay khi nhập đủ link/tên; không cần đổi ngách rồi chọn lại.

Bản 0.3.37 sửa yêu cầu kết nối lại Google lặp lại: sau khi xác minh đúng bảng/kênh, quyền mới thay quyền cũ ngay tại khóa dùng chung của cấu hình và các công việc. Video tiếp theo và lần mở app sau dùng quyền đã khôi phục. App tự làm mới khi một lượt đọc gặp HTTP 401; lỗi máy chủ hoặc giới hạn tần suất khi làm mới không bị báo thành mất quyền. Thao tác ghi/đăng không tự gửi lại khi chưa rõ kết quả.

Bản 0.3.35 thêm cửa sổ Cần bạn lựa chọn: hiện rõ yêu cầu, giữ checkpoint, tiếp tục sau lựa chọn và để các công việc khác chạy. Lỗi mạng/429 có lịch thử lại; yêu cầu cùng kết nối Google được gom lại. Xem mục Thông báo và công việc chờ bên dưới.

Bản 0.3.35 cố định chiều cao nội dung Chi tiết tiến trình và kết quả ở 170 px khi mở, kể cả nội dung ngắn hoặc chưa chọn công việc; nội dung dài cuộn trong khung; chỉ hiện thanh cuộn khi nội dung vượt chiều cao này. Thu gọn vẫn do người dùng chủ động.

Bản 0.3.35 khôi phục lấy metadata/bìa video qua HTTP trước: phiên khách, web detail + web Select và feed dự phòng; làm mới khách tối đa một lần/ngày khi web trả rỗng, không làm mới liên tục vì UIFID. Từ 0.3.42, WebView khách và tài khoản cũng tự tham gia khi nguồn trước chưa đủ dữ liệu video bắt buộc. Thanh cuộn riêng của chi tiết chỉ hiện khi nội dung vượt khung, rộng 20 px; bánh xe và kéo thanh cuộn hoạt động độc lập.

Bản này giữ sửa tải lại sau khi xóa file/thư mục thủ công để dùng metadata của đúng lượt tải trước; chỉ tra cứu lại khi dữ liệu thiếu hoặc link hết hạn. Bộ lọc và chi tiết mặc định thu gọn; chi tiết có thanh cuộn dọc riêng chỉ hiện khi nội dung vượt khung. Cột Công việc để trống khi không còn phần nào, có bộ lọc **Trống**. App giữ tiến độ truyền video khi tạm dừng/mở lại; đặt nút mở kết quả phía trên chi tiết và cho toàn bộ phần chi tiết cuộn riêng. Biểu mẫu, lịch sử và khung kết quả cùng cuộn theo trang; các mục mở/thu gọn độc lập. Cài đặt chia nhóm, hộp thoại có nút xác nhận cố định, hướng dẫn có tìm kiếm. Lịch sử cập nhật theo phần công việc hiện còn sau bổ sung/xóa; bỏ cột Short/Long, thêm tìm kiếm, bộ lọc trạng thái, số thứ tự hàng chờ và sắp xếp theo tiến trình. App tự tìm lại thư mục đã chuyển bằng mã Windows hoặc tìm kiếm có giới hạn, đối chiếu đúng file trước khi liên kết. Video YouTube không còn trên kênh gốc sau lượt đọc thành công được tự cập nhật là đã xóa thủ công; không hỏi xác nhận.

Bản 0.3.35 sửa dấu tích xóa Sheets bị tự bỏ khi làm mới cấu hình cột. Có kiểm tra kết quả bên ngoài app trong Xóa/Bổ sung, hỗ trợ liên kết thư mục đã chuyển sau khi đối chiếu file. App ưu tiên dữ liệu Douyin công khai/khách. Trình duyệt chạy ẩn, tự đóng lời mời đăng nhập có thể bỏ qua; chỉ hiện khi bạn cần đăng nhập hoặc xác minh, rồi tự ẩn để chạy tiếp. Với dữ liệu video, app lấy qua HTTP trước. Từ 0.3.42, HTTP chưa đủ dữ liệu đúng video sẽ tự thử WebView khách rồi tài khoản nếu cần, không hỏi duyệt; video đề xuất sai ID luôn bị từ chối. Đọc kênh nguồn khi điền bảng vẫn dùng bộ đọc kênh riêng. Thiếu ảnh bìa không kích hoạt tài khoản. Tải tiếp dùng metadata đã lưu mã hóa của công việc, chỉ lấy lại link khi cần; HTTP 429 vẫn tạm dừng và giữ checkpoint. Mốc Short/Long mặc định 5 phút, có thể chỉnh.

Một mục **Xử lý video**, một lịch sử và một thư mục mặc định. Chọn các phần muốn thực hiện: **Tải video**, **Điền Google Sheets**, **Đăng YouTube**. Chọn Đăng tự tích và khóa Tải; bỏ Đăng giữ Tải tích. Không có mục tải riêng hoặc chọn video từ máy.

Tên video tối đa **30 ký tự, gồm dấu cách**, không tách dấu tiếng Việt/emoji. Tên cũng là tiêu đề YouTube mặc định; mô tả trống, luôn không dành cho trẻ em. Tiêu đề/mô tả riêng và chế độ hiển thị nằm trong Tùy chọn đăng YouTube. Mặc định mới là Không công khai; chế độ đã lưu và công việc cũ được giữ. Project API chưa xác minh không bị giới hạn chỉ đăng Riêng tư theo tài liệu videos.insert hiện tại.

**Chỉ điền bảng** lấy `video.duration` của đúng video Douyin. Không tải video/bìa, không tạo thư mục, không dùng file local/ffprobe. Mặc định ≤300000 ms (5 phút) là Short, lớn hơn là Long; có thể đổi mốc trong Kết nối & cài đặt; thiếu thời lượng thì báo để thử lại, không đoán. Mọi lượt ghi mới dùng thời lượng nguồn, kể cả đã tải file.

## Mở và cài đặt

Chạy **Mo-ung-dung.cmd**, **Start.ps1** hoặc **artifacts/windows-x64-0.3.46/TimVideoHay.exe**. Bản tự chứa .NET/công cụ; không cần Node, Python hoặc .NET SDK. Giữ nguyên cả thư mục khi chuyển máy.

Gói cài có **Cai-dat.cmd** / **Go-cai-dat.cmd**. Cài vào `%LocalAppData%\Programs\TimVideoHay\0.3.46`, tạo shortcut Start Menu/desktop. Gỡ mặc định giữ dữ liệu. Bản dùng script, chưa có Setup.exe/MSI hoặc mục Installed apps. Xem [INSTALLATION.md](INSTALLATION.md).

## Sử dụng

Bấm **Hướng dẫn sử dụng** trong app để đọc tài liệu offline. Có năm tổ hợp:

| Tải | Bảng | YouTube | Công việc |

| --- | --- | --- | --- |

| Có | Không | Không | Tải video và ảnh bìa |

| Không | Có | Không | Lấy thời lượng, ghi bảng |

| Có | Có | Không | Tải và ghi bảng |

| Có | Không | Có | Tải và đăng |

| Có | Có | Có | Tải, ghi bảng, đăng |

1. Tích công việc muốn làm, nhập link Douyin và tên. Ít nhất một phần phải được chọn.

2. Tại Kết nối & cài đặt, chọn file Sheets và hai tab riêng: **Tab video** và **Tab kênh nguồn**. Danh sách tab tự tải khi mở app; bấm Làm mới tab sau khi thay đổi Sheets. Bấm **Chọn cột video** và **Chọn cột kênh nguồn**, chọn hàng tiêu đề và cột thực tế cho từng thông tin rồi lưu. Có thể dùng tiêu đề tùy ý; app ghi theo vị trí đã chọn. Khi chèn, xóa hoặc đổi thứ tự cột, chọn lại trước công việc mới. Công việc cũ giữ lựa chọn cột đã lưu. Khi chọn Bảng, chọn ngách từ dropdown được đọc từ cột Ngách. Có thể bấm Làm mới ngách sau khi sửa Sheets. App không tự thêm ngách hoặc đổi cấu trúc bảng.

3. Khi chọn YouTube, kết nối kênh và chọn chế độ hiển thị nếu cần. Khi chỉ tải, không cần Google hoặc ngách. Khi chỉ Bảng, không cần kênh/thư mục tải; khi Tải + Đăng, không cần bảng. Kết nối Google dùng dịch vụ đang chọn; nút đổi bảng/kênh riêng luôn có trong cài đặt.

4. Bấm **Bắt đầu xử lý**. App kiểm tra ID trong lịch sử trước xếp hàng. Khi chạy, theo phần chọn, kiểm tra video trong Sheets, thư mục trùng tên và tiêu đề hiện có trên YouTube trước tải/ghi/đăng; trùng thì dừng và thông báo. Tiêu đề so theo Unicode chuẩn hóa, bỏ khoảng trắng đầu/cuối, không phân biệt hoa/thường. Dùng Bổ sung/Tiếp tục trên công việc cũ để giữ kết quả; không phát hiện nội dung giống nhau ở ID/tiêu đề khác.

5. Tải/đăng video chạy lần lượt. Công việc chỉ ghi bảng, xóa Google và lượt xác nhận YouTube dùng tối đa bốn lượt xử lý nền; các lượt ghi/xóa cùng file Sheets được thực hiện lần lượt. Nhiều yêu cầu xóa Google cùng lúc vẫn cho xử lý file cục bộ trước khi đợi lượt Google. Xóa trên máy không xếp sau video khác. Các lựa chọn và đích đã lưu không bị đổi khi bạn sửa biểu mẫu/cài đặt cho video tiếp theo.

[GOOGLE-SETUP.md](GOOGLE-SETUP.md) hướng dẫn OAuth, Test users, đổi email/bảng/kênh, chuyển máy/tài khoản Windows và tạo project/Desktop client mới. Bảng và kênh có thể thuộc hai tài khoản khác nhau. Không cần nhập link bảng/kênh. Bản cài không nhúng cấu hình Google; nhập Desktop JSON một lần ở Thiết lập nâng cao.

**Nguồn Douyin:** video đọc HTTP công khai trước, tự thử WebView khách khi thiếu dữ liệu đúng video và tự dùng tài khoản nếu khách vẫn thiếu dữ liệu bắt buộc. Đọc kênh nguồn khi điền bảng vẫn dùng WebView khách rồi tài khoản nếu cần. Cửa sổ chạy ẩn; CAPTCHA hoặc đăng nhập bắt buộc mới hiện để bạn làm, xong tự ẩn. Không hỏi duyệt chuyển phiên. HTTP 429 thì tạm nghỉ. Xem [DOUYIN-SESSION.md](DOUYIN-SESSION.md). File lớn không giới hạn tổng thời gian tải; không nhận thêm dữ liệu trong 60 giây thì thử lại; đường tải chậm có thể tự kết nối lại có giới hạn. Bấm Tiếp tục dùng link nguồn đã lưu và partial có validator; link bị từ chối/hết hạn mới lấy lại một lần, kiểm tra đúng bản video trước khi nối.

### Bổ sung / thực hiện lại

Nút **Bổ sung** nằm trên từng dòng lịch sử, bao gồm **Tải lại ảnh bìa** khi đã có video/thư mục. Chỉ hiện phần chưa từng chọn, đã xóa xác nhận hoặc file đã mất. Phần đang dở dùng Tiếp tục; phần còn kết quả không được chạy lại.

Hộp thoại ghi rõ phần còn dở sẽ tiếp tục cùng phần mới, kết quả giữ nguyên và đích mỗi phần. Phần mới dùng cài đặt hiện tại. Làm lại phần đã xóa ưu tiên đích cũ; có thể chọn dùng đích hiện tại. Ghi bảng yêu cầu chọn ngách của đúng bảng đó. Đăng dùng file của công việc còn hợp lệ, hoặc tự tải lại nếu file đã xóa/mất. Không tự ghi bảng hoặc đăng lại phần còn kết quả.

### Xóa

Mỗi dòng lịch sử có **một nút Xóa**. App dừng riêng công việc, đọc checkpoint mới và hiện những kết quả đã lưu: thư mục (kể cả tải dở), dòng đã xác minh, video có ID. Hộp thoại không đợi mạng. Nếu đã sửa/xóa kết quả Google bên ngoài app, bấm **Kiểm tra lại kết quả Google** để cập nhật; nút này cũng có trong Bổ sung. Chưa thực hiện/đã xóa thì ẩn lựa chọn; kết quả chưa rõ cần Tiếp tục hoặc Đối soát trước.

- Mặc định chưa chọn xóa. Xóa phần nào chỉ xóa phần đó; không xóa dây chuyền.

- Chọn hết kết quả hiện có mới xuất hiện **Xóa công việc khỏi lịch sử**; nếu không còn kết quả, mục này xuất hiện ngay. Tích nó bảo đảm chọn hết kết quả, bỏ một kết quả sẽ bỏ tích và ẩn xóa lịch sử.

- Không tích xóa lịch sử thì giữ công việc, kể cả đã xóa hết kết quả; có thể thực hiện lại phần đã xóa.

- Xóa thư mục/công việc trống bắt đầu ngay; xóa Google chạy nền riêng, không phải đợi video khác tải/đăng. Xóa nhiều phần xử lý file trước; lỗi một phần vẫn cho xử lý phần khác. Lỗi xóa giữ công việc và ý định. Tiếp tục chỉ xóa phần còn thiếu; không tự khôi phục phần đã xóa. Phần công việc còn dở giữ checkpoint và dừng, không âm thầm chạy tiếp sau xóa.

- Xóa thư mục gồm các tệp bên trong; app kiểm tra sở hữu/đường dẫn/junction. Thiếu quyền xóa YouTube được báo riêng để kết nối lại đúng kênh gốc; không làm chậm phần xóa trên máy đã chọn.

Hộp thoại cập nhật lựa chọn theo checkpoint và core kiểm tra lại khi xác nhận. Xóa lịch sử giữ biên nhận riêng để phát hiện video từng xử lý. Record ẩn đã xóa hết kết quả chỉ cho tạo thay thế sau xác nhận rõ ràng; còn kết quả/chưa rõ thì bị chặn.

### Theo dõi, dừng và đóng app

- Lọc lịch sử theo thao tác/trạng thái; chọn dòng xem tiến độ, từng kết quả, chất lượng/bìa và đường dẫn. Mở file/thư mục/video khi kết quả còn có.

- Nút Tạm dừng/Tiếp tục trên từng dòng đổi theo trạng thái và chỉ dừng công việc đó; Tạm dừng tất cả dừng hàng đợi. Tiếp tục / thử lại dùng checkpoint, không tạo lượt mới.

- Bấm × khi có hoạt động sẽ ẩn xuống khay hệ thống và tiếp tục chạy. Nhấp đúp biểu tượng để mở lại; chuột phải → Thoát và lưu checkpoint để thoát hẳn. Không hoạt động thì × thoát.

- Mở lại: công việc dở thành đã dừng; lượt xác nhận YouTube có ID có thể tự đọc lại. Không yêu cầu file local/Sheets để xác nhận ID YouTube và không chờ xử lý toàn bộ độ phân giải.

- Đổi thư mục/bảng/kênh chỉ áp dụng phần công việc mới được xác nhận; không di chuyển file hoặc đổi đích đã có kết quả. Tải lại bìa nằm trong Bổ sung. Cấp lại quyền Google chỉ hiện khi lỗi quyền; Đối soát chỉ hiện khi lần ghi/upload chưa rõ kết quả. Hai nút nằm cùng hàng Mở file, Mở thư mục, Mở bảng, Mở video YouTube. Mở bảng dùng đích đã lưu của công việc.

## Quy tắc và dữ liệu

## Quy tắc

- Tên dùng làm tiêu đề mặc định, title tối đa 100 ký tự Unicode. Có sửa title/mô tả/chế độ hiển thị trong phần nâng cao. Mô tả mặc định trống.

- Luôn không dành cho trẻ em; không có selector đối tượng người xem.

- Tab video cần bốn loại thông tin: **Link video mẫu, Tên video, Ngách, Loại video**; tên tiêu đề thực tế do bạn đặt và gán trong Chọn cột. Tab nguồn cần năm loại thông tin bên dưới, cũng được gán riêng. Mốc mặc định ≤300 giây Short, >300 Long; chỉnh tại **Kết nối & cài đặt → Phân loại nội dung**. Cùng mốc áp dụng cả video và kênh; công việc đã tạo giữ mốc riêng; không ép YouTube Shorts. Giữ nhãn Video ngắn/Video dài hoặc Shorts khi bảng/dropdown dùng chúng. Trong cùng file cần tab **Kênh nguồn**, cột **Link kênh, Tên kênh, Đăng ký, Ngách, Dạng nội dung**: đọc 粉丝 chính xác, dịch tên ngoài Latinh sang tiếng Anh qua Google Translate miễn phí, dùng ngách đã chọn, phân loại tối đa 10 video mới nhất thành Long/Short/All. Kênh đã có được cập nhật cùng dòng và thông báo rõ. Xóa dòng video giữ dòng kênh. Xem [SOURCE-CHANNEL-PLAN.md](SOURCE-CHANNEL-PLAN.md).

- Ghi literal string; không thêm cột/link YouTube/trạng thái, không sửa dropdown. Ngách mới cần được dropdown chấp nhận. Không ghi vào công thức/ô gộp/vùng bảo vệ.

- Lấy danh sách chất lượng của trình phát web và thử bản nguồn `ratio=default`; kiểm tra độ phân giải/âm thanh thực trước khi chọn. Ưu tiên độ phân giải cao, bản không watermark, FPS, bản nguồn và bitrate/dung lượng. Không ghép/chuyển mã hoặc dùng AI.

- Tự tải bìa dọc và bìa ngang riêng của video; dùng thêm feed Douyin Select khi API web bị chặn hoặc thiếu bìa. Kiểm tra đúng ID video rồi chỉ bổ sung ảnh; không thay danh sách chất lượng video bằng nguồn Select. Chỉ cần link video, không nhập link ảnh. Giữ nguyên pixel/tỷ lệ/định dạng nguồn trả về; không cắt hoặc kéo giãn. Không dùng khung hình `cover/origin_cover` thay bìa đã chọn. Nếu chưa lấy được một chiều, app báo rõ. Ảnh lưu local, không tự đặt thumbnail YouTube; không tải phụ đề. Công việc đã hoàn tất mà thiếu ảnh: chọn công việc → Bổ sung → Tải lại ảnh bìa; không tải lại video hoặc đăng lại YouTube.

- Mỗi video nằm trong thư mục mang tên bạn nhập, video và hai bìa dùng tên sạch, không nối ID. Thư mục cùng tên đã tồn tại, kể cả rỗng, sẽ dừng và báo; chỉ tiếp tục thư mục đã xác minh thuộc chính công việc đó. Không ghi đè thư mục của công việc khác.

- Chọn công việc đã tải → **Bổ sung → Tải lại ảnh bìa** để cập nhật bìa mà không tải lại video, ghi lại bảng hay đăng lại YouTube. Bìa cũ do app quản lý được chuyển vào backup riêng; không ghi đè file khác của người dùng.

- Hiển thị độ phân giải/codec và đường dẫn trong chi tiết công việc; có nút Mở thư mục. Thiếu một/cả hai ảnh bìa vẫn tiếp tục ghi bảng và đăng video hợp lệ. Lượt lấy file bìa có ngân sách tổng 30 giây; giữ ảnh lấy được, báo chiều còn thiếu, không dùng khung hình thay thế. Có thể tải lại bìa sau; nút dừng vẫn hủy công việc bình thường.

- Không bảo đảm mọi video có 1080p/4K hoặc file nguyên bản tác giả. Metadata HTTP không lấy được thì thử bộ đọc Douyin trong app; nếu bị xác minh/giới hạn, báo để xử lý rồi Tiếp tục. Đã đọc thật đủ 10 video khi mở trực tiếp đúng kênh mẫu bằng phiên đăng nhập; nguồn Douyin vẫn có thể thay đổi hoặc chặn. Các file đã tải trước cập nhật giữ nguyên.

- Đổi đích/thư mục áp dụng cho công việc mới. Đổi bảng và đổi kênh độc lập, có thể khác chủ sở hữu Google. Công việc cũ giữ snapshot; kết nối lại phải khớp ID gốc. Sang máy khác giữ nguyên cả thư mục app rồi đăng nhập lại; không cần tạo lại Cloud project/client.

- Một bộ điều phối chạy lần lượt. Job cần xử lý thủ công không ngăn job tiếp theo.

- Thu nhỏ vẫn chạy. Đóng cửa sổ lúc đang chạy xuống tray và báo; chọn Thoát để dừng/lưu. Mở lại phục hồi thành đã dừng, không tự đăng lại.

## Dữ liệu

`%LocalAppData%\TimVideoHay`: SQLite jobs/settings và credential/session upload được mã hóa DPAPI; logs, temp, download-state, cover-backups, douyin-browser. Thư mục douyin-browser chứa dữ liệu phiên WebView2 riêng, quyền giới hạn người dùng hiện tại/System; không sao chép/chia sẻ. WebView2 tự quản lý phiên; app không xuất hoặc lưu bản cookie riêng. Video ở thư mục bạn chọn. Thoát trước backup database; khi còn chạy phải giữ WAL/SHM. Máy/tài khoản Windows khác cần kết nối Google/Douyin lại. Marker cũ được chuyển ra khỏi thư mục video khi xác minh đúng chủ công việc.

Session upload mã hóa trước media; SHA-256 kiểm tra file khi tiếp tục. Có ID YouTube thì chỉ xác minh. Tránh sửa/sắp xếp tab trong lúc app ghi: Sheets không khóa được mọi chỉnh sửa đồng thời. Tab vượt 20.000 dòng/200 cột cần dùng tab nhỏ hơn.

Hồ sơ dùng schema 8: nâng cấp theo transaction, giữ checkpoint/đích/kết quả. Lượt ghi bốn ô cũ đã bắt đầu được hoàn tất theo intent cũ, không tự thêm kênh. Không mở hồ sơ đã nâng cấp bằng bản cũ; chuyển lịch sử sang máy/tài khoản Windows khác chưa được hỗ trợ.

Lựa chọn cột và mốc được lưu cùng đích công việc. Bổ sung phần Bảng lần đầu dùng cài đặt lúc xác nhận; làm lại phần Bảng đã xóa giữ mốc/cột cũ khi chọn đích cũ, hoặc dùng cài đặt hiện tại khi chọn đích mới. Công việc cũ chưa lưu lựa chọn cột vẫn tự nhận diện theo tên tiêu đề cũ.

Node/web/extension cũ ở `legacy/browser-v022`, không thuộc build hoặc đường chạy mới. File dữ liệu/video/backup cũ được giữ; không đổi đăng nhập trình duyệt cũ thành quyền OAuth.

## Phát triển

`scripts/Prepare-NativeTools.ps1`: chuẩn bị ffprobe standalone đúng hash đã ghim. Bộ tải HTTP nội bộ không cần yt-dlp. `scripts/Build-Native.ps1`: build/test/publish Windows x64 tự chứa; máy phát triển cần .NET 10 SDK. `scripts/Test-Native.ps1`: một lệnh build + test; CI GitHub Actions (`.github/workflows/ci.yml`) chạy lệnh này cho mỗi PR và push vào `main`, bỏ qua khi chỉ sửa file `.md`.

Chỉ có một bản build, không nhúng cấu hình Google. Nhập tệp OAuth client loại Desktop app một lần ở **Thiết lập nâng cao**; app lưu mã hóa DPAPI trong hồ sơ và dùng tiếp khi cập nhật, nên không đổi project của quyền cũ. Giữ tệp JSON ngoài Git (có thể đặt ở `packaging/google-oauth.desktop.json`, đã gitignore).

`native/TimVideoHay.Core`: mô hình/quy tắc/điều phối. `Infrastructure`: SQLite/DPAPI/OAuth/Sheets/upload/downloader. `Desktop`: XAML/MVVM/tray. `Tests`: kiểm tra console, Google giả lập không thay nghiệm thu thật.

[Thiết kế](API-ARCHITECTURE.md) · [Đặc tả](SPEC.md) · [Kiểm tra và giới hạn](VALIDATION.md) · [Dependency](THIRD-PARTY-NOTICES.md).

## Bản quyền

© 2026 huriofly-work. Mã nguồn công khai để xem nhưng chưa cấp license: mọi quyền được bảo lưu, không được sao chép, sửa đổi hay phân phối lại khi chưa có sự đồng ý. Thư viện và công cụ bên thứ ba giữ giấy phép riêng, xem [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Chia sẻ và bảo mật

Bản cài nằm trong artifacts/windows-x64-0.3.46 hoặc ZIP trên GitHub Release (cùng một bản); không gửi thư mục dữ liệu. Trong thư mục portable có Cai-dat.cmd, Go-cai-dat.cmd, Install.ps1, Uninstall.ps1 và INSTALLATION.md; không cần mã nguồn. Xem [SECURITY.md](SECURITY.md) để biết cách cài, tự thiết lập Google, bảo vệ dữ liệu và giới hạn của bản chưa ký. Máy phát triển dùng scripts/Build-Native.ps1; build luôn kiểm tra không có cấu hình Google bị nhúng.

## Thay đổi ngoài app: di chuyển hoặc xóa thủ công

Khi mở **Xóa** hoặc **Bổ sung**, app kiểm tra lại các kết quả đã có bằng yêu cầu đọc Google; không gọi Google mỗi lần hộp thoại làm mới trạng thái local. Lỗi quyền/mạng không được coi là đã xóa.

- **Kéo dòng video trong Sheets:** app tìm theo ID/link nguồn và xóa ở vị trí hiện tại, giữ dòng Kênh nguồn. Dấu tích không tự bỏ vì đọc lại cấu hình cột hoặc đổi vị trí dòng. Nếu đổi bảng/cột/lượt ghi thật thì lựa chọn cũ bị bỏ để xác nhận lại. Dòng không còn trong tab video gốc thì app cập nhật kết quả bảng đã mất, không gửi lệnh xóa thêm; Bổ sung cho điền lại. Nội dung dòng đã sửa hoặc nguồn trùng nhiều dòng cần kiểm tra trước khi xóa.

- **Chuyển thư mục đã tải xong:** mở Xóa/Bổ sung → **Chọn vị trí thư mục đã chuyển**, chọn đúng thư mục video, giữ tên thư mục. App kiểm tra dung lượng/SHA-256 của video và các bìa có hash, rồi cập nhật đường dẫn file/bìa và quyền quản lý thư mục. Thư mục mới chứa file lạ, file thay đổi, thiếu video hoặc thuộc công việc khác sẽ bị từ chối. Không quét toàn ổ đĩa. Thư mục chỉ có file tải dở chưa có fingerprint hoàn chỉnh phải đưa về vị trí cũ để tiếp tục.

- **Xóa file video thủ công:** app báo thiếu file; thư mục/bìa còn lại vẫn được giữ. Dùng Bổ sung → Tải để tạo lại video, hoặc Xóa để xóa kết quả local còn lại.

- **Xóa ảnh bìa thủ công:** app báo thiếu bìa, giữ video; dùng Bổ sung → Tải lại ảnh bìa.

- **Xóa cả thư mục thủ công:** trong Xóa, tích **Xác nhận thư mục đã xóa thủ công**. Đây là cập nhật kết quả local; không tạo thư mục thay thế hoặc tìm/xóa thư mục ở vị trí chưa biết. Nếu thư mục xuất hiện lại, app yêu cầu chọn lại.

- **Xóa video YouTube thủ công:** app đọc đúng ID/kênh gốc thành công và không còn video thì tự cập nhật là đã xóa, không hỏi xác nhận hay gửi DELETE; Bổ sung cho đăng lại. Lỗi mạng, lỗi quyền hoặc phản hồi không hợp lệ vẫn giữ kết quả cũ.

Xóa lịch sử vẫn chỉ xuất hiện khi đã chọn xử lý hết các kết quả hiện có. Di chuyển/thiếu file không tự xóa lịch sử.

## Tự tìm thư mục đã chuyển (0.3.35)

App lưu mã định danh ổ đĩa/thư mục/file trong dữ liệu công việc mã hóa. Khi tải mới hoặc mở lại công việc cũ có thư mục còn tồn tại, app thu thập mã để có thể tìm lại về sau. Không tạo file phụ trong thư mục video, không cài dịch vụ hay extension.

Khi mở Xóa/Bổ sung, hoặc tiếp tục phần tải/đăng/xóa thư mục, nếu đường dẫn cũ không còn thì tìm ở nền: ưu tiên mã Windows → kiểm tra thư mục mang đúng tên công việc ngay dưới gốc các ổ cứng/USB đang sẵn sàng → Windows Search theo tên file → các đích lưu hiện tại/đã dùng còn trong lịch sử. Kiểm tra gốc ổ chỉ thử một đường dẫn cụ thể mỗi ổ, không duyệt các thư mục khác; hỗ trợ chuyển từ C: sang F:\ khi chưa được lập chỉ mục. Không quét toàn ổ: tối đa 16 đích, độ sâu 3, 2.000 thư mục, 3 giây duyệt; cả tìm/đối chiếu giới hạn 30 giây. Tạm dừng tất cả hủy thao tác đang kiểm tra; tạm dừng công việc hủy tìm của tiến trình đó. Thư mục cũ còn tồn tại thì không tìm/không hash lại.

Nếu đã chọn tải lại vì báo mất file, app vẫn đối chiếu bằng fingerprint đã lưu của lần tải trước, khi kết quả đó chưa bị xóa. Kết quả tự liên kết phải giữ tên thư mục/file của công việc, có video hoàn chỉnh đúng dung lượng/SHA-256, bìa có hash đúng và không có file lạ hoặc junction. Mã thư mục gốc xác định trực tiếp vị trí; nếu tìm bằng nội dung mà có nhiều bản khớp thì yêu cầu chọn. Thùng rác không được nhận lại làm thư mục làm việc. Tìm không thấy không có nghĩa đã xóa; ổ rời/mất quyền/index chưa cập nhật vẫn cần chọn vị trí hoặc xác nhận xóa. File tải dở chưa có fingerprint, thư mục đổi tên và vị trí không được lập chỉ mục/ngoài phạm vi vẫn có thể cần xử lý thủ công.

## Theo dõi và lọc lịch sử công việc (0.3.35)

Cột **Công việc** hiển thị các phần hiện còn hoặc được chọn để tiếp tục: Tải, Bảng, YouTube. Bổ sung một phần sẽ cập nhật cột; xóa phần nào thì phần đó không còn trong cột và bộ lọc. Khi xóa hết các phần đã chọn và giữ lịch sử, ô Công việc để trống. Bộ lọc Công việc có **Trống** để tìm những dòng này. Có thể tích Trống cùng các công việc khác để lấy cả nhóm trống và nhóm có đủ các việc đã tích. Di chuột lên ô Công việc để xem trạng thái kết quả hoặc các phần đang xóa. Cột Short/Long được bỏ khỏi lịch sử; quy tắc phân loại để ghi Sheets vẫn giữ nguyên.

Mặc định **Theo tiến trình**: đang chạy → hàng chờ → chờ xác nhận YouTube → cần xử lý (đăng nhập/kiểm tra/lỗi) → tạm dừng → hoàn tất. Có thể chọn ngày tạo mới nhất/cũ nhất để đổi cách xem. Bộ lọc dùng ô tích chọn: nhiều trạng thái lấy bất kỳ trạng thái đã tích; nhiều phần công việc luôn yêu cầu có đủ tất cả phần đã tích. Hai nhóm trạng thái/công việc dùng checklist xổ xuống cho gọn; mỗi nhóm có **Bỏ lọc** riêng, ô từ khóa có **Xóa** riêng. Không tích mục nào trong nhóm nghĩa là không lọc nhóm đó. Các nhóm và tìm tên/link/ngách kết hợp với nhau; **Bỏ lọc** khôi phục mặc định. Số lượng bên trên tính toàn bộ lịch sử còn hiển thị trong app, cùng số mục khớp bộ lọc.

Cột **Chờ** là số thứ tự trong toàn bộ hàng chờ; khi lọc có thể thấy số bị cách quãng. Thao tác xóa được ưu tiên; các lượt xác nhận YouTube đến hạn có thể xen vào. Trong mỗi nhóm xóa/xử lý, công việc vào hàng chờ trước chạy trước. Tiếp tục hoặc bổ sung đưa công việc vào cuối nhóm tương ứng; thời gian vào hàng chờ được lưu. Công việc cũ chưa lưu thời gian này dùng ngày tạo. Sắp xếp/lọc danh sách không thay đổi thứ tự chạy.

Danh sách có vùng cuộn riêng, dựng các hàng đang nhìn thấy; không tạo toàn bộ hàng giao diện cùng lúc. Dòng đang chọn được giữ khi sắp xếp hoặc cập nhật nếu vẫn khớp bộ lọc. Lịch sử chỉ đọc dữ liệu local, không polling Google/Douyin.

## Giao diện và thao tác (0.3.35)

- Bấm **Công việc mới** để mở hoặc thu gọn biểu mẫu. Điền link, tên, ngách và chọn phần muốn làm; nút Bắt đầu luôn nằm dưới biểu mẫu. Biểu mẫu, lịch sử, chi tiết và các nút mở kết quả cùng nằm trong vùng cuộn của trang; phần chi tiết không cố định ở đáy cửa sổ.

- Các mục **Công việc mới**, **Tìm kiếm và bộ lọc**, **Chi tiết tiến trình và kết quả** mở/thu gọn độc lập, hoàn toàn do người dùng bấm. Mở một mục, chọn dòng hoặc thêm công việc không tự thu gọn/ẩn mục khác. Thu gọn biểu mẫu giữ dữ liệu đang nhập.

- Mở bộ lọc để tìm tên/link/ngách, lọc trạng thái/công việc hoặc đổi thứ tự hiển thị. **Bỏ lọc** chỉ xóa nhóm tương ứng; **Đặt lại** xóa tất cả bộ lọc và khôi phục sắp xếp mặc định. Số chờ là vị trí trong toàn hàng chờ, không đổi thành số mới khi lọc.

- Chọn dòng để xem kết quả, mở file/thư mục/bảng/video. Mở **Chi tiết tiến trình và kết quả** để đọc thông báo, chất lượng, ảnh bìa và đường dẫn; phần chi tiết trình bày gọn: tên ở tiêu đề, bước xử lý và tiến độ có nhãn tải/đăng rõ ràng, chất lượng và ảnh bìa/kênh chia hai cột. Toàn bộ phần chi tiết có vùng cuộn riêng rộng hết khung, cao cố định 170 px; hàng nút mở kết quả ở phía trên phần chi tiết; đường dẫn file chỉ một dòng, rê chuột để xem đầy đủ. Có thể chủ động thu gọn/mở rộng; thanh tiêu đề và các nút mở kết quả vẫn còn khi thu gọn hoặc mở Công việc mới. Bộ lọc và chi tiết có thể mở cùng lúc với Công việc mới; app không tự thu gọn hoặc ẩn mục nào. **Bước xử lý** nằm trong phần chi tiết thay vì cột lịch sử. Tạm dừng/Tiếp tục, Bổ sung và Xóa ở ngay trên dòng.

- **Làm mới** và **Tạm dừng tất cả** nằm ở đầu mục **Lịch sử công việc**. Làm mới chỉ đọc lại lịch sử trên máy, giữ bộ lọc, thứ tự và dòng được chọn; không tra cứu file/thư mục, Google hoặc làm mới tab/ngách. Tạm dừng tất cả giữ chức năng dừng và lưu tiến độ các công việc. Các mục kết nối vẫn có nút làm mới tab/ngách riêng.
- **Kết nối & cài đặt** chia thành nhóm Google, Sheets, YouTube, phân loại nội dung, lưu file và thiết lập nâng cao. Tab video và tab kênh nguồn có lựa chọn cột riêng nằm cạnh nhau.

- Hộp thoại Xóa/Bổ sung/Chọn cột có thể thu phóng. Nội dung dài cuộn bên trong; Hủy và xác nhận luôn nằm ở cuối cửa sổ. Nút xác nhận xóa có màu đỏ.

- Trong cửa sổ hướng dẫn, bấm **Ctrl+F**, nhập nội dung rồi Enter hoặc **Tìm tiếp**.

### Đọc thanh tiến độ

- **Tải video**: số byte đã ghi trên máy / dung lượng video. **Đăng YouTube**: số byte YouTube đã xác nhận / dung lượng video; cập nhật theo từng khối được xác nhận, không phải dữ liệu mới gửi mà chưa có phản hồi.
- Khi tải song song, độ dài file `.part` có thể bao gồm khoảng trống chưa tải; thanh tiến độ chỉ tính tổng các vùng byte đã ghi. App lưu dữ liệu và bản đồ vùng byte định kỳ khoảng 5 giây, đồng thời lưu khi tạm dừng hoặc lỗi. Đóng đột ngột có thể phải tải lại phần sau checkpoint gần nhất.
- Khi tạm dừng, có lỗi, tiền kiểm lại hoặc mở lại app, phần trăm đã lưu được giữ. App có thể khôi phục tiến độ tải dở cũ từ receipt riêng và file `.part` hợp lệ, không mở Douyin để tính lại. Tải lại từ đầu hoặc đăng lại sau khi xóa bắt đầu một lượt tiến độ mới.
- Số phần trăm tính riêng cho bước truyền được ghi bên cạnh thanh; bước truyền đạt 100% có thể vẫn còn ảnh bìa, bảng hoặc xác nhận kết quả. Chỉ trạng thái **Hoàn tất** xác nhận toàn bộ phần công việc đã chọn xong. Không suy ra phần trăm cả công việc từ thời gian xử lý của các bước khác.

Khi mở app, **Tìm kiếm và bộ lọc** và **Chi tiết tiến trình và kết quả** mặc định thu gọn. Bạn mở từng mục khi cần; không tự thu gọn mục khác. Chi tiết có thanh cuộn dọc riêng; thanh không kéo được nếu nội dung đang đủ ngắn.


0.3.35 sửa đóng lời mời đăng nhập Douyin dạng SVG, chờ thao tác thay vì chuyển tài khoản quá sớm, và cuộn chuột/thanh cuộn riêng trong chi tiết. Xem DOUYIN-SESSION.md về giới hạn của nguồn công khai/khách.


## Thông báo và công việc chờ — 0.3.35

Khi cần quyết định, app hiện cửa sổ **Cần bạn lựa chọn** và đưa lên trước. Cửa sổ không giữ chế độ luôn nằm trên cùng; các công việc khác tiếp tục chạy. Nút **Cần bạn lựa chọn (n)** ở đầu app mở lại yêu cầu còn chờ, kể cả khi bộ lọc lịch sử đang che công việc đó. Cùng kết nối Google được gom thành một yêu cầu, đăng nhập lại đúng đích sẽ khôi phục nhóm công việc liên quan.

- **Để sau** hoặc đóng cửa sổ thông báo: giữ yêu cầu và tiến độ; không tạm dừng các công việc khác. Yêu cầu không tự hiện lặp lại khi chưa thay đổi.
- **Thử lại từ tiến độ đã lưu**: kiểm tra checkpoint trước khi tiếp tục; không tự gửi lại kết quả chưa rõ.
- **Đăng nhập lại và tiếp tục**: chọn đúng bảng/kênh gốc; app nối tiếp sau khi xác nhận đúng đích. Công việc đã chủ động tạm dừng không tự được nối lại cùng nhóm.
- **Kiểm tra kết quả đã gửi**: app đọc lại dòng Sheets hoặc truy vấn phiên upload trước. Nếu phiên upload mất/hết hạn và vẫn chưa rõ kết quả, kiểm tra Studio rồi nhập link/ID video đã có hoặc xác nhận chưa tạo video. App kiểm tra ID thuộc đúng kênh trước khi liên kết.
- **Chọn thư mục đã chuyển**: đối chiếu đúng file trước khi liên kết và tiếp tục.
- **Bỏ phần còn lại**: chỉ loại phần chưa hoàn tất, giữ kết quả đã có; có thể thực hiện lại bằng Bổ sung. Không bỏ phần đã gửi mà chưa xác định kết quả. Nếu bỏ tải khi đăng vẫn cần file thì bỏ luôn phần đăng chưa thực hiện.
- **Tạm dừng công việc/nhóm**: lưu tiến độ theo lựa chọn của bạn. Tạm dừng tất cả cũng tạm dừng các yêu cầu đang chờ.

Lỗi mạng được thử lại có khoảng nghỉ, tối đa hai lần tự động trước khi hỏi. HTTP 429 dùng thời điểm Retry-After hoặc khoảng nghỉ mặc định, không chuyển phiên để thử sớm. **Chờ tự thử lại** khác **Tạm dừng**; thời điểm thử lại được lưu qua lần thoát/mở app. Thiếu ảnh bìa khi video đã tải và kiểm tra đúng vẫn được bỏ qua để tiếp tục; trạng thái bìa ghi rõ trong chi tiết.

CAPTCHA/đăng nhập Douyin hiện đúng cửa sổ cần thao tác. App giữ phiên trong lúc bạn xác minh và tự đọc tiếp khi yêu cầu được giải quyết. Lỗi thao tác/cấu hình có cửa sổ thông báo riêng; quay lại sửa dữ liệu rồi thực hiện lại thao tác. Khởi động app/bản cài bị lỗi nghiêm trọng có thể chưa chạy được các chức năng này.

## Thông báo thống nhất — 0.3.36

Lỗi thao tác và yêu cầu của công việc cùng nằm trong **Cần bạn lựa chọn**. Hai lỗi khác nhau được giữ thành hai yêu cầu; lỗi giống nhau lặp lại tăng số lần, không tạo nhiều cửa sổ. Các lỗi thao tác còn chờ được lưu mã hóa trên máy; đóng cửa sổ/Để sau giữ yêu cầu qua lần mở app. Bấm **Đã xử lý, bỏ thông báo** chỉ bỏ yêu cầu đang chọn, không xóa kết quả hay công việc. Không bấm nút này nếu vẫn muốn giữ yêu cầu để xử lý sau. Lỗi lưu ổ đĩa chỉ có thể giữ trong phiên khi chưa ghi được dữ liệu.

Lỗi dữ liệu nhập hiện cạnh link/tên/tiêu đề/mô tả hoặc lựa chọn tab, có hướng dẫn khi nút Bắt đầu chưa khả dụng. Sai mốc thời lượng hiện màu đỏ. Lỗi trong trình kết nối Google hiện ngay trong trình hướng dẫn; sửa/chọn lại bằng các nút của bước hiện tại. Các lỗi thao tác có nút mở nơi cần sửa; đọc ngách lỗi mạng có Tải lại, thiếu quyền có Mở kết nối. Đọc ngách thành công tự bỏ yêu cầu ngách cũ.

Khi đang có hộp thoại xác nhận hoặc đang xác minh Douyin, yêu cầu mới đợi trong danh sách, không tự mở cửa sổ khác che lên. Hoàn tất thao tác hiện tại, app đưa yêu cầu mới lên trước một lần. Thông báo hoàn tất khi app ở nền gom các công việc gần nhau trong khoảng 3 giây qua khay hệ thống; bấm thông báo để mở app. Hoàn tất không giành tiêu điểm. Yêu cầu cần xử lý được ưu tiên hơn thông báo hoàn tất; lịch sử luôn giữ kết quả dù Windows không hiện thông báo khay.

Lỗi dữ liệu sai không có nút Thử lại; lỗi trùng yêu cầu bạn xử lý trùng trước khi kiểm tra lại. Công việc cũ vẫn giữ đích lúc tạo; đổi cài đặt mặc định không sửa đích của công việc cũ. Xác nhận video trùng và kiểm tra ghi bảng dùng nút nêu rõ hành động thay cho Có/Không chung chung.
