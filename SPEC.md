# Đặc tả triển khai 0.3.39

## Kênh block — chưa phát hành

Sau nhận diện Douyin và trước kiểm tra thư mục trùng tên, công việc có bảng kết nối chạy bước **Kiểm tra kênh block**, kể cả chỉ Tải. Nhánh xóa kết quả và chỉ xác minh YouTube đã đăng bỏ qua bước này. Đích tab block được chụp riêng lúc tạo công việc; sec_uid tác giả được lưu checkpoint từ kênh nguồn đã có hoặc luồng metadata hiện tại. Mỗi lượt chạy đọc mới tab, so chính xác sec_uid trong Link kênh; không ghi vào tab block. Link sai báo số dòng, dòng trống bỏ qua. Kênh bị block sinh lỗi BlockedChannel, giữ kết quả và checkpoint, chờ người dùng bỏ block rồi Tiếp tục hoặc xóa công việc; không tự thử lại. Chưa có bảng thì ghi chú chưa kiểm tra, đã có bảng nhưng thiếu tab block thì dừng với lỗi cấu hình. Wizard bắt buộc chọn ba tab khác nhau và gợi ý tab tên đúng Kênh block; payload cũ thiếu trường mới vẫn đọc được.

## Điều phối thao tác từ 0.3.39

- Giữ một tác vụ tải/đăng media tại một thời điểm; tối đa bốn tác vụ nền chỉ ghi bảng, xóa Google hoặc xác nhận YouTube. Thời gian chờ thử lại không chiếm lượt chạy. Bộ lập lịch đánh thức lượt xác nhận đến hạn cả khi media khác đang truyền.
- Xóa chỉ trên máy hoặc xóa lịch sử trống chạy ngay trên thread nền sau xác nhận/checkpoint; không đưa vào FIFO video. Xóa Google bắt đầu ở nền riêng. Không đổi schema 8; công việc từ bản cũ vẫn đọc được.
- Thao tác dài giữ reservation theo job; pause/remove chỉ hủy và chờ đúng reservation đó. Resume/bổ sung/đối soát không ghi đè snapshot đang được xử lý. Dừng/thoát hủy và đợi mọi reservation lưu xong.
- Khóa thư mục theo đường dẫn Windows; khóa ghi/xóa Sheets theo spreadsheet, bao gồm hai tab ghi atomic. Kiểm tra sở hữu/junction trước xóa kết hợp được giữ. File được xử lý trước, rồi YouTube/Sheets; lỗi một phần vẫn xử lý phần khác. Chỉ ẩn lịch sử khi mọi phần đã chọn hoàn tất và không còn kết quả chưa rõ. Retry không tạo lại kết quả đã xóa.
- Xóa YouTube thiếu scope báo NeedsAuth trước DELETE; phần cục bộ đã chọn vẫn thực hiện. Tiếp tục/kết nối lại cấp đúng quyền của kênh gốc.
- Đọc mạng/tìm thư mục/hash không giữ gate lập lịch chung. Phiên Douyin thủ công và lượt dùng bộ đọc nguồn khóa cùng tài nguyên; không khóa xóa file hoặc upload không cần đọc nguồn.
- Hộp thoại Xóa/Bổ sung mở từ checkpoint đã lưu, có nút kiểm tra Google riêng. Core kiểm tra lại lựa chọn khi xác nhận. Giao diện quản lý nhóm nhập/cấu hình và token riêng từng lệnh; mở kết quả, lịch sử, làm mới tab/ngách và đổi thư mục không bị lệnh cấu hình chậm khóa chung. WaitingForYouTube/WaitingToRetry giữ app trong khay, nhưng không tính là đang sử dụng credential/nguồn.

## Quy tắc nền và các mốc trước

Mốc mặc định 5 phút: ≤300000 ms Short; >300000 Long. Bản 0.3.25 cho chỉnh mốc, gán cột hai tab và xác nhận phiên Douyin bằng phản hồi tài khoản; xem SOURCE-CHANNEL-PLAN.md. Xóa dòng video giữ dòng kênh.

Một UI WPF, một bộ điều phối, một lịch sử. JobActions: Download=1, WriteSheet=2, UploadYouTube=4. Upload cần Download. Chỉ Bảng dùng metadata JSON và không chạm media/bìa/thư mục/ffprobe. Niche/Sheets/YouTube/folder được validate và preflight theo phần cần chạy.

SelectedActions lưu kế hoạch tích lũy; PendingActions lưu lượt đang dở; kết quả hiện tại và cờ xóa độc lập. Metadata lưu ID/ms/nguồn/thời điểm, kiểm tra đúng ID và ms nguyên dương. Type lấy nguồn, không fallback local. DownloadAttemptId/SheetAttemptId/YouTubeAttemptId độc lập; receipt cũ được lưu trước làm lại kết quả đã xóa. Phiên/offset phần chưa xong giữ nguyên, không đổi generation khi thêm phần khác.

Submit nhận diện source trước queue và kiểm tra mọi record dưới gate, kể cả ẩn. Trùng ném DuplicateJobException, không tạo job; UI cho mở/hủy hoặc xác nhận thay thế record ẩn đã xóa hết. Source thiếu ở record cũ phải giải link để đối chiếu, không ghi snapshot cũ đè checkpoint worker. Không nhận diện cùng nội dung khác ID.

Bổ sung chỉ cho phần chưa chọn/đã xóa xác nhận/file đã mất, không cho phần đang chạy/đang xóa/chưa rõ kết quả. Đăng bổ sung tái sử dụng file hợp lệ hoặc ép tải lại; phần dở giữ pending cùng lượt. Đích mới chỉ gắn với phần mới; redo ưu tiên đích cũ. Đối thoại hiển thị đúng đích và ngách.

Xóa có một hộp thoại động; core dừng job, đọc lại state và validate các kết quả hiện có. RemoveHistoryRequested tách khỏi DeletionRequested. Chỉ ẩn khi toàn bộ kết quả được chọn/xóa và không có kết quả chưa rõ. Xóa riêng giữ job; lỗi giữ intent để retry, không replay upload/ghi bảng. Phần dở khác giữ checkpoint và dừng. Không xóa dây chuyền; không có route hide-but-keep-results mới.

Schema 8: migration transaction giữ ID, đích, partial, upload session/offset, deleted receipt và trạng thái ẩn. Lượt ghi đã có SheetWriteStarted từ trước schema 7 được gắn IncludeSourceChannel=false để hoàn thành intent bốn ô cũ; không tự thêm dữ liệu kênh vào lượt đã ghi. Job mới/bổ sung ghi lại dùng kênh. Binary cũ từ chối schema mới.

CanVerifyExisting chỉ phụ thuộc pending upload, ID/kênh và chưa verified/deleted; độc lập Sheets/file. Sau upload, xác nhận đúng kênh/privacy/not-kids; không đợi processing. Scheduler và token pause/close giữ nguyên.

Thư mục/tên video theo tên người dùng, không source/job ID. Ownership lưu trong AppData/download-state bằng hash đường dẫn, chặn ghi đè thư mục của công việc khác hoặc có dữ liệu. Startup chuyển `.timvideohay-owner` cũ khi nội dung khớp job/source, không xóa marker không khớp. Checkpoint DownloadDirectory/DownloadFormatId/DownloadExtension giữ lựa chọn và đuôi file khi resume. Bộ tải HTTP nội bộ dùng partial và DPAPI receipt theo source/format/path/validator/dung lượng; chia đoạn 32 MiB, tối đa ba lỗi mạng liên tiếp; chỉ nối bytes khi Content-Range/ETag hoặc Last-Modified khớp (If-Range 206 có thể không lặp lại ETag). Partial cũ chưa có validator tải lại từ đầu. Không ghép/chuyển mã hoặc tự đổi chất lượng khi đang tải.

DouyinPublicReader lấy metadata web/feed công khai, DouyinWebMetadata chuẩn hóa để nhận các rendition cao bị bỏ sót khi chỉ truyền aweme_id. Thử endpoint nguồn ratio=default bằng uri nguồn; chỉ nhận ứng viên này khi ffprobe truy cập và xác minh stream. Probe từng rendition qua cầu nối range song song, chỉ chọn URL thực đã xác nhận có cả hình/âm thanh; thử mirror tiếp theo khi URL trước không truy cập được hoặc chỉ có hình; phân biệt video-only và loại codec bytevc2. Chọn độ phân giải thực → không watermark → FPS → bản nguồn → bitrate/size. QuickTime giữ đuôi .mov; video không được chuyển mã. Khi các API lỗi mở bộ đọc video tích hợp để đăng nhập/xác minh khi cần; không chạy extractor ngoài. Không chứng nhận file nguyên bản của người đăng bằng hash.

Bìa đã chọn cover_original_scale, ưu tiên dynamic_cover nguyên kích thước khi trùng URI với bìa đã chọn và bytes xác nhận ảnh tĩnh; WebP ANIM/ANMF/VP8X animation và APNG acTL bị loại. Bìa ngang từ y_cover_hor hoặc universal_cover_horizontal169_big/169 nếu API trả về. Khi nguồn web aid=6383 thiếu ứng viên ngang, thử feed aid=568863 hoặc detail của Jingxuan trong WebView khách khi đã cần bộ đọc đó, có giới hạn thời gian, tối đa 4MB metadata; chỉ bổ sung bìa của đúng aweme_id, không thay danh sách rendition video nguồn chính. Lỗi/timeout nguồn bổ sung giữ metadata chính, còn hủy từ người dùng được truyền ra để dừng. y_cover là fallback bìa dọc. Khi thiếu cover_original_scale/y_cover, chỉ nhận cover dự phòng nếu URI khớp dynamic_cover và khác origin_cover; kiểm tra bytes tĩnh, ưu tiên bản dynamic nguyên kích thước. Poster rõ ràng bổ sung từ nguồn khác luôn được ưu tiên hơn nhóm dự phòng. Không dùng origin_cover hoặc cover chưa đối chiếu thay bìa; không cắt/ngụy tạo chiều còn thiếu, tỷ lệ ngang nguồn có thể là 4:3. Probe kích thước thực, tải bytes không chuyển mã, giới hạn 16MB/ảnh. MediaCover lưu đường dẫn, hình học, source field, hash; MediaFile.Covers tùy chọn để đọc checkpoint cũ. Checkpoint media trước ảnh, checkpoint mỗi bìa; ảnh lỗi/thiếu không chặn video hợp lệ ghi bảng/upload. Ngân sách tổng lấy file bìa 30 giây; giữ bìa checkpoint còn đúng hash khi chiều khác lỗi hoặc nguồn không còn ảnh. Metadata nguồn lỗi sau khi đã có video: kiểm tra lại file/hash trước khi bỏ qua bìa. Hủy người dùng luôn truyền ra, không tự bỏ qua để chạy Google. Không upload thumbnail YouTube. Sửa ảnh bìa trên công việc đã tải luôn bỏ cache metadata trong bộ đọc và đọc lại đúng nguồn, kể cả cache lưu đang rỗng/hết hạn; không gọi Sheets/YouTube hoặc bộ tải video; giữ state/stage/progress/kết quả đích. Bìa cũ có dấu quản lý/hash được backup vào AppData/cover-backups, không xóa/ghi đè file lạ. Dừng/thoát chờ bảo trì lưu checkpoint.

DouyinDataReader ưu tiên HTTP công khai/khách, rồi WebView2 khách InPrivate riêng, cuối cùng tự dùng phiên đăng nhập nếu khách thiếu dữ liệu bắt buộc, thông báo và không hỏi duyệt. Khách ttwid dùng lại bản guest-douyin DPAPI Token/Expires còn hạn tối đa 24 giờ; không đọc snapshot cookie tài khoản hoặc CookieCredentialId cũ. Chỉ cookie khách này được đưa vào WebView khách; không export cookie account. HTTP 401/403 yêu cầu browser/JSON CAPTCHA mở khách; thiếu UIFID tránh đăng ký liên tục và thử feed trước. HTTP 429 dừng/cooldown tối thiểu 15 phút hoặc Retry-After dài hơn; không đổi tài khoản. Metadata cache 2 phút, HTTP giãn ít nhất 2 giây. WebView chạy ẩn; tự đóng lời mời đăng nhập có thể bỏ qua. CAPTCHA hoặc đăng nhập bắt buộc mới hiện, hoàn tất thì tự ẩn. Khách chưa đủ dữ liệu sau lượt đọc giới hạn thì tự dùng phiên tài khoản và thông báo, không hỏi duyệt. Không chuyển phiên khi CAPTCHA đang chờ hoặc bị 429. Tài khoản giữ mở khi cần xác minh. Không solver CAPTCHA, extension hoặc dịch vụ trả phí. Không dùng tài khoản chỉ để bổ sung bìa. Browser không tải media, không cho download/popup/host objects/web-message, quyền thiết bị bị từ chối. Thời gian chờ metadata không giới hạn tổng thời gian tải video. CDN 429 dừng. CDN 401/403/404/410 làm mới link cùng format một lần; partial vẫn cần validator/dung lượng khớp. Metadata nguồn tải lưu DPAPI gắn source/attempt/folder, thử link đã lưu trước khi lấy lại dữ liệu; nguồn cache hỏng/thiếu hoặc đổi lượt tải cần đọc lại. Không lưu cookie/page state trong metadata. Giữ checkpoint, đích Google và kết quả cũ. Các khoảng chờ là chính sách app, không bảo đảm an toàn tài khoản.

Sheets: một header đủ bốn cột không trùng trong 30 dòng đầu; tối đa tab 20.000 dòng/200 cột. Ngoài bốn cột/công thức rỗng vẫn chiếm dòng. Kiểm tra merge/protection/strict dropdown/read trước ghi. Persist intent/nhãn type trước batchUpdate. Tìm source trước mỗi lần ghi/xác minh; intent không tìm dòng không autoappend. Không khóa mọi edit đồng thời.

Upload: URI DPAPI trước media, chunk 8MiB/Content-Range, accepted offset từ 308 Range. Resume empty PUT. Session mất/hết hạn sau media cần đối soát, không tự tạo mới. Final ID lưu trước videos.list; progress UI không thay checkpoint.

## Giới hạn

Chưa nghiệm thu OAuth/Picker/quyền/upload thật, network drop thực, tương tác tray và Windows sạch. Phiên khách mới đã tải hoàn chỉnh video ô tô 1080p và đủ hai bìa; sửa đủ hai bìa video bọ ngựa mà giữ nguyên MOV 4K đã tải trước. Không đọc cookie tài khoản trong hai phép thử này; không thay kiểm tra phát và so sánh hình ảnh bằng mắt. Google adapter dùng HttpClient/System.Text.Json thay bộ SDK sinh mã lớn; PKCE/state/refresh/resumable có kiểm tra riêng, không thay integration thật.

## Bảo mật và bản chia sẻ 0.3.11

Xem SECURITY.md. Từ 0.3.46 chỉ còn một bản build, không nhúng Google client và không còn chế độ ForSharing; mọi hồ sơ mới tự nhập OAuth client JSON trong app. Ở 0.3.11, ForSharing cấm nhúng Google client; người nhận tự tạo project/import JSON. Gói sạch mới, bỏ PDB/đường dẫn origin, khóa dependency và kiểm tra resource/hash/nội dung theo chính sách. ffprobe hash ghim trong assembly; media mạng đi qua HTTPS/CDN allowlist và socket DNS chặn IP nội bộ; probe từ xa qua cầu nối loopback mã ngẫu nhiên, không đưa URL CDN trực tiếp cho ffprobe. ACL CurrentUser/System, chặn reparse khi tải/xóa. Bản portable chưa ký; hash không chống giả mạo toàn gói. Không yêu cầu chứng thư hoặc project chung cho việc dùng cá nhân/chia sẻ thử.

Cột video/kênh được gán theo chỉ số vị trí và hàng tiêu đề (1–30), không phụ thuộc tên tiêu đề khi đã có mapping. Chèn/xóa/chuyển cột cần chọn lại. Snapshot đích và mốc Short/Long giữ riêng theo công việc; legacy thiếu mốc dùng 300000 ms, thiếu mapping dùng nhận diện tiêu đề cũ.

0.3.25: Tự tìm lại thư mục theo mã Windows, sau đó chỉ mục/phạm vi đích đã dùng; xác minh SHA-256, tránh nhận nhiều bản khớp. YouTube không còn ID sau GET đúng kênh thành công tự coi là đã xóa thủ công, không yêu cầu xác nhận. Lỗi đọc giữ kết quả.

0.3.25 bổ sung kiểm tra một đường dẫn đúng tên công việc ngay dưới gốc các ổ Fixed/Removable đang sẵn sàng trước chỉ mục; không duyệt gốc ổ. Kết quả vẫn phải qua kiểm tra sở hữu/dung lượng/SHA-256 và không có file lạ. Nhiều bản khớp yêu cầu chọn thủ công.

0.3.25: Lịch sử dùng CurrentActions loại bỏ cờ kết quả đã xóa thay SelectedActions gốc; lọc cùng mask. Không đổi invariant upload cần download hay receipt gốc. EnqueuedAt nullable tương thích schema 8; submit/resume/add/delete chụp thời gian xếp hàng. JobQueue.Order dùng chung cho worker/UI: xóa trước, FIFO theo EnqueuedAt ?? Created và tie-break Created/Id. Lượt xác nhận đến hạn giữ chính sách xen vào trước xử lý thường. QueuePosition/QueueLabel chỉ hiển thị, không persist; tính trước khi lọc.

0.3.26: Bộ lọc checkbox đa trạng thái OR, công việc ANY mặc định/ALL tùy chọn; nhóm rỗng không lọc, AND với tìm kiếm. Giữ global queue positions. Làm mới đọc kết quả mục được chọn, tabs/niches một lần; không resume hoặc xóa. Mọi section mở/thu gọn độc lập do người dùng; không handler thu gọn chéo hoặc tự thu gọn sau submit. Footer chi tiết luôn giữ header/actions, nội dung có thể thu gọn nhưng không có ScrollViewer và không Visibility trigger theo NewJobExpanded/SelectedJob. Bước xử lý chuyển từ cột lịch sử vào footer. Vùng trên có cuộn và DataGrid chiều cao hữu hạn để duy trì virtualization.

0.3.27: Bộ lọc dùng checklist xổ xuống với nút xóa riêng từng nhóm/từ khóa. Trạng thái OR; công việc luôn ALL, không có tùy chọn ANY. Làm mới chỉ gọi local projection, không RecheckResults/Sheets/niches; toolbar Làm mới/Tạm dừng tất cả ở lịch sử. Chi tiết trở về name/results + action row + expander một cột cuộn full width, max 260 px. Mọi section vẫn mở/thu gọn độc lập.

0.3.28: JobDetailHost chuyển vào StackPanel thuộc WorkArea, sau lịch sử; cùng cuộn với biểu mẫu/lịch sử, không Dock.Bottom. Chi tiết có header tên, kết quả gọn, stage/progress/percent chung hàng, thông tin kỹ thuật hai cột, đường dẫn ellipsis + tooltip nguyên văn; nội dung dài cuộn max 180 px. Không tự mở/thu gọn mục khác. Không thay đổi worker hoặc cách lấy dữ liệu.

## 0.3.29 — chi tiết và tiến độ truyền video

Hàng nút mở kết quả ở trên Expander chi tiết; toàn bộ nội dung mở rộng có vùng cuộn riêng 180 px. Cả khung vẫn nằm trong cuộn chung của trang. Tải xuống lưu số byte/total, checkpoint tối đa mỗi 5 giây và khi dừng/lỗi. Khi mở lại, phục hồi từ receipt riêng và file .part trong thư mục đã kiểm tra sở hữu; không tra cứu Douyin. Upload dùng byte YouTube xác nhận, cập nhật phần trăm trước checkpoint và sau truy vấn phiên. Tiền kiểm/chuyển bước không xóa tiến độ. Thanh ghi rõ bước tải/đăng, số thập phân; 100% của bước truyền không đồng nghĩa hoàn tất mọi công việc.

## 0.3.30 — tải lại và lịch sử

Metadata media được phép dùng lại sau bổ sung tải lại khi có receipt của lượt tải trước đúng job, source, attempt và thư mục. Byte receipt vẫn riêng theo lượt mới, không mang offset cũ sang. Nếu URL hết hạn thì lấy mới một lần; 429 vẫn dừng/cooldown. Web challenge không ngăn thử nguồn công khai feed độc lập. Tải có format hợp lệ không yêu cầu thời lượng từ xa; ffprobe kiểm tra thời lượng file, ghi bảng vẫn phải có thời lượng nguồn. Bìa tùy chọn không kích hoạt account.

Mở app mặc định thu gọn Tìm kiếm và bộ lọc, Chi tiết tiến trình và kết quả. Thanh cuộn dọc riêng luôn hiện khi mở chi tiết; không có overflow thì thanh bị vô hiệu. Mở/thu gọn các mục vẫn độc lập. Cột Công việc chỉ chứa Tải/Bảng/YouTube, rỗng nếu không còn phần nào; bộ lọc công việc thêm Trống. Khi chọn Trống cùng các phần khác, lấy cả nhóm trống và nhóm có đủ các phần đã tích; kết hợp trạng thái/từ khóa giữ nguyên.


## Bản 0.3.31: đọc phiên khách và cuộn chi tiết

App nhận diện cả dấu × dạng SVG không có nhãn của lời mời đăng nhập Douyin. Lời mời có thể mở lại khi chuyển đường dẫn; app tự đóng có giới hạn (tối đa 6 lần trong lượt đọc), rồi giữ cửa sổ cho người dùng nếu vẫn bị che. Không coi lời mời chưa đóng là bằng chứng bắt buộc dùng tài khoản. CAPTCHA vẫn chờ người dùng và không tự chuyển phiên.

Cửa sổ đọc chạy ngoài màn hình, không lấy focus/không hiện trên thanh tác vụ; WebView vẫn hoạt động để trang tiếp tục tạo yêu cầu. Chỉ đưa cửa sổ về màn hình khi cần thao tác. Đóng app/công việc vẫn đóng bộ đọc.

Cuộn chuột trong Chi tiết tiến trình và kết quả cuộn phần này trước; ở mép hoặc nội dung vừa khung thì cuộn trang ngoài. Thanh cuộn riêng chỉ hiện khi nội dung vượt khung, tránh thanh cuộn vô hiệu gây hiểu nhầm.

Nguồn công khai có thể trả 403 thiếu UIFID hoặc trả video đề xuất khác ID. App không nhận video đề xuất thay video nguồn. Phiên khách cũng có thể bị Douyin từ chối dù SDK đã gửi định danh; vẫn giữ tài khoản là phương án cuối cho dữ liệu bắt buộc. Không cam kết mọi link đều tải được bằng khách.

## Điều chỉnh 0.3.32

Đọc metadata và bìa video qua HTTP trước. Không mở WebView hoặc dùng tài khoản chỉ vì API rỗng/sai ID/UIFID/lỗi mạng. WebView video chỉ chạy cho xác minh/đăng nhập máy chủ yêu cầu rõ ràng. Không thay luồng đọc kênh nguồn của bước điền bảng. Chi tiết có thanh cuộn dọc riêng luôn hiện, rộng 20 px, toàn bộ nội dung cuộn riêng với chiều cao cố định 170 px.

## Điều chỉnh 0.3.33

Chiều cao nội dung Chi tiết tiến trình và kết quả cố định 170 px khi mở; không phụ thuộc độ dài dữ liệu. Khi thu gọn vẫn giữ tiêu đề và các nút mở kết quả.


## 0.3.34 — thanh cuộn theo độ dài nội dung

Chi tiết tiến trình và kết quả giữ Height=170, VerticalScrollBarVisibility=Auto. Nội dung vừa khung không hiện thanh cuộn; nội dung vượt khung hiện thanh cuộn dọc riêng rộng 20 px. Thu gọn/mở rộng và cuộn chuột giữ hành vi hiện có.


## 0.3.35 — yêu cầu lựa chọn và tiếp tục

JobIssue lưu mã nguyên nhân, action bị ảnh hưởng, dependency, RetryAt và ID yêu cầu trong payload DPAPI hiện có; tối đa 50 decision receipts mỗi job. Legacy NeedsAuth/NeedsAction/Failed được đọc và tạo issue khi khởi động. WaitingToRetry nằm cuối enum để giữ số trạng thái cũ; deadline được lưu và scheduler không giữ worker cho một yêu cầu đang chờ người dùng. Thử lại mạng/429 tối đa hai lần tự động, checkpoint được đối chiếu trước khi gửi lại. Authentication dependency gom UI và chặn API cùng credential đang thiếu quyền; rebind chỉ áp dụng đúng spreadsheet/channel gốc và job NeedsAuth, không tự chạy job Paused. Yêu cầu stale bị từ chối theo issue ID và lựa chọn hiện tại. Omit chỉ loại work chưa rõ ràng chưa gửi; không xóa kết quả, không bỏ unknown remote mutation. Upload inspection dùng query rỗng, kết quả đã có có thể liên kết sau kiểm tra đúng kênh. Thông báo modeless, bật lại từ badge, bring-forward một lần rồi bỏ Topmost; CAPTCHA giữ cửa sổ xác minh riêng.


## 0.3.44 — tải phân đoạn liên tục và lưu tiến độ từng vùng byte

Sau một Range hợp lệ, file đủ lớn với ETag mạnh dùng tối đa bốn worker. Mỗi worker lấy yêu cầu kế tiếp tối đa 4 MiB ngay khi xong phần trước; HttpClient được phép tái sử dụng kết nối khi phản hồi đã đọc hết. Mỗi luồng đọc buffer 128 KiB, kiểm tra Content-Range/tổng dung lượng/ETag và Content-Length trước khi ghi theo vị trí. Những vùng kề nhau được gộp; tiến độ bằng tổng byte trong bản đồ, kể cả các vùng nằm sau khoảng trống.

Receipt riêng được DPAPI bảo vệ bổ sung Segments Version=1, validator mạnh và danh sách khoảng [Start, End). Validator cấp cũ để rỗng khi dùng bản đồ, để bản app cũ tải lại an toàn thay vì nhận độ dài file có khoảng trống là prefix. Receipt cũ liên tục vẫn tiếp tục được. Không đổi schema SQLite. Flush dữ liệu xuống đĩa trước khi lưu bản đồ định kỳ khoảng 5 giây; tạm dừng/lỗi chờ các worker đóng rồi flush và lưu toàn bộ vùng đã nhận, sau đó checkpoint công việc. Mở lại chỉ tin vùng đã checkpoint, tải các khoảng thiếu; phần ghi sau checkpoint cuối có thể tải lại sau mất điện.

Lỗi mạng giảm song song 4→2→1 và thử lại từ vùng thiếu; lỗi hết hạn link/HTTP 429/validator được ưu tiên hơn lỗi hủy từ worker khác. CDN bỏ Range hoặc trả một phần dải thì chuyển về prefix liên tục, đối chiếu lại nguồn bằng bộ tải tuần tự. Nếu nguồn đổi, thay từ đầu; bản đồ không hợp lệ bị loại. Tất cả vùng phải đủ trước khi đổi .part thành file kết quả.

Tham khảo nguyên tắc phân đoạn, tái sử dụng kết nối và lưu vị trí tải của [IDM](https://www.internetdownloadmanager.com/support/segmentation.html). App triển khai hàng đợi phần 4 MiB liên tục; không tích hợp hay cần cài IDM.
