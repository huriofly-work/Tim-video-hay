# Thiết kế hệ thống app Windows — Tìm video hay 0.3.33

Mốc phân loại mặc định ≤300000 ms Short, lớn hơn Long; có thể chỉnh trong app. GoogleSheets ghi nguyên tử 4 ô video + 5 ô kênh, cập nhật theo sec_uid. DouyinDataReader lấy video qua HTTP trước (web detail + Select, phiên khách ttwid, feed đúng ID). Chỉ xác minh rõ ràng mới mở WebView khách; chỉ login_required/401 mới mở phiên tài khoản. Metadata trống/sai ID/mạng/UIFID không mở browser. Đọc kênh nguồn vẫn dùng WebView khách rồi tài khoản khi cần, độc lập với download-only. WebView chạy ẩn, tự đóng lời mời đăng nhập; hiện khi cần thao tác và tự ẩn khi hoàn tất. Metadata tải lưu mã hóa theo công việc/lượt tải/thư mục để tiếp tục mà không đọc lại trang; chỉ làm mới link bị từ chối một lần. Không export/import cookie tài khoản; ttwid khách dùng lại từ guest-douyin DPAPI.

Ngày 09/10/2026. C#/.NET 10 WPF/MVVM; UI và bộ điều phối trong một tiến trình, tác vụ mạng/file bất đồng bộ. Không cần server riêng, frontend trình duyệt, extension, AI hoặc dịch vụ lấy nguồn trả phí. Thực thi và giới hạn hiện tại: SPEC.md; bằng chứng: VALIDATION.md.

Một biểu mẫu chọn Tải / Bảng / YouTube, một lịch sử, một thư mục mặc định. Đăng cần tải. Bảng lấy thời lượng JSON Douyin, độc lập media. Ngách lấy dropdown của đúng bảng khi được chọn. Bổ sung và xóa theo từng kết quả với receipt/generation; pre-submit chống trùng ID nguồn.

Tên tối đa 30 ký tự gồm dấu cách, title mặc định tên, description trống/not-kids. Đăng giữ nguyên file tải chất lượng cao; không ghép/chuyển mã, không subtitle/OCR/TTS. Ảnh bìa tác giả lưu local. OAuth chọn đích, không cần link bảng/kênh; mỗi dịch vụ có thể dùng tài khoản riêng.

## 3. OAuth, bảng và kênh: loại bỏ đầu vào thừa

OAuth cấp quyền truy cập, không tự chọn mọi tài nguyên. Đích vẫn cần được xác định, nhưng ID lấy từ lựa chọn/kết quả API, không cần nhập URL thủ công.

| Thông tin | Giao diện mới | Lưu nội bộ |

| --- | --- | --- |

| Bảng Google Sheets | Chọn bảng / Đổi bảng | spreadsheetId + tên để hiển thị |

| Tab trong bảng | Chọn từ danh sách tab thật | numeric sheetId + tên để hiển thị |

| Kênh YouTube | Kết nối YouTube / Đổi kênh; hiện tên kênh | credentialRef + channelId |

| Thư mục tải | Hộp chọn thư mục Windows | Đường dẫn |

| Nguồn video | Người dùng dán link Douyin | platform + sourceId dạng chuỗi |

Không có ô nhập link bảng, link kênh, ID bảng, ID tab hoặc channelId. Nút Mở bảng/Mở kênh có thể tạo URL từ ID đã lưu để người dùng xem; đó là đầu ra tiện dụng, không phải cấu hình.

### 3.1 Chọn bảng bằng Google Picker Desktop

Nút Chọn bảng mở luồng OAuth + Google Picker dành cho desktop trong trình duyệt hệ thống. Giới hạn chọn một tệp và MIME Google Sheets. Google callback trả picked_file_ids cùng authorization code; app xác minh phiên OAuth, đổi code thành token và đọc lại metadata/loại tệp bằng API, không tin callback chưa xác minh.

Luồng dùng duy nhất scope drive.file, không cộng YouTube/openid/spreadsheets vào yêu cầu Picker. Google hiện quy định luồng desktop chỉ nhận drive.file. Các tham số trigger_onepick=true, prompt=consent, MIME filter và callback do adapter cấu hình, người dùng không nhập.

Token drive.file dùng được cho các method Sheets đã chọn như spreadsheets.get và spreadsheets.batchUpdate đối với tệp người dùng đã cấp quyền. Không cần xin scope toàn bộ spreadsheets hay drive.metadata.readonly để chỉ làm việc với bảng được chọn.

Sau chọn bảng, app đọc tab bằng Sheets API, cho chọn riêng tab video/tab kênh nguồn và gán cột. Tiêu đề cũ được dùng làm gợi ý, không là điều kiện khi có mapping hợp lệ. Không cố định tên Bảng công việc. Khi đổi bảng, kiểm tra xong mới thay cấu hình.

Quyền truy cập tệp qua drive.file không tạo quyền chỉnh sửa vượt quá quyền của người dùng. Có thể kiểm tra capability canEdit qua metadata nếu trả về, kiểm tra vùng bảo vệ và vẫn xử lý từ chối ghi thật.

Không tự làm danh sách mọi tệp Drive bằng scope restricted chỉ để bỏ URL. Không nhúng browser vào app. Không cần trang JavaScript Picker tự viết, API key kiểu Web Picker hoặc web server phục vụ giao diện. Chỉ mở listener loopback tạm để nhận callback, đóng sau hoàn tất/hủy/hết hạn.

Căn cứ: [Picker Desktop](https://developers.google.com/workspace/drive/picker/guides/desktop-mobile-picker), [Sheets get scopes](https://developers.google.com/workspace/sheets/api/reference/rest/v4/spreadsheets/get), [Sheets batchUpdate](https://developers.google.com/workspace/sheets/api/reference/rest/v4/spreadsheets/batchUpdate).

### 3.2 Nhận diện kênh từ OAuth YouTube

Nút Kết nối YouTube mở OAuth chính thức với youtube.upload + youtube.readonly. App gọi channels.list(mine=true), lấy channelId/tên kênh mà credential thực sự đại diện và hiển thị ngay trong cửa sổ.

Không suy quyền đăng từ URL, handle, email hoặc vai trò quản lý Studio. Không dùng channelId do UI nhập để ép videos.insert đăng vào một kênh khác. Với kênh thương hiệu hoặc nhiều danh tính, người dùng chọn danh tính trong luồng Google; app xác minh kết quả. Nếu không nhận diện duy nhất một đích upload hợp lệ, dừng cấu hình và yêu cầu kết nối lại.

Một kênh được nhận diện duy nhất thì tự gắn, không thêm bước chọn lại từ URL. Đổi kênh là cấp quyền/chọn lại đúng danh tính, kiểm tra rồi thay kết nối hoạt động. Không triển khai kho nhiều kênh/đa kênh khi phạm vi chỉ cần một kênh.

Hai kết nối Sheets và YouTube là có chủ đích: Picker desktop giới hạn scope và YouTube credential gắn danh tính kênh. Dùng cùng project/client Desktop nhưng lưu credential theo chức năng, không giả định refresh token có thể thay thế cho nhau. Cùng tài khoản Google có thể tận dụng đăng nhập đang có; người dùng cũng có thể chọn tài khoản khác mà không thay kiến trúc.

Thu hồi quyền Google cấp project có thể ảnh hưởng cả hai kết nối. Ngắt kết nối local và thu hồi quyền Google phải được phân biệt; không âm thầm revoke một dịch vụ rồi làm hỏng dịch vụ kia.

Căn cứ: [channels.list](https://developers.google.com/youtube/v3/docs/channels/list), [videos.insert](https://developers.google.com/youtube/v3/docs/videos/insert), [OAuth Desktop](https://developers.google.com/identity/protocols/oauth2/native-app).

## 4. Cấu trúc chương trình

Một solution, ba project sản phẩm và một project test. Lớp nghiệp vụ không tham chiếu WPF, Google SDK, SQLite hoặc executable tải nguồn.

```text

native/

  TimVideoHay.Desktop/           WPF, Views, ViewModels, bootstrap, tray

  TimVideoHay.Core/              Job model, quy tắc, điều phối, interfaces

  TimVideoHay.Infrastructure/    Google, SQLite, DPAPI, file, nguồn Douyin

  TimVideoHay.Tests/             Quy tắc, adapter contracts, recovery

tools/

  # Không dùng yt-dlp, Chromium riêng hoặc engine cookie khách.

  ffprobe.exe                   Kiểm tra file

packaging/                      Script cài theo user

scripts/                        Prepare tools, build/test/publish

artifacts/windows-x64/          Bản chạy tự chứa (không commit)

legacy/browser-v022/            Bản browser lưu để khôi phục

```

Desktop gọi Core trực tiếp qua service/command. Core gọi ISheetDestination, IYouTubeDestination, IVideoSource, IJobStore, ISecretStore; Infrastructure thực thi. Download/probe/hash nằm cùng adapter nguồn, chưa tách IFileStore khi chỉ có một loại nguồn. Không generic repository, event bus, CQRS/MediatR hoặc plugin framework khi chưa có nhu cầu.

| Phụ thuộc | Lý do tồn tại |

| --- | --- |

| .NET 10 + WPF | UI Windows và logic C# cùng một nền tảng |

| INotifyPropertyChanged/ICommand có sẵn | MVVM nhỏ, không cần thêm toolkit chỉ cho vài commands |

| Microsoft.Data.Sqlite | Giao dịch và checkpoint local; không ORM |

| HttpClient/System.Text.Json có sẵn | Adapter cho các endpoint Google chính thức, không kéo toàn bộ SDK sinh mã |

| DPAPI của Windows | Mã hóa token Google/biên nhận/URI upload dưới user hiện tại |

| yt-dlp executable | Tách phần nguồn Douyin thường thay đổi khỏi nghiệp vụ |

| ffprobe executable | Thời lượng/stream/chất lượng file thật |

Bản triển khai 0.3 đã chọn adapter HttpClient hẹp cho OAuth PKCE/Picker, Sheets và YouTube, thay đề xuất SDK ban đầu để giảm dependency trong app có ít endpoint và cần kiểm soát session qua restart. Vẫn dùng endpoint/giao thức Google chính thức và crypto chuẩn .NET, không dựng API hoặc thuật toán mã hóa mới. Đánh đổi là phải bảo trì và nghiệm thu OAuth/refresh/lỗi giao thức; test giả lập chưa thay nghiệm thu Google thật. Không thêm ffmpeg khi chỉ giữ nguyên file có audio/video.

## 5. Giao diện và thiết lập

Hai tab: Xử lý video và Kết nối & cài đặt. Biểu mẫu có ba lựa chọn, các trường phụ thuộc thao tác; một lịch sử lọc theo hành động/trạng thái, có Bổ sung và một nút Xóa trên mỗi dòng. Hai hộp thoại đọc lại checkpoint, cập nhật lựa chọn và giữ phần đang dở rõ ràng.

## 6. Điều phối

Resolve Douyin và kiểm tra trùng ID trước khi tạo job. Preflight dịch vụ được chọn, đọc duration nếu ghi bảng; sau đó chạy Tải → Bảng → YouTube, bỏ phần không chọn hoặc đã có kết quả. Chỉ Bảng không gọi media/bìa/ffprobe. Thiếu dữ liệu nguồn dừng trước ghi, không đoán theo file. Một coordinator chạy PendingActions và checkpoint từng bước; phần cần thao tác không chặn job khác. Không thêm process worker/service riêng.

I/O dùng async/await và cancellation, không chặn Dispatcher WPF. Progress đưa về UI theo nhịp giới hạn; danh sách lịch sử virtualized và phân trang khi lớn. Không cần SSE/WebSocket/polling HTTP để UI lấy trạng thái cùng process.

Single-instance mutex; mở lần hai đưa cửa sổ hiện có ra trước. Thu nhỏ vẫn chạy. Nếu có job đang chạy, đóng cửa sổ đưa app vào khay và báo rõ; Thoát thực sự sẽ dừng I/O có kiểm soát, lưu checkpoint rồi kết thúc. Không Windows Service/tự chạy cùng Windows mặc định. Không hứa tiếp tục chạy khi tắt máy.

Khởi động lại nhận diện bước gián đoạn, đối soát và hiện nút Tiếp tục; không tự upload lại hoặc tự tiếp tục thay đổi đích. Người dùng không phải đăng nhập lại chỉ vì đổi cửa sổ/profile browser.

Bản 0.3.6 không chờ processing YouTube: có ID và xác nhận đúng kênh/chế độ/đối tượng là hoàn tất upload. Tự phục hồi xác nhận chỉ đọc khi đã có ID, độc lập với bảng; metadata tạm thiếu/lỗi mạng có lịch đọc lại tự động. Lỗi thực sự và upload chưa rõ kết quả vẫn dừng để xử lý; không replay side effect.

## 7. Dữ liệu và kết nối

Dữ liệu vận hành ở %LocalAppData%/TimVideoHay, tách binary cài đặt và thư mục video người dùng. SQLite WAL, migration version, giao dịch ngắn; không transaction mở suốt request mạng. Sao lưu dùng SQLite backup nhất quán, không copy đơn lẻ DB đang có WAL.

Ba nhóm dữ liệu chính: Settings/Connections; Jobs (input, snapshot, stage/status, checkpoints/result); Artifacts (path, hash, size, duration/source). Nhật ký có rotation riêng. Checkpoint theo bước lưu trong Jobs, chưa cần table event/job_step riêng khi chỉ phục hồi từng job. Chỉ tách bảng khi truy vấn/retention thực tế chứng minh cần.

Snapshot đích gồm credentialRef, spreadsheetId, sheetId, channelId, outputDir và thông tin đăng. Đổi bảng/kênh chỉ áp dụng job mới. Tiếp tục job cũ phải cùng đích/danh tính; không lấy cấu hình mới thế vào checkpoint cũ.

SourceId Douyin là chuỗi. Client request ID chống bấm đôi; canonical source + channelId bảo vệ upload đã có/chưa rõ. Tải riêng không bị chặn bởi upload trước, có thể tái dùng artifact đúng nguồn/hash/thư mục nếu phù hợp.

Token Google/biên nhận/session URI mã hóa DPAPI CurrentUser bằng C#; không cần PowerShell helper. Không lưu plaintext trong DB/log/argv. Credential reference tách khỏi payload hiển thị. Access token làm mới tự động, invalid_grant chuyển cần kết nối lại. Credential mới phải được kiểm tra đúng đích trước phục hồi job.

OAuth External Testing với scope chức năng có thể cấp refresh token hết hạn sau 7 ngày; hướng dẫn cấu hình không hứa đăng nhập một lần mãi mãi. [Vòng đời token](https://developers.google.com/identity/protocols/oauth2#expiration), [DPAPI trong .NET](https://learn.microsoft.com/en-us/dotnet/standard/security/how-to-use-data-protection).

## 8. Douyin và file

Bộ tải HTTP C# nội bộ và ffprobe để đo chất lượng thực tế/hash. Metadata từ WebView2 được chuẩn hóa thành rendition; probe chọn bản có hình/âm thanh và chất lượng cao nhất khả dụng, giữ ratio=default. Jingxuan chỉ bổ sung poster, không thay danh sách rendition nguồn.

Chỉ đọc một video đúng ID, không playlist/tìm kiếm/thống kê. Spawn executable với argument list, timeout, output giới hạn và cancellation; không ghép shell string. Pin phiên bản/checksum, chỉ cập nhật từ release chính thức sau kiểm tra.

Lấy nguồn/metadata rồi C# stream file .part, probe, rename an toàn. Nếu bài thử chứng minh download qua executable cần thiết, adapter sở hữu tải thay C#; chỉ giữ một đường tải trong release. Không hai download engine chồng nhau.

Lấy danh sách rendition đầy đủ theo thông số web và thử bản nguồn ratio=default. Probe độ phân giải/stream thực, chọn bản audio/video chung chất lượng cao truy cập được. Không chuyển mã/ghép. Mỗi video có thư mục và tên file theo tên nhập, chỉ sanitize ký tự Windows cấm, không nối source/job ID. Ownership/checkpoint chặn trùng tên/ghi đè hoặc nối part khác format; không sửa tên Sheets/title. Tải ảnh bìa tĩnh local riêng. Hash kiểm tra tái dùng/resume, không chứng minh đây là file gốc tác giả.

Chỉ resume download nếu Range và ETag/size xác nhận cùng tài nguyên. URL hết hạn thì resolve lại; thay bản media thì tải lại, không nối byte sai. Chặn URL không hợp lệ/private/loopback và kiểm tra redirect; không gửi cookie Google hay cookie Douyin sang CDN khác domain.

DouyinDataReader điều phối dữ liệu video/kênh; DouyinPublicReader kiểm tra đúng ID/thời lượng, giữ rendition và tìm bìa tác giả qua nguồn công khai. Phiên khách ttwid dùng lại tối đa 24 giờ từ định dạng Token/Expires cũ; chỉ đọc khóa guest-douyin. Cookie khách đó được đưa riêng vào WebView khách khi cần; không sao chép cookie tài khoản. HTTP 401/403 yêu cầu trình duyệt hoặc JSON CAPTCHA mở WebView khách, không chạy endpoint liên tục. Thiếu UIFID thử feed trước. HTTP 429 dừng/cooldown; không chuyển tài khoản. Khách CAPTCHA/lời mời đăng nhập giữ cửa sổ để người dùng xử lý, không auto fallback theo timeout. Chuyển tài khoản bằng lựa chọn rõ ràng trong cửa sổ khách. Thiếu bìa không dùng tài khoản. CookieCredentialId cũ bị JSON bỏ qua; Google/biên nhận giữ nguyên. DouyinWebMetadata là parser dùng chung. Không dùng API trả phí, solver CAPTCHA hoặc tạo chữ ký giả.

## 9. Sheets

Gán bốn/năm trường theo chỉ số cột vật lý và hàng tiêu đề, lưu mapping trong đích và checkpoint công việc. Tên tiêu đề có thể đổi; chèn/xóa/di chuyển cột cần gán lại. Cấu hình cũ chưa có mapping tự nhận diện các tiêu đề cũ. Tên tab đổi thì vẫn dùng sheetId; tab bị xóa thì báo chọn lại, không ghi sang tab khác cùng tên.

Đọc userEnteredValue, công thức và validation của vùng liên quan; công thức trả rỗng không phải ô trống. Tìm canonical source trước ghi: đúng nội dung thì tái dùng, khác nội dung thì báo xung đột. Hàng mới nằm sau vùng dữ liệu, không lấy bốn ô trống trong hàng đã có dữ liệu khác.

Lưu ý định/hàng/giá trị trước request. Ghi bốn ô trong một spreadsheets.batchUpdate, field mask userEnteredValue, stringValue cho chuỗi; không sửa format, công thức, validation hoặc cột khác. Có nhãn Video ngắn/Video dài thì dùng nhãn hợp lệ của bảng. Ngách không hợp dropdown thì báo rõ, không tự nới quy tắc.

Response ghi thành công phải đọc lại bốn ô. Timeout thì đối soát trước ghi lại, không append mù. Chỉ đọc các ranges/fields cần, không tải toàn bộ workbook mỗi lần.

Google bảo đảm request batch được áp dụng cùng nhau nhưng không khóa cộng tác viên; mutex/SQLite chỉ bảo vệ job app này. Không hứa chống ghi đè tuyệt đối khi người khác đồng thời sửa đúng ô. Đọc lại/sai khác thì giữ checkpoint và báo xung đột. [batchUpdate](https://developers.google.com/workspace/sheets/api/reference/rest/v4/spreadsheets/batchUpdate).

Không thêm cột status/link YouTube/ID ẩn hoặc developer metadata chưa cần. Kết quả và lỗi ở lịch sử app.

## 10. YouTube và khả năng phục hồi

Trước upload xác minh credential/channelId, title và category mặc định hợp lệ. Gửi file nguyên, selfDeclaredMadeForKids=false và visibility snapshot. Không suy category từ ngách bằng AI hoặc dựa defaults Studio.

Resumable: lưu ý định; nhận và mã hóa session URI trước gửi media; dùng stream/chunk phù hợp với cancellation/progress và lưu offset. Khi mất mạng hoặc response cuối, hỏi session để lấy offset/kết quả server xác nhận. Không suy số byte đã gửi bằng số byte server nhận.

Lưu video ID ngay khi có. Session hết hạn sau gửi mà chưa rõ kết quả thì cần đối soát, không khởi tạo upload mới vô điều kiện. Có ID thì bước tiếp tục chỉ kiểm tra video đó. Không tự chọn video theo title/tên file để kết luận trùng khớp. [Resumable upload](https://developers.google.com/youtube/v3/guides/using_resumable_upload_protocol).

Kiểm tra ID, kênh, privacyStatus, selfDeclaredMadeForKids/madeForKids và trạng thái upload failed/rejected/deleted. Không truy vấn processingDetails hoặc dùng xử lý chất lượng làm điều kiện Hoàn tất. Thiếu metadata xác nhận/lỗi mạng thì đọc lại tự động với backoff, giữ ID; không đo phần trăm xử lý YouTube bằng suy đoán. Hoàn tất là upload và metadata đã xác nhận, không phải bảo đảm HD/4K đã xử lý xong.

Project API chưa xác minh không bị giới hạn chỉ đăng Private theo tài liệu hiện tại. Mặc định mới Unlisted; giữ chế độ đã lưu. Job yêu cầu Unlisted mà nhận Private thì chưa đạt, không tự hạ mục tiêu hoặc upload lại. OAuth verification và audit upload là hai việc khác nhau. [videos.insert](https://developers.google.com/youtube/v3/docs/videos/insert).

Retry có giới hạn cho mạng/429/5xx, Retry-After/backoff/jitter. 401 refresh một lần; invalid_grant cần kết nối lại; 403 phân biệt quyền/quota/hạn chế; 400/schema không retry vô hạn. Không có giao dịch chung Sheets/file/YouTube hoặc exactly-once tuyệt đối; thiết kế dùng ý định và đối soát để giảm trùng.

## 11. Đóng gói, kiểm thử và dọn phần cũ

Release self-contained .NET cho Windows x64, đóng gói bằng installer theo user; không buộc cài SDK, Node, Python hoặc đăng ký server. Runtime/tool cần thiết đi kèm và có checksum/license. Build phát triển cần .NET SDK, không suy từ runtime cũ là đủ. Installer/update thay binary khi app đã lưu checkpoint, không tự xóa dữ liệu/video. [Publish .NET](https://learn.microsoft.com/en-us/dotnet/core/deploying/).

Kiểm thử Core không cần WPF/tài khoản thật. Adapter contracts kiểm thử giả lập giao thức/timeouts; nghiệm thu Google/Douyin trên tài khoản/link thật là riêng. Kiểm tra UI ở DPI/phóng to cửa sổ, nhập Unicode, tiến độ/cancel, lịch sử dài và chạy nền. Đo bộ nhớ, thời gian phản hồi và recovery trước khi tuyên bố mượt.

Trước viết toàn bộ UI: thử ba rủi ro lớn trong adapter C# nhỏ — Douyin local, Picker Desktop + Sheets drive.file, YouTube đúng danh tính + resumable. Picker phải thử hủy, sai MIME, callback giả, grant thiếu scope, refresh/reselect và bảng chia sẻ; không giả định tài liệu mới đồng nghĩa integration đã nghiệm thu.

Các bước triển khai:

1. Spike nguồn và Google integration thật.

2. Core/SQLite/DPAPI/checkpoint, kiểm thử lỗi và restart.

3. WPF UI chọn ba thao tác, kết nối tài nguyên không nhập URL.

4. Nghiệm thu end-to-end: 10–20 link Douyin đại diện, Sheets bốn ô/validation/formula, upload thử Private, mất mạng/response/crash/sai kênh/quota.

5. Migration an toàn, installer và dọn code/phụ thuộc cũ; nghiệm thu Unlisted khi project đủ điều kiện.

Loại khỏi chương trình đích: Node/runtime Node, web public/*, local server/API routes/bridge/SSE, toàn bộ extension, debugger/clipboard/browser/profile ownership, scripts launcher browser, script mã hóa PowerShell, AI/model, tìm kiếm/review/ranking và UI nhập link bảng/kênh. Không mang chúng vào app chỉ để test cũ vẫn pass.

Không sao chép mã JS vào executable Windows. Những quy tắc nghiệp vụ đúng chuyển thành C# và test tương ứng; bỏ giả định/giới hạn không còn cần. Bản sao mã cũ chỉ phục vụ khôi phục, không là engine thứ hai của app.

Migration đọc bản sao state/checkpoint cũ, import cấu hình đích theo ID như ứng viên cần cấp quyền lại, file đã xác minh và ID YouTube dùng chống trùng. Job browser dở/chưa rõ upload không tự chạy lại bằng API. Không xóa video, checkpoint hay backup người dùng khi chuyển.

## 12. Kết quả thiết kế

Kiến trúc chốt: C#/.NET 10 + WPF + SQLite + OAuth/Picker/Google APIs + một adapter Douyin local + ffprobe. Giao diện không có link bảng/kênh; chỉ còn chọn tài nguyên, link nguồn và thông tin người dùng cần nhập.

Browser chỉ tham gia Google OAuth/Picker bên ngoài app; loopback listener chỉ sống trong phiên callback. Không cần extension, AI, Node server, web frontend, IPC hoặc dịch vụ nền riêng.

Đây là thiết kế; chưa có Windows binary mới hoặc bài nghiệm thu tài khoản thật. Bản thiết kế trước được sao lưu tại data/rebuild-backups/design-before-native-20261003/API-ARCHITECTURE.md.

Mốc Short/Long lưu bằng milliseconds, mặc định 300000, có ô nhập phút từ 1 giây đến 1440 phút. Mỗi job chụp mốc khi tạo/bổ sung Bảng theo đích được chọn; so sánh `duration <= threshold` cho Short, lớn hơn cho Long. Phân loại kênh dùng cùng mốc trên 10 video mới nhất. Schema 8 ngăn binary cũ bỏ qua mapping/mốc và ghi sai.

0.3.31: WindowsFileIdentity dùng kernel32 metadata/OpenFileById; MovedFolderFinder chạy Task.Run, giới hạn duyệt/hash, chỉ khi đường dẫn cũ mất. Windows Search COM là tùy chọn, lỗi thì duyệt các đích đã dùng. MovedDownloadDirectory tách Match/Apply để không hash lại bản được chọn. YouTube empty owner-ID lookup tự đánh dấu deleted theo lựa chọn người dùng.

0.3.31: MovedFolderFinder dùng DriveInfo để thử Path.Combine(root, FileStem(job.Name)) trên ổ local sẵn sàng, không enumerate root và không đọc tài khoản. Gộp ứng viên với index/known roots để phát hiện nhiều bản; hash một bản không đủ để bỏ qua kiểm tra các ứng viên khác. Drive-root provider có thể inject cho kiểm thử copy-delete khác volume.

0.3.31: JobHistory.Project lọc current action + state + search, nhóm workflow hoặc ngày tạo. WPF duy trì selected ID, snapshot bao gồm QueuePosition riêng vì trường này JsonIgnore, row virtualization và vùng cuộn cố định. Cột Short/Long chỉ bỏ trên history; Sheet/duration logic giữ nguyên. Core/UI cùng JobQueue.Order; không lưu thứ tự UI vào worker.


0.3.31: DouyinDownloadMetadata cho phép metadata của lượt tải trước khi receipt khớp job/source/attempt/directory và có media; MediaDownload vẫn dùng byte receipt của lượt mới. DouyinPublicReader thử feed độc lập sau web challenge và format rỗng, không gửi cookie tài khoản. Download không bắt buộc source_duration_ms; metadata-only vẫn bắt buộc. Cache thời lượng từ browser không được đánh dấu đã kiểm tra chất lượng tải. Account vẫn cuối; 429 dừng mọi fallback.

0.3.31: JobHistory.ProjectSelected có includeEmptyActions để lọc CurrentActions=None; nếu tích cùng action mask thì OR nhóm trống với nhóm có đủ actions. Cột ModeLabel trả string rỗng khi không còn phần nào. WPF startup thu gọn filters/details, scroll dọc details Visible (disabled nếu nội dung không tràn), không đổi trạng thái các mục khác.


## Bản 0.3.31: đọc phiên khách và cuộn chi tiết

App nhận diện cả dấu × dạng SVG không có nhãn của lời mời đăng nhập Douyin. Lời mời có thể mở lại khi chuyển đường dẫn; app tự đóng có giới hạn (tối đa 6 lần trong lượt đọc), rồi giữ cửa sổ cho người dùng nếu vẫn bị che. Không coi lời mời chưa đóng là bằng chứng bắt buộc dùng tài khoản. CAPTCHA vẫn chờ người dùng và không tự chuyển phiên.

Cửa sổ đọc chạy ngoài màn hình, không lấy focus/không hiện trên thanh tác vụ; WebView vẫn hoạt động để trang tiếp tục tạo yêu cầu. Chỉ đưa cửa sổ về màn hình khi cần thao tác. Đóng app/công việc vẫn đóng bộ đọc.

Cuộn chuột trong Chi tiết tiến trình và kết quả cuộn phần này trước; ở mép hoặc nội dung vừa khung thì cuộn trang ngoài. Thanh cuộn riêng chỉ hiện khi nội dung vượt khung, tránh thanh cuộn vô hiệu gây hiểu nhầm.

Nguồn công khai có thể trả 403 thiếu UIFID hoặc trả video đề xuất khác ID. App không nhận video đề xuất thay video nguồn. Phiên khách cũng có thể bị Douyin từ chối dù SDK đã gửi định danh; vẫn giữ tài khoản là phương án cuối cho dữ liệu bắt buộc. Không cam kết mọi link đều tải được bằng khách.

## 0.3.32 — HTTP video trước, WebView khi cần tương tác

Khôi phục Select web bổ sung poster, không đổi rendition video. Khôi phục renewal khách tối đa một lần/ngày sau body web 200 rỗng và các feed thất bại; lưu RefreshedAt trong guest-douyin, tương thích ticket Token/Expires cũ. Không renewal cho UIFID, 403, mạng hoặc 429. CAPTCHA có thể là JSON hay HTML HTTP 200. DouyinDataReader không mở WebView chỉ vì thiếu dữ liệu; guest verification không đủ metadata cũng không chuyển tài khoản. Metadata hợp lệ sau xác minh xóa trạng thái browser-required để những lượt sau lại thử HTTP. 429 giữ cooldown.

Detail ScrollViewer VerticalScrollBarVisibility=Visible, MaxHeight=170, thanh cuộn 20 px. Bánh xe ưu tiên detail, ở mép hoặc nội dung vừa khung chuyển trang ngoài. Fixture xác minh layout/chuột, không thay nghiệm thu tài khoản hoặc tải thật.

## 0.3.33 — chiều cao chi tiết cố định

Nội dung Expander chi tiết dùng ScrollViewer Height=170 (thay MaxHeight), giữ vùng hiển thị khi dữ liệu ngắn/dài hoặc chưa chọn công việc. Nội dung dài vẫn cuộn riêng; thu gọn Expander vẫn hoạt động độc lập. Chỉ thay WPF layout và kiểm tra giao diện, không thay logic Douyin/Google/queue.


## 0.3.34 — thanh cuộn theo độ dài nội dung

Chi tiết tiến trình và kết quả giữ Height=170, VerticalScrollBarVisibility=Auto. Nội dung vừa khung không hiện thanh cuộn; nội dung vượt khung hiện thanh cuộn dọc riêng rộng 20 px. Thu gọn/mở rộng và cuộn chuột giữ hành vi hiện có.


## 0.3.35 — yêu cầu lựa chọn và tiếp tục

JobIssue lưu mã nguyên nhân, action bị ảnh hưởng, dependency, RetryAt và ID yêu cầu trong payload DPAPI hiện có; tối đa 50 decision receipts mỗi job. Legacy NeedsAuth/NeedsAction/Failed được đọc và tạo issue khi khởi động. WaitingToRetry nằm cuối enum để giữ số trạng thái cũ; deadline được lưu và scheduler không giữ worker cho một yêu cầu đang chờ người dùng. Thử lại mạng/429 tối đa hai lần tự động, checkpoint được đối chiếu trước khi gửi lại. Authentication dependency gom UI và chặn API cùng credential đang thiếu quyền; rebind chỉ áp dụng đúng spreadsheet/channel gốc và job NeedsAuth, không tự chạy job Paused. Yêu cầu stale bị từ chối theo issue ID và lựa chọn hiện tại. Omit chỉ loại work chưa rõ ràng chưa gửi; không xóa kết quả, không bỏ unknown remote mutation. Upload inspection dùng query rỗng, kết quả đã có có thể liên kết sau kiểm tra đúng kênh. Thông báo modeless, bật lại từ badge, bring-forward một lần rồi bỏ Topmost; CAPTCHA giữ cửa sổ xác minh riêng.
