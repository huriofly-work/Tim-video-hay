# Kế hoạch chọn, bổ sung và xóa từng phần công việc

Ngày: 09/10/2026. Cơ sở: mã nguồn và bản chạy 0.3.13. Trạng thái: triển khai trong 0.3.14; kết quả kiểm tra tại VALIDATION.md.

## Yêu cầu đã chốt

- Bỏ tab Chỉ tải video. Một tab Xử lý video có ba ô tích: Tải video, Điền Google Sheets, Đăng YouTube.
- Điền bảng lấy thời lượng từ Douyin, không phụ thuộc file trên máy hoặc tải video để lấy thời lượng.
- Tích Đăng YouTube tự tích và khóa Tải video. Ghi bảng vẫn độc lập với tải/đăng. Có năm tổ hợp hợp lệ.
- Lịch sử có Bổ sung / thực hiện lại cho phần chưa chọn và phần đã xóa kết quả; giữ nguyên kết quả còn tồn tại.
- Trước khi tạo công việc mới, nhận diện ID video Douyin và kiểm tra công việc trùng trong dữ liệu đã lưu; thông báo ngay, không đợi tải/ghi/đăng mới phát hiện.
- Chỉ một nút Xóa. Hộp thoại cho chọn xóa các kết quả và thêm Xóa công việc khỏi lịch sử; xóa khỏi lịch sử bắt buộc chọn xóa toàn bộ kết quả hiện có. Chọn xóa khỏi lịch sử tự tích các mục kết quả còn lại. Người dùng đã xác nhận việc thực hiện lại phần đã xóa.
- Giữ Windows app, bộ tải miễn phí, OAuth, chất lượng video/bìa hiện tại. Bỏ thiết kế chọn file local từ kế hoạch trước; không thêm AI, extension hoặc dịch vụ trả phí.

## 1. Thời lượng Douyin độc lập với file

Đã thử trực tiếp ngày 09/10/2026: GET feed aid=1128 trên api5-normal-c-hl.amemv.com và aweme.snssdk.com. Không gửi cookie tài khoản, gọi URL media, chạy ffprobe hoặc đọc file video. Hai nút trả đúng ID và cùng video.duration cho từng mẫu:

| Video | video.duration (ms) | Giây | Loại trong bảng |
| --- | --- | --- | --- |
| 7673782911702748458 | 224445 | 224,445 | Short |
| 7659702794928147739 | 254200 | 254,2 | Long |
| 7673535793037577472 | 2439215 | 2439,215 | Long |

Bằng chứng: test-output/workflow-options-20261009/douyin-duration.json. Sáu phản hồi HTTP 200, status_code=0, đúng aweme_id. Đây là kiểm tra ba mẫu, không bảo đảm mọi video hoặc mọi thời điểm.

- Tách ReadMetadataAsync/ReadDurationAsync khỏi DownloadAsync. Lưu SourceId, canonical URL, DurationMilliseconds, nguồn/thời điểm lấy dữ liệu.
- Thử web detail; nếu web bị chặn hoặc thời lượng không hợp lệ, thử feed trên tối đa hai nút hiện có. Kiểm tra đúng ID/status, giới hạn JSON/thời gian và truyền hủy người dùng. Không được buộc có format tải video mới nhận thời lượng hợp lệ.
- Đường chỉ ghi bảng chỉ đọc JSON: không probe URL media, tải bìa, tạo thư mục video, gọi ffprobe hoặc yêu cầu file local. Không suy đoán từ phụ đề, tiêu đề hay ảnh.
- video.duration có đơn vị mili giây. Chỉ nhận số hữu hạn dương hợp lệ; so trực tiếp với 300000 ms, không làm tròn sang phút trước khi phân loại.
- Mọi lượt ghi bảng dùng thời lượng Douyin, kể cả có file tải. Quy tắc bảng từ 0.3.15: ≤5 phút Short, >5 phút Long. Khi tiếp tục một lượt ghi, giữ metadata đã chốt cho lượt đó; lượt ghi mới có thể lấy metadata mới.
- Cả các nguồn đều thiếu thời lượng: báo lỗi lấy thông tin và cho thử lại; không ghi dòng sai/thiếu loại và không chuyển sang yêu cầu file trên máy.

## 2. Biểu mẫu và năm tổ hợp

Hai tab: Xử lý video và Kết nối & cài đặt. Đầu biểu mẫu có ba ô tích và Bắt đầu thực hiện; cần ít nhất một thao tác. Lần đầu mặc định cả ba, lần sau nhớ lựa chọn hợp lệ gần nhất.

- Tích Đăng → tự tích Tải, khóa Tải và giải thích Cần tải video để đăng YouTube. Bỏ Đăng giữ Tải tích; người dùng có thể bỏ Tải sau đó.
- Link Douyin và tên tối đa 30 ký tự gồm dấu cách luôn bắt buộc. Ngách/bảng/tab chỉ bắt buộc khi chọn Bảng; kênh/thiết lập đăng khi chọn Đăng; thư mục khi chọn Tải.
- Không Bảng thì không tải dropdown ngách/kiểm tra Sheets. Không Đăng thì không yêu cầu YouTube OAuth. Chỉ Bảng không cần thư mục tải. Wizard chỉ yêu cầu dịch vụ cần dùng; Sheets/YouTube vẫn kết nối riêng và có thể thuộc hai tài khoản.
- Một biểu mẫu, một thư mục mặc định, một lịch sử có bộ lọc. Không có trường chọn file trên máy; file đăng thuộc công việc tải Douyin.

| Tải | Bảng | YouTube | Thực hiện |
| --- | --- | --- | --- |
| Có | Không | Không | Tải video và hai bìa |
| Không | Có | Không | Lấy thời lượng Douyin và ghi bảng; không tải video/bìa |
| Có | Có | Không | Lấy thời lượng Douyin, tải video/bìa và ghi bảng |
| Có | Không | Có | Tải và đăng, không gọi Sheets |
| Có | Có | Có | Lấy thời lượng Douyin, tải → bảng → YouTube |

Các bước chuẩn bị chỉ dùng dịch vụ cần thiết; sau đó thực hiện Tải → Bảng → YouTube, bỏ qua việc không chọn. Thiếu bìa không chặn video hợp lệ như hiện tại. Lỗi một bước lưu checkpoint và dừng tại bước lỗi, không tự bỏ bước để đăng tiếp.

Tải bắt buộc với Đăng không có nghĩa tải lại mọi lần. File đã tải của chính công việc còn hoàn chỉnh/hợp lệ thì dùng lại. File đã xóa/mất/thay đổi thì tải lại hoặc báo xung đột; không đăng file không rõ nguồn.

## 3. Bổ sung / thực hiện lại trong lịch sử

Nút Bổ sung / thực hiện lại mở danh sách được tạo từ tiến trình và kết quả hiện tại. Chỉ hiện ô chọn cho phần chưa từng chọn hoặc đã xóa xác nhận; không luôn hiện đủ ba thao tác và không giữ phần không phù hợp dưới dạng checkbox bị khóa.

| Tình trạng | Hành vi |
| --- | --- |
| Chưa từng chọn | Cho bổ sung |
| Đã xóa kết quả và xác nhận xong | Cho thực hiện lại |
| Hoàn tất, kết quả còn tồn tại | Ẩn lựa chọn bổ sung của phần đó; giữ nguyên kết quả |
| Đã chọn nhưng chưa xong/lỗi/đã dừng | Không hiện như việc bổ sung mới; dùng Tiếp tục với checkpoint hiện có |
| Đang chạy/đang xóa | Không hiện thao tác tạo mới cho phần đó; không sửa lượt đang chạy |
| Kết quả ngoài app chưa rõ | Ẩn lựa chọn làm lại; thông báo cần xác minh, không cho gửi lại mù |

- Hộp thoại ghi rõ phần sẽ chạy, phần giữ nguyên và đích dùng cho từng phần. Nếu còn việc dở, phải hiển thị rõ phạm vi tiếp tục cùng phần bổ sung; không âm thầm chạy hoặc bỏ phần cũ.
- Bổ sung Đăng vẫn ép Tải. Tải đã xong thì không hiện lại checkbox Tải, chỉ ghi Dùng file đã tải và chuẩn hóa phụ thuộc ở core để dùng kết quả đó. Tải chưa chọn/đã xóa thì hiện và tự tích Tải/Tải lại trước Đăng; nếu Tải đang dở thì dùng checkpoint cũ, không tạo lượt tải mới.
- Tải xong → bổ sung Bảng: lấy thời lượng Douyin và ghi bảng, không tải lại. Bảng xong → bổ sung Đăng: tải nếu cần rồi đăng, không ghi bảng lần hai.
- YouTube đã xóa nhưng file còn → xác minh file rồi đăng lại. File và YouTube đã xóa → tải lại rồi đăng.
- Thao tác mới lấy đích từ thiết lập hiện tại, hiển thị trước xác nhận. Thao tác đã có kết quả giữ đích gốc. Thực hiện lại phần đã xóa ưu tiên đích cũ, cho chọn đích khác trước tạo lượt mới.
- Link/ID và tên công việc giữ nguyên. Thêm Bảng có thể yêu cầu chọn ngách từ đích bảng mới; không tự đổi dòng đã ghi trước đó.
- Không có phần cần thêm/chạy lại thì vô hiệu nút thực hiện trong hộp thoại.

## 4. Một nút Xóa và quan hệ đối nghịch

Mỗi công việc chỉ có một nút Xóa. Hộp thoại tạo danh sách lựa chọn theo những kết quả thực sự hiện có, tối đa gồm:

- Xóa thư mục video đã tải.
- Xóa dòng Google Sheets đã ghi.
- Xóa video YouTube đã đăng.
- Xóa công việc khỏi lịch sử.

Chưa có kết quả hoặc đã xóa xác nhận thì ẩn hoàn toàn lựa chọn xóa phần đó, không hiện checkbox vô hiệu. Kết quả chưa rõ cần xác minh thì chỉ hiện thông báo, chưa hiện lựa chọn xóa mù. Mặc định không tích. Không tích xóa khỏi lịch sử thì công việc vẫn nằm trong danh sách, kể cả đã xóa hết các kết quả.

Thư mục tải dở đã được tạo và thuộc công việc cũng là kết quả local có thể xóa; ghi rõ Xóa thư mục tải dở. Không cần chờ tải hoàn tất mới có lựa chọn này. Dòng Sheets/video YouTube chỉ hiện khi đã xác định được kết quả, không chỉ vì trước đó đã tích chọn thao tác.

### Quan hệ giữa các lựa chọn

- Xóa công việc khỏi lịch sử chỉ hợp lệ khi toàn bộ kết quả hiện có trong ba nhóm đều được chọn xóa hoặc đã xóa xác nhận trước đó.
- Chỉ hiện lựa chọn Xóa công việc khỏi lịch sử khi đã chọn xóa hết các kết quả đang có, hoặc công việc không còn kết quả cần xóa; ẩn nếu có kết quả chưa rõ cần xác minh. Bỏ chọn một kết quả làm lựa chọn này tự bỏ tích và ẩn lại.
- Khi người dùng tích Xóa công việc khỏi lịch sử, tự tích toàn bộ các mục kết quả hiện có trong cùng thao tác cập nhật; không tồn tại trạng thái xác nhận chỉ ẩn công việc mà giữ kết quả.
- Khi bỏ tích bất kỳ mục kết quả cần xóa nào, tự bỏ tích Xóa công việc khỏi lịch sử. Khi tích đủ các mục kết quả, không tự tích xóa lịch sử: việc giữ hay bỏ công việc vẫn do người dùng chọn.
- Bỏ tích xóa lịch sử giữ nguyên các mục kết quả đang tích, để có thể xóa hết kết quả nhưng vẫn giữ công việc và thực hiện lại sau.
- Công việc chỉ có một hoặc hai loại kết quả thì chỉ cần chọn hết những kết quả đó để xóa lịch sử; không bắt xóa kết quả chưa từng có. Công việc chưa có kết quả có thể chọn xóa lịch sử mà không gọi Sheets/YouTube/xóa thư mục không tồn tại.
- Kết quả chưa rõ, đặc biệt đã gửi media mà chưa rõ YouTube ID, phải được xác minh trước khi coi đã xóa hết và ẩn công việc. Không tự tích rồi bỏ qua phần chưa rõ.
- Core kiểm tra cùng điều kiện với UI ngay khi xác nhận, không chỉ dựa vào các ô tích. Dừng công việc đang chạy và đọc lại checkpoint/kết quả trước khi chốt danh sách xóa, tránh bỏ sót kết quả vừa tạo.

### Thực hiện xóa

- Xóa phần nào → đánh dấu phần đó Đã xóa và cho Bổ sung / thực hiện lại chọn phần đó sau khi xác nhận xóa thành công. Giữ receipt/ID kết quả cũ.
- Không xóa dây chuyền: xóa local giữ Sheets/YouTube; xóa YouTube giữ file/Sheets; xóa dòng giữ file/YouTube. Phụ thuộc Tải → Đăng chỉ dùng khi thực hiện đăng.
- Xóa lỗi/dở/mất phản hồi giữ ý định và tiếp tục xóa đúng kết quả cũ; chưa được tạo kết quả mới khi chưa rõ xóa đã thành công.
- Không rõ YouTube ID thì không đoán video để xóa/upload lại. Thư mục phải qua kiểm tra sở hữu, marker, đường dẫn/junction hiện có.
- Không tự phục hồi ngay sau xóa; chỉ Bổ sung / thực hiện lại được xác nhận mới chạy phần đã xóa.

Nếu chọn xóa lịch sử mà một phần xóa chưa xong, công việc còn hiện với trạng thái xóa lỗi/chờ. Lưu ý định xóa để Tiếp tục chỉ xóa những phần còn thiếu; chỉ ẩn sau khi xác nhận đã xóa hết các kết quả. Không có nút hoặc hộp thoại xóa lịch sử riêng.

Giữ record/receipt cần thiết trong dữ liệu riêng để không mất dấu upload chưa rõ hoặc đăng trùng khi tạo công việc mới. Không hồi sinh công việc đã ẩn khi mở app.

Ví dụ đối nghịch: Chưa ghi bảng → Bổ sung ghi bảng → Có dòng → Xóa dòng → Đã xóa → Thực hiện lại ghi bảng. Tương tự cho video YouTube và thư mục tải.

### Cập nhật lựa chọn theo tiến trình

| Kết quả hiện tại | Lựa chọn Xóa | Lựa chọn Bổ sung / thực hiện lại |
| --- | --- | --- |
| Chỉ tải hoàn tất; chưa chọn Bảng/Đăng | Thư mục video | Bảng, YouTube; dùng file đã tải |
| Chỉ ghi bảng hoàn tất; chưa chọn Tải/Đăng | Dòng Sheets | Tải, YouTube; chọn YouTube tự chọn Tải |
| Có đủ file, dòng và video YouTube | Cả ba kết quả | Không có phần cần bổ sung |
| Đã xóa dòng; file và YouTube còn | Thư mục video, video YouTube | Ghi lại bảng |
| Đã xóa file; dòng và YouTube còn | Dòng Sheets, video YouTube | Tải lại video |
| Đã xóa hết kết quả | Chỉ Xóa công việc khỏi lịch sử | Những phần đã xóa có thể thực hiện lại |

Mở hộp thoại phải đọc trạng thái mới nhất. Nếu checkpoint/kết quả đổi khi hộp đang mở, cập nhật danh sách và giữ các lựa chọn người dùng chỉ khi còn áp dụng cho đúng kết quả/receipt; bỏ những lựa chọn đã không còn hợp lệ. Kiểm tra lại ngay trước xác nhận. Thông tin trạng thái có thể hiển thị bằng văn bản, nhưng không trộn vào danh sách checkbox thực hiện/xóa. Không có lựa chọn hợp lệ thì không mở hộp thoại trống.

## 5. Mô hình, checkpoint và chống trùng

JobActions flags: Download=1, WriteSheet=2, UploadYouTube=4. UI và core đều chặn Upload không có Download. Tách thời lượng Douyin khỏi MediaFile; Rules.Validate và GoogleSheets chỉ yêu cầu dữ liệu theo hành động.

Phân biệt ba lớp: (1) yêu cầu/kế hoạch lượt đang chạy cố định; (2) kết quả hiện tại từng thao tác; (3) lịch sử lượt/receipt với requestId, đích, phiên upload/offset, fingerprint, ý định và kết quả xóa.

- Bổ sung/thực hiện lại tạo lượt mới trong cùng công việc. Thao tác thực hiện lại có generation/idempotency key mới sau khi xóa cũ xác nhận; không xóa receipt hoặc reset cờ tùy tiện để vượt chống trùng. Phần chưa xong tiếp tục lượt/phiên cũ.
- Một bộ điều phối xây bước theo thao tác và kết quả, không nhân bản năm pipeline. ReadDuration không phụ thuộc tải, MediaFile hoặc ffprobe local. Type/ghi bảng dùng SourceMetadata.DurationMilliseconds.
- CanVerifyExisting độc lập với SheetVerified; Tải + Đăng không cần receipt Sheets. Có YouTube ID chỉ xác nhận upload, không chờ xử lý mọi độ phân giải, không bắt file local còn để xác nhận.
- Tiêu đề dùng tên nhập, mô tả trống, không dành cho trẻ em; giữ thiết lập hiển thị/giới hạn project hiện có.
- Chống trùng theo thao tác, ID nguồn và đúng đích, xét receipt còn hiệu lực kể cả job đã ẩn. Receipt đã xóa xác nhận không ngăn lượt thực hiện lại được yêu cầu rõ ràng.
- Thay checkbox/thư mục/bảng/kênh trên biểu mẫu không đổi lượt đang chờ/chạy hoặc đích kết quả đã tồn tại. Đích chỉ bổ sung cho hành động chưa có hoặc lượt mới đã được xác nhận.
- Tiến độ tính theo phần cần chạy trong lượt; phần giữ lại không bị báo là vừa tạo mới.

### Kiểm tra video trùng ngay khi bắt đầu công việc mới

Hiện SubmitAsync chỉ kiểm tra RequestId; nhận diện source và kiểm tra trùng theo kênh nằm trong worker. Chuyển việc nhận diện và kiểm tra video trùng lên trước khi lưu/xếp hàng công việc mới, áp dụng cho cả năm tổ hợp, không chỉ cho đăng YouTube.

- Khóa nhận diện là nền tảng + ID video Douyin. Giải link chia sẻ và chuẩn hóa link đầy đủ trước khi so sánh, bỏ khác biệt query/link chia sẻ. Không dùng tên nhập làm khóa: cùng video đổi tên vẫn trùng; hai video khác ID trùng tên không bị coi là cùng video.
- So sánh với công việc đã hoàn tất, đang chạy/đang chờ, bị dừng/lỗi, đang xóa hoặc đang xác minh kết quả. Xét cả record đã ẩn để không bỏ sót receipt ngoài app còn hiệu lực/chưa rõ. Kiểm tra trùng video là bước thông báo chung; kiểm tra trùng từng thao tác/đích vẫn giữ riêng.
- Khi trùng, hiển thị công việc cũ: tên, ngày tạo, ID nguồn, các phần đã thực hiện/còn kết quả/đã xóa/đang dở, bảng/tab/kênh liên quan. Không tự tạo hoặc chạy bản sao từ lần bấm Bắt đầu này.
- Với công việc còn trong lịch sử, cho Mở công việc đã có hoặc Hủy. Từ công việc đó dùng Bổ sung / thực hiện lại cho phần còn thiếu/đã xóa, hoặc Tiếp tục cho phần đang dở; không tạo thêm dòng lịch sử cho cùng phần đã làm.
- Ví dụ video đã tải trước đó, nay chọn cả ba: thông báo video đã có công việc tải, mở công việc cũ rồi bổ sung Bảng và YouTube; tái sử dụng file hợp lệ, không tải/ghi/đăng lại phần có sẵn.
- Cùng ID nhưng chọn đích khác vẫn thông báo đã từng xử lý, không âm thầm coi là video mới. Không tự chuyển đích của kết quả cũ hoặc đăng lại sang kênh khác từ cảnh báo trùng; việc thêm luồng tạo bản sao cho đích khác không thuộc yêu cầu hiện tại.
- Nếu record cũ đã ẩn và tất cả kết quả đã xóa xác nhận, vẫn thông báo đã từng xử lý trước khi cho xác nhận tạo công việc thay thế. Có kết quả còn hiệu lực hoặc chưa rõ thì chưa cho tạo mới. Không hồi sinh record đã ẩn.
- Lưu SourceId ngay khi tạo mới. Dữ liệu cũ có SourceId thì giữ; link đầy đủ cũ có thể trích ID khi chuyển đổi. Link chia sẻ cũ chưa có ID cần giải/đối chiếu khi kiểm tra, không đoán bằng tên và không tuyên bố không trùng nếu chưa nhận diện được các record cần đối chiếu.
- Resolve link trước, sau đó kiểm tra và ghi công việc trong cùng vùng đồng bộ/transaction. Kiểm tra lại ở core tại thời điểm lưu để hai lần bấm hoặc hai yêu cầu đồng thời không vượt kiểm tra. Trong lúc kiểm tra trùng chưa gọi downloader, tạo thư mục, ghi Sheets hoặc upload.
- Khi nhận diện link/kiểm tra thất bại, giữ dữ liệu biểu mẫu và báo thử lại; không xếp hàng công việc chưa kiểm tra. Chưa có công việc trùng thì tạo bình thường với lựa chọn/đích đã chốt.

Phạm vi nhận diện trước khi tải là cùng ID Douyin. Cùng nội dung được đăng lại thành một ID Douyin khác chưa thể kết luận trùng chỉ bằng link; không hứa nhận diện nội dung hình/tiếng và không thêm AI/OCR để giải quyết ngoài phạm vi này.

## 6. Chuyển dữ liệu và dọn mã thừa

- Nâng schema 5 → 6 theo transaction, giữ DPAPI và rollback; app cũ từ chối schema mới.
- Full cũ → cả ba thao tác; DownloadOnly cũ → Tải. Chuyển cờ/receipt cũ thành kết quả/lượt đầu, giữ job/request ID, file partial, offset/session upload, ý định xóa và credential.
- Giữ nguyên trạng thái dừng, record đã ẩn và đường dẫn từng công việc; không tự đăng lại hoặc di chuyển/gộp thư mục trên đĩa.
- Thư mục mặc định chung lấy từ Xử lý video hiện có; đường dẫn các công việc tải riêng cũ vẫn giữ nguyên.
- Dọn tab/form/commands/collection/thư mục riêng và nhánh Full/DownloadOnly. Chỉ giữ định dạng cũ trong bộ chuyển đổi. Bỏ toàn bộ luồng chọn file local của kế hoạch trước.

## 7. Thứ tự triển khai và nghiệm thu

1. Tách đọc thời lượng; kiểm tra không gọi media/bìa/ffprobe/file local. Kiểm tra 300000/300001 ms, thiếu/0/âm/NaN/không phải số, sai ID, lỗi HTTP, timeout/hủy và fallback; không buộc có format tải để đọc thời lượng.
2. Thêm action/result/attempt và migration trước UI. Kiểm tra các trạng thái cũ, rollback, DPAPI và từ chối binary cũ.
3. Sửa validate/điều phối/checkpoint/chống trùng cho năm tổ hợp. Kiểm tra ID video trước khi tạo công việc và trước mọi tác động tải/ghi/đăng. Kiểm tra link chia sẻ/full/query khác nhau, đổi tên cùng ID, cùng tên khác ID, job hoàn tất/đang dở/đã ẩn, dữ liệu cũ thiếu SourceId và hai lần gửi đồng thời. Chỉ Bảng không tải file/bìa hoặc cần thư mục; chỉ Tải không gọi Google; Tải + Đăng không gọi Sheets. Core và UI đều ép phụ thuộc Tải.
4. Gộp biểu mẫu/lịch sử, wizard theo dịch vụ, một thư mục; thêm Bổ sung / thực hiện lại và một nút Xóa với danh sách lựa chọn cập nhật theo tiến trình, cùng điều kiện xóa lịch sử.
5. Kiểm tra vòng đời làm → xóa → làm lại của từng kết quả, mọi tổ hợp xóa, tự chọn toàn bộ khi xóa lịch sử, tự bỏ xóa lịch sử khi bỏ một kết quả, chỉ một/hai/không có kết quả, mất phản hồi/xóa dở, kết quả chưa rõ và công việc đã ẩn. Kiểm tra core từ chối ẩn khi còn kết quả cần xóa; không ghi/đăng lại phần còn tồn tại.
6. Kiểm tra dừng/tiếp tục/đóng app, bổ sung khi còn việc dở, đổi đích/checkbox, không xóa dây chuyền, marker/junction và credential. Kiểm tra lựa chọn ẩn/hiện cho từng checkpoint, thư mục dở, kết quả chưa rõ, xóa xong và kết quả thay đổi khi hộp đang mở. UI thật với hồ sơ riêng; cập nhật hướng dẫn và đóng gói bản mới khi đạt.

Google trong hồi quy dùng mock. Ghi/xóa dòng hoặc đăng/xóa video thật nghiệm thu riêng trên đích thử người dùng chỉ định. Không coi thử metadata hoặc mock là nghiệm thu Google; không thay đổi tài khoản/kết quả thật trong giai đoạn lập kế hoạch.
