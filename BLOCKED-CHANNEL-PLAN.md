# Kế hoạch: tab Kênh block

Trạng thái: đã triển khai ngày 16/11/2026 (Issue #18); test fixture và smoke đạt, chưa nghiệm thu trên bảng Google thật. Đọc `AGENTS.md` và `docs/HANDOFF.md` trước khi sửa.

## Mục tiêu

Trong cùng file Google Sheets có tab **Kênh block** liệt kê các kênh Douyin không được lấy video. Mỗi công việc mới (và mỗi lần chạy tiếp công việc còn dở) phải kiểm tra kênh của video trước mọi thao tác tải, ghi bảng hay đăng YouTube. Nếu video thuộc kênh bị block thì dừng công việc, báo rõ và không làm thao tác nào.

## Quyết định đã chốt

| Câu hỏi | Quyết định |
| --- | --- |
| Tab chứa gì | Cột bắt buộc **Link kênh** dạng `https://www.douyin.com/user/<sec_uid>`. Có thể có cột khác (vd. Ghi chú), app bỏ qua. So khớp chính xác theo `sec_uid`, cùng quy tắc với `SheetLayout.FindChannel`. |
| Phạm vi kiểm tra | Mọi công việc khi app đã kết nối bảng Google, kể cả công việc chỉ Tải hoặc Tải + YouTube. |
| Khi bị block | Dừng ở trạng thái Cần xử lý, lỗi loại mới `JobIssueKind.BlockedChannel`, thông báo "Video thuộc kênh bị block (dòng N trong tab Kênh block). App không tải, ghi bảng hay đăng video này." Công việc vẫn nằm trong lịch sử; người dùng xóa mục hoặc bỏ kênh khỏi tab rồi bấm Tiếp tục. |
| Tìm tab | Chọn trong wizard Kết nối & cài đặt, **bắt buộc**, giống tab Kênh nguồn. Wizard tự gợi ý tab tên đúng "Kênh block". Thiếu tab thì không hoàn tất được wizard. |

Quyết định mặc định (chủ repo có thể đổi khi duyệt):
- App chưa kết nối bảng Google nào: bỏ qua kiểm tra, ghi một dòng trong thông báo công việc "Chưa kết nối bảng nên chưa kiểm tra Kênh block".
- Hồ sơ cũ đã kết nối bảng nhưng chưa chọn tab block: công việc dừng với `JobIssueKind.Configuration` "Chọn tab Kênh block trong Kết nối & cài đặt", không tải gì.
- Dòng có ô Link kênh không rỗng nhưng không phải link kênh Douyin hợp lệ: dừng và báo số dòng sai (không lặng lẽ bỏ qua, vì gõ sai link sẽ làm việc chặn mất tác dụng). Dòng rỗng bỏ qua.
- Tab block chỉ đọc, app không bao giờ ghi vào tab này.

## Thiết kế

- **Snapshot đích:** lúc tạo công việc, chụp đích tab block từ cài đặt vào công việc (trường mới, vd. `VideoJob.BlockListSheet`), độc lập với `job.Sheet` vì công việc chỉ Tải có `job.Sheet = null`. `SheetTarget` thêm `BlockedChannelSheetId`, `BlockedChannelTabName`, `BlockedChannelColumns` giống nhóm `SourceChannel*`. Hồ sơ cũ đọc được (trường mới có giá trị mặc định null), không đổi schema SQLite nếu payload JSON đủ chứa.
- **Lấy sec_uid kênh của video:** ưu tiên `job.SourceChannel.SecUid` nếu đã có. Nếu chưa, thêm vào `IVideoSource` hàm đọc tác giả (vd. `ReadAuthorSecUidAsync`) dùng lại đường metadata hiện có (HTTP công khai → WebView2 khách → phiên tài khoản, trường `source_sec_uid`, `DouyinPublicReader.Author`). Lưu vào công việc (vd. `job.SourceAuthorSecUid`) và checkpoint để lần sau không đọc lại. Không đoán; không lấy được thì dừng như các lỗi nguồn hiện có (giữ xử lý 429/CAPTCHA).
- **Đọc tab block:** thêm vào `ISheetDestination` hàm (vd. `CheckBlockedChannelAsync(VideoJob, ct)`) trong `GoogleSheets`: đọc lưới tab qua `GridAsync` (`resolveDropdowns: false`), parse với `Rules.BlockedChannelHeaders = ["Link kênh"]` hoặc mapping đã lưu, rồi tìm `sec_uid`. Đọc mới mỗi lần chạy, không cache, để kênh vừa thêm vào tab có hiệu lực ngay với công việc còn dở. Giữ giới hạn 20.000 dòng/200 cột hiện có.
- **Chỗ chèn trong luồng:** `JobCoordinator.RunAsync`, ngay sau bước "Nhận diện Douyin" và **trước** "Kiểm tra thư mục trùng tên", thêm stage "Kiểm tra kênh block". Không áp dụng cho nhánh xóa kết quả (`DeletionRequested`) và nhánh chỉ xác minh YouTube đã đăng (`CanVerifyExisting`).
- **Thông báo:** dùng đường thông báo NeedsAction sẵn có (lịch sử + toast trong `MainViewModel.Notifications`). Kiểm tra `JobAttention` để loại lỗi mới hiện đúng nhãn và không được tự thử lại (`AutomaticRetryAttempts`).

## Các bước (mỗi bước build + test chạy được)

Chạy sau mỗi bước: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Native.ps1` phải exit 0, 0 cảnh báo.

1. **Core: model và luồng.** Thêm trường snapshot, `JobIssueKind.BlockedChannel`, `Rules.BlockedChannelHeaders`, hàm mới trong `ISheetDestination`/`IVideoSource` (có default để code cũ biên dịch được), stage mới trong `RunAsync`. Test runner (giả lập):
   - Kênh bị block → công việc NeedsAction loại BlockedChannel; không gọi tải, ghi bảng, upload.
   - Kênh không bị block → chạy tiếp như cũ.
   - Công việc chỉ Tải vẫn bị kiểm tra.
   - Chạy tiếp công việc dở sau khi kênh vừa bị thêm vào tab → dừng trước thao tác còn lại; thao tác đã xong giữ nguyên.
   - Chưa kết nối bảng → bỏ qua kèm thông báo; đã kết nối nhưng thiếu tab block → Configuration.
   - Payload hồ sơ cũ không có trường mới vẫn đọc được.
2. **Infrastructure: đọc tab và đọc tác giả.** `GoogleSheets.CheckBlockedChannelAsync` và đọc `sec_uid` trong `DouyinSource`. Test với HTTP giả lập: khớp đúng `sec_uid`; link có `www.` hay không; dòng rỗng bỏ qua; link sai báo đúng số dòng; tab bị xóa báo lỗi rõ; app không gửi request ghi nào tới tab block.
3. **Desktop: wizard và cài đặt.** `ConnectionWizardViewModel`, `GoogleConnectionDraft`, `MainViewModel` (dropdown chọn tab, gợi ý theo tên "Kênh block", không trùng tab video hoặc Kênh nguồn, nút Chọn cột cho tab block qua `SheetColumnsWindow`), cập nhật `SheetLabel`/`ColumnMappingLabel`. Cập nhật dữ liệu giả lập smoke (thêm tab `Kênh block`). Smoke: chạy exe đã build với `--data-dir <profile-fixture> --smoke-test <ảnh.png> --smoke-wizard`, kiểm tra ảnh và JSON báo cáo. Không chạy launcher mặc định.
4. **Tài liệu.** `README.md` (mục Google Sheets), `GOOGLE-SETUP.md` (cách tạo tab Kênh block), `CHANGELOG.md` mục `[Unreleased] → Added`, ghi chú vào `SPEC.md` nếu có mục mô tả luồng kiểm tra. Không đổi `VERSION` (tăng khi phát hành).

## Ràng buộc

- Giữ snapshot đích, checkpoint, khả năng tiếp tục và kiểm tra trùng hiện có (`AGENTS.md`).
- Không dùng tài khoản, bảng hay kênh thật; mọi ca Google/Douyin dùng giả lập.
- Không ghi email, đường dẫn máy, ID bảng thật vào code, test hay tài liệu.
- File `.ps1` nếu sửa: UTF-8 BOM + CRLF.

## Tiêu chí xong

- `Test-Native.ps1` exit 0, build 0 cảnh báo, các ca mới ở bước 1–2 đều đạt, tổng số ca tăng so với 333.
- Smoke wizard đạt và ảnh cho thấy ô chọn tab Kênh block.
- Tài liệu và CHANGELOG cập nhật trong cùng lần sửa.
- Chưa nghiệm thu trên bảng Google thật: ghi rõ đây là kết quả fixture.

## Quy trình GitHub

Issue "Chặn video từ kênh trong tab Kênh block" → branch `feat/blocked-channels` từ `main` mới nhất → sửa theo các bước trên → review diff + chạy test → commit nhỏ theo Conventional Commits (`feat(core): ...`, `feat(sheets): ...`, `feat(desktop): ...`, `docs: ...`) → push → PR `Closes #<số>` → CI xanh → squash merge → xóa branch.
