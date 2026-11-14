# Cài đặt, cập nhật và gỡ Tìm video hay 0.3.45

Cập nhật 06/10/2026. Tài liệu cho gói portable, không cần mã nguồn. Bản Windows x64 tự chứa runtime và công cụ, chưa ký số. Không cần cài Node/Python/.NET SDK hoặc quyền Administrator. Chỉ mở gói từ nguồn bạn tin cậy; không tắt Defender/SmartScreen để chạy app.

## 1. Chọn cách sử dụng

| Cách | Thao tác | Khi gỡ |

| --- | --- | --- |

| Portable | Giải nén rồi chạy TimVideoHay.exe | Thoát app, xóa riêng thư mục đã giải nén; dữ liệu app vẫn giữ |

| Cài cho tài khoản Windows | Giải nén rồi chạy Cai-dat.cmd | Chạy Go-cai-dat.cmd hoặc shortcut Gỡ Tìm video hay |

Gói vẫn dùng file EXE. Cai-dat.cmd cài từ gói portable, chưa phải Setup.exe/MSI và chưa đăng ký trong Installed apps của Windows. Dùng script/shortcut gỡ bên dưới.

## 2. Cài lần đầu

1. Giải nén **toàn bộ ZIP** vào thư mục riêng, ví dụ D:\TimVideoHay. Không chạy bên trong cửa sổ ZIP; không chỉ chép một EXE.

2. Dùng portable: mở TimVideoHay.exe. Muốn cài: mở **Cai-dat.cmd**. Script kiểm tra file với release-manifest.json rồi sao chép; không tự chạy app.

3. Chương trình được cài ở `%LocalAppData%\Programs\TimVideoHay\0.3.45`. Có shortcut **Tìm video hay** ở Start Menu/desktop và **Gỡ Tìm video hay 0.3.45** ở Start Menu, riêng cho tài khoản Windows hiện tại.

4. Mở app, chọn thư mục tại Xử lý video, chỉ tích Tải video để thử một link trước.

5. Sheets/YouTube: xem [GOOGLE-SETUP.md](GOOGLE-SETUP.md), mục 7–8 để tạo project/Desktop client riêng, rồi kết nối bảng/tab và kênh trong app. Bản chia sẻ không có Google client của người đóng gói; tải riêng không cần Google.

6. Douyin ưu tiên phiên của app và nguồn tự động; khi cần đăng nhập/xác minh, làm [DOUYIN-SESSION.md](DOUYIN-SESSION.md). Không xuất cookie hoặc cài extension. Nếu chưa có WebView2 Runtime, xem mục 7.

Không muốn shortcut desktop: mở PowerShell tại thư mục giải nén và chạy:

```powershell

powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1

```

Thêm `-NoShortcuts` nếu không muốn tạo shortcut nào. Sử dụng app: [README.md](README.md). Bảo mật: [SECURITY.md](SECURITY.md).

## 3. Cập nhật hoặc cài lại

1. Hoàn tất công việc hoặc tạm dừng/lưu. **Thoát app từ khay hệ thống**; × có thể chỉ ẩn app khi đang có hoạt động.

2. Bản đã cài: gỡ bản cũ theo mục 4, **giữ dữ liệu mặc định**, rồi cài bản mới. Installer từ chối ghi đè phiên bản đã tồn tại; gỡ giữ dữ liệu rồi cài lại.

3. Portable: giải nén bản mới vào thư mục mới rồi chạy EXE mới. Không chép đè thư mục đang chạy hoặc trộn DLL hai bản.

4. Cùng máy/tài khoản Windows, hồ sơ mặc định được dùng lại. App nâng schema khi cần; sau nâng schema không dùng app cũ mở lại hồ sơ đó. Token hết hạn thì đăng nhập lại.

5. Gỡ một bản cũ chỉ xóa shortcut nếu shortcut vẫn trỏ đúng bản đang gỡ; giữ shortcut đang trỏ tới bản mới hơn.

## 4. Gỡ và giữ dữ liệu (mặc định)

1. Thoát app từ khay hệ thống. Script từ chối nếu bản cần gỡ còn chạy; không tự giết tiến trình/xóa công việc.

2. Mở **Gỡ Tìm video hay 0.3.45** ở Start Menu, hoặc **Go-cai-dat.cmd** trong thư mục giải nén/bản cài.

3. PowerShell hỏi xác nhận: nhập **Y** rồi Enter để gỡ, **N** để hủy.

4. Xóa file thuộc bản cài và shortcut đúng phiên bản. **Giữ lịch sử, cài đặt, cookie, kết nối Google tại `%LocalAppData%\TimVideoHay`.** Cài lại để dùng tiếp; token hết hạn vẫn cần cấp quyền lại.

5. Không xóa video/ảnh đã tải, dòng Sheets hoặc video YouTube. File khác bạn tự đặt trong thư mục cài, không nằm trong manifest, cũng được giữ; thư mục có thể còn lại.

Nếu shortcut mất hoặc bản cũ chưa có uninstaller, mở PowerShell tại thư mục giải nén gói mới:

```powershell

powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Uninstall.ps1

```

Script của gói mới có thể gỡ bản đã cài cùng số phiên bản 0.3.35, kể cả bản cũ chưa có Uninstall.ps1. Nó dùng manifest của **bản đã cài**, không xóa mã nguồn hoặc thư mục portable trên máy phát triển.

## 5. Gỡ và xóa hồ sơ mặc định

Chỉ chọn khi không còn cần lịch sử/công việc đang dở. Xóa hồ sơ làm mất checkpoint; không tạo công việc mới đăng lại khi kết quả lần trước chưa rõ.

Thoát **mọi bản Tìm video hay**, mở PowerShell tại thư mục giải nén:

```powershell

powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Uninstall.ps1 -RemoveUserData

```

Đọc câu xác nhận rồi nhập Y để tiếp tục. Xóa database lịch sử/cài đặt, credential/cookie/session mã hóa, log và marker được nhận diện trong **hồ sơ mặc định**. Giữ media, bìa backup và file khác, kể cả khi nằm trong hồ sơ. Không xóa video/ảnh ở thư mục tải, dòng Sheets hoặc video YouTube.

Hồ sơ riêng mở bằng `--data-dir` không tự bị xóa. Cookie Netscape/JSON Google gốc bạn tự lưu cũng không bị xóa. Muốn chỉ xóa hồ sơ sau khi đã gỡ app, vẫn chạy lệnh này từ gói giải nén.

Gỡ app/xóa token local **không tự thu hồi quyền Google**. Muốn thu hồi: mở https://myaccount.google.com/connections, chọn ứng dụng và xóa quyền. Không cần xóa Cloud project nếu vẫn muốn dùng lại. Xóa dữ liệu Google phải làm riêng bằng thao tác bạn chủ động chọn.

## 6. Đổi máy/tài khoản và xử lý lỗi

- **Máy mới/người khác:** chỉ gửi ZIP chia sẻ sạch, cài mới rồi kết nối Google lại. Không gửi hồ sơ `%LocalAppData%\TimVideoHay`, cookie, lịch sử hoặc bản cá nhân có bundle Google. DPAPI gắn với máy/tài khoản Windows, không chuyển nguyên database/credential. Video/ảnh sao chép bình thường.

- **Đổi email/bảng/kênh/project:** xem mục 4–8 của [GOOGLE-SETUP.md](GOOGLE-SETUP.md). Bảng và kênh có thể dùng hai tài khoản khác nhau. Đổi project khi còn công việc dở cần hồ sơ riêng, không ghi đè credential cũ.

- **Checksum/thiếu DLL:** giải nén lại gói nguyên vẹn vào thư mục mới; không sửa manifest để bỏ qua lỗi.

- **Phiên bản đã cài:** gỡ giữ dữ liệu rồi cài lại, không ghi đè file đang dùng.

- **App đang chạy:** thoát từ khay hệ thống rồi thử lại; script không tự dừng tải/upload.

- **Junction/symlink/manifest lỗi:** script dừng trước khi gỡ. Kiểm tra đường dẫn; không bỏ kiểm tra hoặc xóa cả LocalAppData.

- **Cài một phần do hết dung lượng/quyền file:** giữ hồ sơ, giải quyết lỗi và kiểm tra riêng thư mục phiên bản trong Programs/TimVideoHay. Không xóa thư mục video/hồ sơ để sửa lỗi cài.

- **Không thấy Installed apps của Windows:** bản portable/script dùng shortcut gỡ riêng, không có Setup.exe/MSI; làm mục 4.

Cài/gỡ được kiểm tra trong hồ sơ thử riêng; chưa thay thế nghiệm thu trên Windows sạch hoặc mọi cấu hình Windows.

## 7. Microsoft Edge WebView2 Runtime

App tự chứa .NET; bộ đọc Douyin cần WebView2 Evergreen Runtime của Microsoft. Windows 11/máy có Edge thường đã có, app dùng lại. Không đóng gói một trình duyệt Chromium riêng. Nếu app báo thiếu Runtime: tải **Evergreen Standalone Installer x64** tại https://developer.microsoft.com/microsoft-edge/webview2/ và cài theo hướng dẫn Microsoft, rồi mở lại app. Không cài Runtime từ website không xác định. Runtime tự cập nhật độc lập với app.

Gỡ mặc định giữ cả phiên Douyin. `-RemoveUserData` xóa douyin-browser của hồ sơ mặc định sau kiểm tra đường dẫn/link; nếu phát hiện video/ảnh trong đó, giữ lại và báo để chuyển file ra ngoài rồi chạy lại. Gỡ app không gỡ WebView2 vì ứng dụng khác có thể cần Runtime.
