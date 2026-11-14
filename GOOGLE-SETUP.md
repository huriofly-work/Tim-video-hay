# Hướng dẫn cài đặt và Google thủ công — Tìm video hay 0.3.36

Douyin dùng phiên riêng của app, tách khỏi Google. Đăng nhập/xác minh khi cần tại Kết nối & cài đặt → Douyin và dữ liệu nâng cao → Mở Douyin trong app; phiên được giữ cho lần sau. Không nhập/xuất cookie thủ công. Xem [DOUYIN-SESSION.md](DOUYIN-SESSION.md).

Sau khi chọn bảng/tab, app tự đọc dropdown cột **Ngách** để bạn chọn trên màn hình Xử lý video. Nếu chưa có dropdown: mở Sheets, chọn vùng ô dưới tiêu đề Ngách, dùng **Dữ liệu → Xác thực dữ liệu** để tạo danh sách nhập trực tiếp hoặc danh sách từ vùng ô, rồi bấm **Làm mới ngách** trong app. Khi đổi bảng/tab, app tải danh sách của đích mới. Tên video tối đa 30 ký tự, tính cả dấu cách, cho mọi tổ hợp công việc.

Một mục **Xử lý video** chọn Tải / Bảng / YouTube, một lịch sử và một thư mục mặc định. Chỉ chọn Bảng thì không tải video; chọn Đăng tự chọn Tải. Đổi đích không thay công việc đã lưu; bổ sung/thực hiện lại hiển thị đích trước khi xác nhận.

Trong lịch sử, **Tạm dừng mục này** dừng riêng công việc; **Tạm dừng tất cả** dừng toàn bộ. **Xóa công việc** hỏi có xóa thêm thư mục video trên máy/dòng Sheets/video YouTube đã tạo hay không; chỉ hiện lựa chọn tương ứng với tiến độ thực tế sau khi dừng. Tất cả mặc định bỏ chọn. Phần không chọn được giữ; xóa thư mục gồm mọi tệp bên trong và không thể hoàn tác qua app. Lỗi xóa giữ mục trong lịch sử; Tiếp tục chỉ thử phần xóa chưa xong.

Ảnh bìa là phần tùy chọn: thiếu một/cả hai bìa, lỗi mạng hoặc hết ngân sách lấy ảnh thì app giữ video hợp lệ và tiếp tục các bước Google. Công việc cũ đang dừng vì ảnh bìa: mở bản mới, chọn đúng lịch sử rồi bấm **Tiếp tục / thử lại**. Khi cần, dùng Bổ sung → Tải lại ảnh bìa.

Lần đầu chọn xóa YouTube, app có thể mở trang cấp thêm quyền **youtube.force-ssl**; chọn đúng tài khoản/kênh gốc. Quyền upload/đọc cũ không đủ để xóa. Quyền mới được lưu cùng kết nối để lần sau không phải cấp lại, trừ khi bị thu hồi/hết hiệu lực. Nếu quản lý scope trong Cloud **Data Access**, bổ sung `https://www.googleapis.com/auth/youtube.force-ssl` khi cần chức năng xóa. Đối chiếu [videos.delete](https://developers.google.com/youtube/v3/docs/videos/delete). Không cần cấp quyền này khi chỉ tải hoặc đăng video. Upload không rõ ID phải kiểm tra Studio trước, app không tự đoán video để xóa.

Cập nhật 10/10/2026. Tài liệu dành cho người tự cài app, quản lý Google Cloud và đổi tài khoản/đích. Tên nút Google ghi bằng tiếng Anh; giao diện tiếng Việt có thể dùng tên tương đương.

## Mục lục

1. Trạng thái hiện tại và chọn đúng trường hợp

2. Cài app trên Windows hoặc chuyển máy

3. Kết nối bảng và kênh trong app

4. Thêm email Test users

5. Đổi email, bảng, tab hoặc kênh

6. Đổi tài khoản quản lý Google Cloud, giữ project

7. Tạo và thiết lập Google Cloud project mới từ đầu

8. Dùng OAuth client của project mới trong app

9. Chuyển Testing sang In production

10. Kiểm duyệt upload YouTube

11. Xử lý lỗi và thu hồi quyền

## 1. Trạng thái hiện tại và chọn đúng trường hợp

Bản chia sẻ không tự lấy project hoặc tài khoản cá nhân của người đóng gói. Bản chia sẻ không nhúng Desktop client. làm mục 7–8 để tạo project/Desktop client của bạn, nhập JSON trong Thiết lập Google nâng cao rồi kết nối. Chỉ tải Douyin không cần Google.

Đăng nhập tài khoản của chính bạn. Không cần tài khoản Google Cloud của nhà phát hành, không nhận token/cookie/lịch sử của họ. Project ở Testing cần chủ project thêm email của bạn vào Test users; video được đăng theo chế độ bạn chọn.

Ba loại tài khoản có thể khác nhau: tài khoản quản lý Cloud project, tài khoản có quyền sửa Sheets, tài khoản sở hữu/kết nối kênh YouTube. Đổi một loại không mặc nhiên phải đổi hai loại còn lại.

| Nhu cầu | Phần cần đọc | Có cần OAuth client mới? |

| --- | --- | --- |

| Đổi email dùng bảng/kênh | 4–5 | Không |

| Cài sang máy khác | 2–3 | Không, nếu dùng cùng project |

| Dùng email Google Cloud khác quản lý project cũ | 6 | Không |

| Không giữ project cũ, tạo project riêng bằng email Cloud mới | 7–8 | Có; đăng nhập lại và dùng hồ sơ dữ liệu riêng |

| Không muốn thêm Test user mỗi khi dùng email mới | 9 | Không; cần hoàn thiện điều kiện Publish của Google |

## 2. Cài app trên Windows hoặc chuyển máy

### Dùng bản portable hoặc cài cho tài khoản Windows

Hướng dẫn cài/cập nhật/gỡ đầy đủ: [INSTALLATION.md](INSTALLATION.md).

1. Giải nén nguyên gói ZIP chia sẻ sang thư mục riêng, giữ EXE/DLL/tools/tài liệu cùng nhau. Không chỉ chép một EXE, không gửi bản cá nhân hay hồ sơ dữ liệu cho người khác.

2. Mở TimVideoHay.exe để dùng portable, hoặc Cai-dat.cmd để cài và tạo shortcut. Không cần mã nguồn, Node/Python/.NET SDK hoặc quyền Administrator.

3. Bản cài nằm ở %LocalAppData%\Programs\TimVideoHay\0.3.36. Muốn gỡ: Go-cai-dat.cmd hoặc shortcut Gỡ Tìm video hay 0.3.35, mặc định giữ dữ liệu. Cập nhật cùng phiên bản: gỡ giữ dữ liệu rồi cài lại, không ghi đè app đang chạy.

4. Chọn thư mục trong Xử lý video; chỉ tích Tải video để thử trước khi kết nối Google.

5. Bản chia sẻ không nhúng Google client. Tạo project/Desktop client của chính bạn theo mục 7–8, nhập JSON vào app rồi kết nối bảng/kênh theo mục 3. Email mới trong Testing cần mục 4.

Muốn chạy PowerShell thủ công từ thư mục giải nén:

```powershell

powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1 -DesktopShortcut

powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Uninstall.ps1

```

### Dữ liệu khi chuyển máy

Dữ liệu app mặc định ở %LocalAppData%\TimVideoHay. Token/client/cookie/phiên upload dùng DPAPI, gắn với máy/tài khoản Windows; sao chép dữ liệu không giữ được quyền đăng nhập trên máy khác. Video/ảnh bìa có thể chép như file bình thường.

App hiện chưa có chức năng chuyển trọn lịch sử và công việc đang dở sang máy khác. Hoàn tất/đối soát công việc trên máy cũ, giữ hồ sơ cũ để tra cứu. Chép thư mục chương trình chuyển app; không tự di chuyển công việc hay quyền Google.

## 3. Kết nối bảng và kênh trong app

### Chuẩn bị bảng

Email chọn bảng phải có quyền Editor. Bảng thuộc email khác: dùng trực tiếp email đó hoặc để chủ bảng chia sẻ quyền sửa cho email bạn sẽ chọn trong app.

Tab cần một hàng tiêu đề trong 30 dòng đầu. Trong app bấm **Chọn cột video**, chọn hàng tiêu đề (1–30), bấm Đọc cột từ bảng rồi gán bốn loại thông tin **Link video mẫu, Tên video, Ngách, Loại video** vào bốn cột khác nhau. Danh sách hiển thị chữ cột và tiêu đề thực tế; tên không bắt buộc trùng nhãn trên app. Làm tương tự ở **Chọn cột kênh nguồn** cho năm loại thông tin của tab nguồn. Cột không cần liền nhau hoặc theo thứ tự. Đổi tên tiêu đề không làm mất lựa chọn; chèn/xóa/chuyển cột cần chọn lại. Giữ các cột khác như cũ. Cấu hình cũ chưa gán cột vẫn dùng tự nhận diện theo tên cũ. App ghi sau hàng dữ liệu cuối, không tự sửa cấu trúc/dropdown.

Nếu có dropdown Ngách, thêm ngách bạn sẽ nhập. Loại video cần nhãn Short/Shorts/Video ngắn và Long/Video dài. Mốc mặc định ≤300 giây là Short, >300 giây là Long; đổi ở Kết nối & cài đặt → Phân loại nội dung; mốc mới áp dụng cho công việc mới và cả phân loại kênh; đây là nhãn của bảng, không ép YouTube Shorts. Trong cùng file cần tab **Kênh nguồn** với năm cột **Link kênh, Tên kênh, Đăng ký, Ngách, Dạng nội dung**; Dạng nội dung chấp nhận **Short, Long, All**. Vùng ghi không được có công thức/ô gộp/bảo vệ cản ghi. App cập nhật cùng dòng kênh theo sec_uid; xóa dòng video giữ kênh. Không sửa/sắp xếp tab trong lúc app ghi.

### Chọn bảng → chọn kênh → xác nhận

1. Kết nối & cài đặt → **Kết nối Google**.

2. Bấm **Chọn / đổi bảng Google Sheets**. Google mở trong trình duyệt mặc định; chọn đúng email có quyền sửa. Nếu Testing, email phải có trong Test users.

3. Đọc quyền Google và cấp quyền nếu đúng app của bạn. Trong Picker, chọn đúng một bảng.

4. Quay về app, kiểm tra tên bảng, chọn **Tab video** và **Tab kênh nguồn** khác nhau trong cùng file. Dùng **Chọn cột video / Chọn cột kênh nguồn** nếu tiêu đề khác tên mặc định → **Tiếp tục**. App kiểm tra bốn cột video trước khi sang bước kênh; năm cột kênh được kiểm tra trước công việc ghi bảng.

5. Bấm **Chọn / đổi kênh YouTube**, chọn email/kênh thương hiệu muốn đăng. Có thể khác email của bảng. Cấp các quyền đọc kênh và upload mà app yêu cầu.

6. Quay về app, đọc tên kênh và bấm **Mở kênh để kiểm tra** → **Tiếp tục**.

7. Đối chiếu tên bảng, tab và kênh → **Xác nhận kết nối**.

Chỉ bước xác nhận cuối lưu đích của trình hướng dẫn. Hủy giữ đích cũ, dọn token mới của lượt thiết lập khỏi máy. Hai lượt OAuth là có chủ đích để bảng/kênh dùng hai tài khoản độc lập. Mỗi lượt chờ tối đa 5 phút; quá thời gian phải bắt đầu lại từ app.

Picker cấp quyền cho bảng đã chọn; chỉ đăng nhập chưa đủ. App dùng drive.file thay vì đọc toàn bộ Drive. [Picker Desktop](https://developers.google.com/workspace/drive/picker/guides/desktop-mobile-picker).

### Thử một công việc

Bản 0.3.35 **không chờ YouTube xử lý video/chất lượng**. Có ID và xác nhận đúng kênh, chế độ, không dành cho trẻ em là báo Hoàn tất; YouTube xử lý tiếp ở phía dịch vụ. Chỉ khi thiếu thông tin xác nhận hoặc lỗi mạng, app hiện Chờ xác nhận và tự đọc lại; không cần Đối soát/Tiếp tục cho trường hợp chờ này. Để app mở hoặc xuống khay hệ thống. Thoát/mở lại sẽ tự tiếp tục xác nhận ID đã lưu; Dừng chủ động tạm dừng. Bản mới nhận diện đúng lỗi timeout xử lý của bản cũ để xác nhận lại ngay, không tự tiếp tục các lỗi cần kiểm tra khác.

Xử lý video → nhập link Douyin, tên, ngách. Trong Tùy chọn đăng nâng cao, chọn chế độ đăng mong muốn; mặc định mới là Không công khai, chế độ đã lưu được giữ. Không cần ô xác nhận bổ sung. Bắt đầu xử lý, rồi mở bảng/YouTube Studio kiểm tra dòng video và dòng kênh nguồn, kênh YouTube, tiêu đề, chế độ và không dành cho trẻ em. Nếu đã ghi/upload một phần, dùng Tiếp tục / thử lại hoặc Đối soát… trước khi tạo job mới.

## 4. Thêm email Test users

Chỉ cần trong Testing. Thêm mỗi email mới một lần; có thể thêm sẵn nhiều email. Email Sheets và email YouTube đều cần có nếu dùng hai tài khoản.

1. Đăng nhập Cloud bằng tài khoản có quyền quản lý OAuth project. Email dùng app không cần được cấp quyền quản trị Cloud.

2. Mở [Audience của project](https://console.cloud.google.com/auth/audience). Với project khác, chọn project đó trước.

3. Kiểm tra tên/ID project; tìm **Test users → Add users**.

4. Nhập email → **Enter** để thành một thẻ. Lặp lại nếu thêm nhiều email.

5. Bấm **Save**, chờ biểu mẫu đóng và email hiện trong bảng. Nếu chưa lưu, kiểm tra thông báo; có thể Tab đến Save rồi nhấn Space để kích hoạt nút.

6. **Reload trang.** Chỉ coi hoàn tất khi email còn sau reload.

7. Đóng trang OAuth lỗi, hủy lượt chờ cũ trong app, kết nối lại bằng đúng email.

Testing tối đa 100 Test users. Với các scope chức năng này, quyền/refresh token hết hạn sau 7 ngày kể từ lúc cấp quyền; đăng nhập lại, không thêm lại email. [Quy định Testing](https://support.google.com/cloud/answer/15549945?hl=en).

## 5. Đổi email, bảng, tab hoặc kênh

| Muốn đổi | Thao tác trong Kết nối & cài đặt | Kiểm tra |

| --- | --- | --- |

| Bảng hoặc email của bảng | Chọn / đổi bảng → chọn email/bảng | Chọn đúng tab bên dưới; app ban đầu chọn tab đầu tiên |

| Chỉ tab cùng bảng | Chọn tab dữ liệu trong danh sách | Đủ bốn cột |

| Kênh hoặc email của kênh | Kết nối / đổi kênh | Tên kênh và Mở kênh đúng |

| Cả hai | Kết nối Google, làm ba bước | Xác nhận bảng/tab/kênh |

Không cần JSON/project mới. Đổi riêng bảng giữ kênh; đổi riêng kênh giữ bảng. Quyền được mời trong Studio không tự bảo đảm API truy cập đúng kênh; nếu sai, hủy và OAuth lại đúng danh tính.

Đích mới áp dụng cho công việc mới. Job cũ giữ snapshot đích lúc tạo. Khi cần cấp lại quyền cho job cũ: chọn job → **Kết nối lại đích công việc** → chọn đúng bảng/kênh gốc. App kiểm tra ID; thao tác này không chuyển job cũ sang đích khác.

## 6. Đổi tài khoản quản lý Google Cloud, giữ project

Nếu chỉ muốn dùng email Cloud khác để quản lý, giữ project/client giúp tránh đổi cấu hình app. Quyền Cloud không phải quyền của bảng/kênh.

1. Đăng nhập email đang có quyền quản lý project, chọn đúng TimVideoHay.

2. Vào **IAM & Admin → IAM → Grant access**.

3. Nhập email Google Cloud mới của chính bạn vào New principals. Nếu muốn chuyển quyền quản lý toàn project, chọn **Basic → Owner**. Owner là quyền quản trị rộng; chỉ cấp cho tài khoản bạn chủ đích dùng để quản lý, không cấp cho email chỉ cần chạy app.

4. Save; hoàn tất xác thực/xác nhận/chấp nhận lời mời nếu Google yêu cầu.

5. Đăng nhập Cloud bằng email mới, kiểm tra truy cập được đúng Project ID, Clients, Audience và APIs. Nếu chưa truy cập được, giữ email cũ và xử lý lời mời/quyền trước.

6. Trong Branding, cập nhật email hỗ trợ/liên hệ nếu cần và kiểm tra sau khi lưu.

7. Chỉ khi tài khoản mới quản lý được đầy đủ, mới cân nhắc gỡ quyền tài khoản cũ trong IAM. Không gỡ chủ sở hữu đang dùng trước khi kiểm tra tài khoản mới.

Project ID, client ID và quyền app không cần thay chỉ vì thêm tài khoản quản lý. Bảng/kênh không đổi chủ theo IAM. Nếu project có Billing, quyền Billing là phần riêng cần xem xét; project hiện tại chưa bật Billing.

Nếu không truy cập được email/project cũ, email mới không thể tự lấy quyền bằng thao tác trên. Dùng khôi phục tài khoản hoặc tạo project mới theo mục 7–8.

[IAM chính thức](https://docs.cloud.google.com/iam/docs/granting-changing-revoking-access). Nếu thực sự chuyển quyền kiểm soát project YouTube API cho chủ thể khác, kiểm tra yêu cầu Change of Control tại [hướng dẫn YouTube](https://developers.google.com/youtube/v3/guides/quota_and_compliance_audits).

## 7. Tạo và thiết lập Google Cloud project mới từ đầu

Làm khi cần project độc lập, không giữ project cũ. Đổi tài khoản Google Cloud không bắt buộc tạo mới; mục 6 là lựa chọn giữ cấu hình.

### 7.1. Tạo project

1. Vào [Google Cloud Console](https://console.cloud.google.com/) bằng email Cloud mới. Kiểm tra avatar/email góc trên.

2. Bộ chọn project → **New project** → nhập tên dễ nhận biết → chọn Organization/Location theo tài khoản (Gmail cá nhân thường không có tổ chức) → **Create**.

3. Chọn project vừa tạo. Ghi lại Project ID và Project number trong thông tin project. Tên hiển thị có thể giống project khác; ID mới là giá trị đối chiếu.

4. Reload hoặc mở lại project, kiểm tra đúng ID trước khi cấu hình các bước sau. [Tạo project](https://docs.cloud.google.com/resource-manager/docs/creating-managing-projects).

### 7.2. Bật bốn API

**APIs & Services → Library**. Tìm và mở lần lượt:

- Google Sheets API — sheets.googleapis.com

- Google Drive API — drive.googleapis.com

- YouTube Data API v3 — youtube.googleapis.com

- Google Picker API — picker.googleapis.com

Với từng dịch vụ, bấm **Enable**, đợi đến khi hiện Enabled/Manage. Trong **Enabled APIs & services**, kiểm tra đủ bốn tên thuộc project mới. Luồng này không cần API key hoặc Apps Script. Không kích hoạt Billing chỉ để làm theo hướng dẫn OAuth này; nếu Google có yêu cầu khác, đọc đúng dịch vụ/tính năng đang chọn trước.

### 7.3. Thiết lập Google Auth Platform

1. Mở **Google Auth Platform → Get started** nếu chưa khởi tạo.

2. App name: **Tìm video hay**; chọn email hỗ trợ và email liên hệ nhà phát triển bạn theo dõi.

3. Audience: **External** cho Gmail cá nhân. Internal dành cho tài khoản trong tổ chức Workspace/Cloud Identity phù hợp.

4. Đọc và tự chấp nhận cam kết/chính sách Google đang hiển thị để hoàn tất setup.

5. Trong Branding, lưu thông tin và tải lại để kiểm tra. Trong Audience, giữ Testing và thêm email dùng bảng/kênh theo mục 4.

[Branding](https://support.google.com/cloud/answer/15549049?hl=en) · [External/Internal](https://support.google.com/cloud/answer/15549945?hl=en).

### 7.4. Khai báo Data Access

**Google Auth Platform → Data Access → Add or remove scopes**. Chọn hoặc nhập ba scope dưới đây, cập nhật và lưu; không thêm quyền toàn bộ Drive/Sheets/YouTube khi app không dùng.

| Scope | Công dụng |

| --- | --- |

| https://www.googleapis.com/auth/drive.file | Bảng đã chọn qua Picker, đọc metadata và ghi bảng đó |

| https://www.googleapis.com/auth/youtube.upload | Upload vào kênh đã cấp quyền |

| https://www.googleapis.com/auth/youtube.readonly | Xác minh kênh/video |

| https://www.googleapis.com/auth/youtube.force-ssl | Tùy chọn: xóa video theo xác nhận người dùng; xin thêm khi chọn chức năng xóa |

Khai báo scope không cấp quyền dữ liệu; người dùng vẫn OAuth từ app. Luồng kết nối thông thường dùng ba scope cơ bản, quyền xóa xin riêng khi chọn xóa video. Chưa nghiệm thu Data Access/quyền xóa trên project thật.

### 7.5. Tạo Desktop client và lấy JSON

1. **Google Auth Platform → Clients → Create client**.

2. Application type: **Desktop app**; tên **TimVideoHay Windows** hoặc tên riêng rõ ràng.

3. Không chọn đánh dấu AI agent nếu có: app hiện không dùng AI.

4. Create → **Download JSON**; nếu hộp đã đóng, mở trang chi tiết client để tìm tải JSON.

5. Lưu file riêng, không gửi client secret/token/cookie vào chat.

6. Reload danh sách Clients, kiểm tra client còn và Type là Desktop. Không chọn Web application hoặc Service account, không nhập redirect URI của Web app.

[Quản lý OAuth client](https://support.google.com/cloud/answer/15549257?hl=en) · [OAuth Desktop](https://developers.google.com/identity/protocols/oauth2/native-app).

### 7.6. Kiểm tra trước khi kết nối

Đúng project mới; đủ bốn API; External/Testing; email có trong Test users sau reload; client Desktop tồn tại; JSON của đúng client. Sau đó làm mục 8. Chưa chuyển In production hoặc xin audit chỉ để thử đăng Riêng tư.

## 8. Dùng OAuth client của project mới trong app

**Token project cũ không dùng được với client mới.** Bản hiện tại giữ client đã lưu để bảo vệ quyền/job cũ và từ chối nhập client ID khác vào cùng hồ sơ. Ngắt bảng/kênh không tự xóa client đã lưu.

### Cách thủ công hiện có: hồ sơ dữ liệu riêng

1. Hoàn tất/đối soát các job đang chạy ở project cũ; thoát app. Giữ dữ liệu cũ để tiếp tục/tra cứu, không xóa file DPAPI/database để ép thay project.

2. Mở PowerShell, chạy app với một đường dẫn dữ liệu **mới, chưa sử dụng**:

```powershell

& 'D:\TimVideoHay\artifacts\windows-x64\TimVideoHay.exe' --data-dir "$env:LOCALAPPDATA\TimVideoHay-ProjectMoi"

```

3. Trong hồ sơ mới, **trước khi bấm Kết nối Google**, vào Kết nối & cài đặt → Thiết lập Google nâng cao → **Nhập cấu hình Google Desktop** → chọn JSON project mới.

4. Chọn thư mục tải rồi kết nối bảng/kênh theo mục 3. Đăng nhập lại; không dùng token của project cũ.

5. Những lần sau mở bằng cùng lệnh/đường dẫn dữ liệu. Có thể tạo shortcut đến EXE, thêm phần Arguments: `--data-dir "%LocalAppData%\TimVideoHay-ProjectMoi"`, dùng đường dẫn thật của bạn.

6. Mo-ung-dung.cmd/shortcut mặc định vẫn mở hồ sơ mặc định. App chưa có giao diện chọn/chuyển hồ sơ; tránh nhầm hai hồ sơ. Job mới bắt đầu ở lịch sử mới, không chuyển job cũ sang project mới.

Bản chia sẻ không có client nhúng; hồ sơ sạch cho phép nhập client của bạn trước lần kết nối đầu tiên. Bản cá nhân có client nhúng vẫn cho phép nhập client riêng khi chưa lưu quyền. Client riêng đã lưu sẽ được ưu tiên. Nếu báo đã có client khác, đường dẫn hồ sơ đã từng được dùng; chọn một đường dẫn sạch khác, không xóa dữ liệu cũ.

### Đóng gói client mới để chuyển máy

Không cần build nếu đã nhập JSON theo cách trên. Nếu muốn client mới đi sẵn trong bản app, người phát triển cần .NET 10 SDK và công cụ đã chuẩn bị. Trong thư mục mã nguồn, chạy:

```powershell

./scripts/Build-Native.ps1 -GoogleClientPath 'C:\duong-dan\desktop-client-moi.json' -OutputDirectory 'D:\TimVideoHay-ProjectMoi'

```

Build/test/publish vào thư mục mới, chỉ nhúng client ID/secret; không nhúng token. Giữ nguyên bản và hồ sơ cũ. Bản mới vẫn cần hồ sơ dữ liệu riêng khi dùng trên Windows đang có client cũ; đổi binary không tự đổi client đã lưu. Sang Windows/máy sạch, dùng bản đóng gói mới rồi đăng nhập như mục 3.

## 9. Chuyển Testing sang In production

Chỉ làm sau khi thử thành công kết nối/ghi bảng/upload. In production bỏ yêu cầu thêm Test users nhưng không tự xác minh OAuth app.

1. Data Access: rà đúng các scope mục 7.4.

2. Branding: hoàn thiện các trường Google yêu cầu. Nếu Publish app bị khóa, kiểm tra và hoàn thiện các mục Google yêu cầu trong Branding.

3. Nếu Google yêu cầu homepage/privacy policy/terms/domain, cung cấp trang thật mô tả app và dữ liệu, khai báo domain hợp lệ; xem lỗi trong Branding/Verification Center. Không điền URL giả hoặc coi ngoại lệ cá nhân là bỏ được mọi trường bắt buộc.

4. Audience → khi **Publish app** khả dụng, đọc nội dung xác nhận rồi áp dụng.

5. Reload và kiểm tra Publishing status là **In production**.

6. Kết nối lại từ app để thử quyền mới; không giả định token cấp lúc Testing tự hết mọi giới hạn cũ.

Google có ngoại lệ không bắt buộc xác minh OAuth cho app dùng cá nhân dưới 100 người, nhưng vẫn có cảnh báo chưa xác minh/giới hạn người dùng. Nếu Google chặn cụ thể, xử lý theo lỗi và Verification Center. [Audience](https://support.google.com/cloud/answer/15549945?hl=en) · [Ngoại lệ cá nhân](https://support.google.com/cloud/answer/13464323?hl=en).

## 10. OAuth verification và YouTube API audit

Hai quy trình khác nhau. Theo tài liệu [videos.insert](https://developers.google.com/youtube/v3/docs/videos/insert) kiểm tra ngày 10/10/2026, upload từ project API chưa xác minh **không bị giới hạn chỉ xem Riêng tư**. Không cần audit chỉ để chọn Không công khai. App gửi đúng chế độ người dùng chọn, nhận video ID và kiểm tra trạng thái; nếu khác yêu cầu, giữ ID và báo để kiểm tra trên Studio, không đăng lặp lại.

Audit về tuân thủ/quota vẫn có thể được Google yêu cầu. Chỉ thực hiện theo yêu cầu thực tế hoặc khi cần thêm quota, xem [hướng dẫn audit](https://developers.google.com/youtube/v3/guides/quota_and_compliance_audits). Testing/Test users và thời hạn token OAuth là vấn đề cấp quyền riêng, vẫn áp dụng.

## 11. Xử lý lỗi và thu hồi quyền

| Hiện tượng | Kiểm tra / cách xử lý |

| --- | --- |

| 403 access_denied, chỉ người kiểm thử | Đúng project/email; thêm Test users và reload theo mục 4; hủy phiên cũ rồi OAuth lại |

| Email đã thêm nhưng vẫn chặn | Đối chiếu client/project trong chi tiết lỗi, email thực chọn, hạn chế của tài khoản; không tự kết luận mọi 403 do Test users |

| Cảnh báo chưa xác minh | Đối chiếu đúng app của bạn, đọc quyền trước khi tự quyết định tiếp tục; nếu không cho tiếp tục, xử lý lỗi cụ thể |

| App chờ/không quay lại/quá 5 phút | Hủy và bắt đầu lại từ app, giữ app mở; trình duyệt trả về 127.0.0.1 của phiên hiện tại; không dùng trang OAuth cũ |

| Không thấy bảng | Đúng email, quyền Editor, tệp là Google Sheets; chọn lại bằng Picker |

| Thiếu cột/dropdown không hợp lệ | Kiểm tra đúng tab, gán lại cột; thêm lựa chọn dropdown trên bảng rồi Tiếp tục |

| Hết dòng trống/ô gộp/bảo vệ | Thêm dòng hoặc dùng vùng/tab có thể ghi, không chỉnh đồng thời khi app ghi |

| API chưa bật/403 dịch vụ | Kiểm tra Enabled APIs đúng project và lý do lỗi; kiểm tra quyền bảng/kênh |

| Không có kênh hoặc sai tên | OAuth đúng email/kênh thương hiệu; kiểm tra Mở kênh; quyền được mời Studio có thể không dùng được cho API |

| Cứ thêm video hoặc mở lại là phải kết nối | Dùng bản 0.3.37 trở lên. Kết nối lại đúng đích công việc sẽ khôi phục quyền tại khóa dùng chung, nên cấu hình mặc định và các công việc dùng quyền đó cùng được sửa. Nếu quyền cũ đã bị Google thu hồi, cần cấp lại một lần |
| Hết quyền khoảng 7 ngày | Testing: OAuth lại; job cũ dùng Kết nối lại đích công việc. Không thêm lại email |

| Client khác | Đổi email dùng nút đổi đích; đổi project dùng hồ sơ mới theo mục 8 |

| Upload bị Private | Audit YouTube, không chỉ Publish OAuth; xem mục 10 |

| Chờ xác nhận | Chỉ thiếu thông tin xác nhận/lỗi mạng: app tự đọc lại theo giờ hiển thị, không upload lại. Không phải chờ xử lý chất lượng YouTube |

| Mất mạng/không rõ đã ghi hoặc upload | Kiểm tra bảng/Studio rồi Tiếp tục hoặc Đối soát; không tạo job trùng trước khi kiểm tra |

| Quota/429 | Kiểm tra quota dịch vụ và thời gian phục hồi; không tạo project khác để né quota |

Khi cần hỗ trợ, ghi lại bước, mã/nội dung lỗi, email dùng và tên đích. Che token, authorization code, client secret và cookie trong ảnh/log.

**Ngắt local:** Kết nối & cài đặt → Ngắt kết nối bảng hoặc Ngắt kết nối YouTube. Không thu hồi toàn bộ quyền trên Google.

**Thu hồi trên Google:** mở [Quyền kết nối Google](https://myaccount.google.com/connections) bằng đúng email → tìm ứng dụng → xem quyền → chọn xóa/thu hồi và đọc phạm vi ảnh hưởng trước khi xác nhận. Thu hồi cùng project có thể khiến cả Sheets và YouTube của tài khoản đó cần kết nối lại. [Phạm vi thu hồi OAuth](https://developers.google.com/identity/protocols/oauth2/native-app).

Ngắt/thu hồi không tự xóa video/ảnh local, dòng bảng hay video đã đăng.

## Phạm vi kiểm chứng

Các thao tác app đối chiếu với mã/giao diện 0.3.35; quy định Google kiểm tra ngày cập nhật. Hướng dẫn cài/chuyển Cloud project không phải bằng chứng đã chạy thành công mọi trường hợp. Chưa nghiệm thu OAuth/Sheets/upload thật hoặc chuyển project thật. Khi Google đổi giao diện/yêu cầu, ưu tiên thông báo Console và nguồn chính thức được liên kết trong từng mục.

