# TODOS

App chỉ để chủ repo tự dùng. Release dùng để quản lý phiên bản và sao lưu bản cài, không nhắm tới người khác; repo public nên ai cũng tải được file đính kèm. Nguồn gốc: mục 5 của `docs/HANDOFF.md` (05/11/2026).

## Nghiệm thu

### Nghiệm thu tải phân đoạn 0.3.44 và bìa trên công việc thử được phép

**What:** Chạy tải phân đoạn và nút Bổ sung bìa trên dữ liệu Douyin thật được phép, ghi bằng chứng.

**Why:** Tải kiểu IDM 0.3.44 và bìa 0.3.43 mới qua fixture; chưa có benchmark hay resume thật trên CDN.

**Context:** So thời gian/throughput với bản trước trên cùng rendition và điều kiện mạng. Dừng giữa tải, thoát app, mở lại và tiếp tục. Thử Bổ sung bìa với Alien 03 trên lịch sử thật: video giữ nguyên, không gọi Google. Chỉ kết luận có bìa ngang khi có poster nguồn đúng ID. **Đạt khi:** file đủ byte, probe/hash đối chiếu được, chỉ tải vùng thiếu hoặc fallback an toàn, không tạo kết quả trùng. Cần chỉ dẫn riêng trước khi dùng dữ liệu thật.

**Effort:** M
**Priority:** P1
**Depends on:** None

### Nghiệm thu Google bằng bảng và kênh thử riêng

**What:** Chạy wizard OAuth, ghi Sheets và upload YouTube trên đích thử riêng.

**Why:** Các ca Google trong runner đều dùng giả lập; trạng thái OAuth, hạn mức và điều kiện upload chưa xác minh lại.

**Context:** Ghi đúng tab/cột, không trùng, không hỏng công thức hay dropdown. Upload rồi đối soát cùng ID khi gián đoạn. Thử kết nối lại đúng bảng/kênh, xóa từng phần sau xác nhận. Quy trình ở `GOOGLE-SETUP.md`. **Đạt khi:** có bằng chứng Google thật, ghi tách khỏi kết quả fixture. Cần chỉ dẫn riêng trước khi dùng tài khoản thật.

**Effort:** M
**Priority:** P1
**Depends on:** None

## Release và sao lưu

### Nghiệm thu khôi phục từ zip release

**What:** Tải zip từ release, so SHA-256, giải nén, chạy `Cai-dat.cmd` trên hồ sơ fixture và kết nối Google lại.

**Why:** Release chỉ có giá trị sao lưu khi đã khôi phục thử thành công một lần.

**Context:** Dùng `--data-dir` fixture, không đụng hồ sơ thật. **Đạt khi:** bản cài từ zip mở được, smoke test đạt, biết rõ các bước kết nối lại Google.

**Effort:** S
**Priority:** P2
**Depends on:** None (release v0.3.46 đã có)

### Sao lưu và khôi phục hồ sơ

**What:** Ghi và thử cách sao lưu/khôi phục `%LOCALAPPDATA%\TimVideoHay`.

**Why:** Release không chứa hồ sơ. Payload mã hóa DPAPI CurrentUser, nên cài lại Windows hoặc đổi máy có thể mất lịch sử job, cài đặt và quyền Google.

**Context:** Tối thiểu: sao lưu đúng (thoát app, giữ bộ DB/WAL/SHM nhất quán), khôi phục trên cùng user, và cách làm lại từ đầu khi không giải mã được (kết nối lại Google, nhập lại link). Hồ sơ không bao giờ lên GitHub. **Đạt khi:** hướng dẫn đã thử thật một lần trên hồ sơ fixture và app mở lại đúng lịch sử.

**Effort:** M
**Priority:** P2
**Depends on:** None

### Nâng cấp tại chỗ trên máy mình

**What:** Cài bản kế tiếp bằng `./packaging/Install-Local.ps1` đè lên bản đang dùng trên máy.

**Why:** Mỗi release sẽ được cài đè theo cách này; cần chắc hồ sơ và công việc dở được giữ.

**Context:** Bản cũ vẫn nằm trong `%LOCALAPPDATA%\Programs\TimVideoHay\<VERSION>`. **Đạt khi:** nâng cấp giữ nguyên hồ sơ và công việc dở, quay lại bản cũ được.

**Effort:** S
**Priority:** P2
**Depends on:** None

## Ngoài phạm vi hiện tại

- **Phân phối cho người khác:** ký gói, rà giấy phép phân phối ffprobe, nghiệm thu trên Windows sạch, MSI/MSIX. Chỉ mở lại khi đổi quyết định dùng cá nhân.
- **Tính năng:** phụ đề nguồn (chưa có mẫu dữ liệu có mốc thời gian), nguồn video ngoài Douyin, API tải bên thứ ba, nhập video local, chuyển mã, AI.

## Completed

### Tài liệu cũ lệch với mã

**What:** Sửa hoặc đánh dấu lịch sử các tài liệu cũ.

**Why:** `REBUILD-PLAN.md` còn mô tả yt-dlp/cookie thủ công; `SECURITY.md` còn mô tả segment 32 MiB, trong khi 0.3.44 dùng vùng tối đa 4 MiB và bốn worker.

**Context:** Đối chiếu với mã hiện tại và `README.md`. **Đạt khi:** tài liệu khớp mã, hoặc file kế hoạch cũ ghi rõ là lịch sử.

**Effort:** S
**Priority:** P3
**Depends on:** None
**Completed:** 2026-11-14 — `SECURITY.md` mô tả đúng bốn kết nối × vùng 4 MiB và đoạn 32 MiB khi tải tuần tự; `REBUILD-PLAN.md` ghi rõ là tài liệu lịch sử; `SPEC.md` ghi chú ForSharing đã bỏ từ 0.3.46; `README.md` khuyên giữ OAuth JSON ngoài thư mục repo.

### Audit lỗ hổng dependency

**What:** Thỉnh thoảng chạy `dotnet list package --vulnerable` cho các project native.

**Why:** Restore dùng lock file nhưng chưa từng chạy audit trực tuyến.

**Context:** Cần mạng. **Đạt khi:** không có lỗ hổng mức cao chưa xử lý, hoặc đã ghi lý do chấp nhận.

**Effort:** S
**Priority:** P3
**Depends on:** None
**Completed:** 2026-11-14 — thay bằng Dependabot alerts và security updates trên GitHub, theo dõi liên tục các gói NuGet; ngày 14/11/2026 có 0 cảnh báo đang mở.

### CI GitHub Actions cho build + test

**What:** Chạy `scripts/Test-Native.ps1` trên `windows-latest` với .NET SDK 10 cho mỗi PR.

**Why:** PR hiện không có check tự động, chỉ có test chạy local.

**Context:** Cần nguồn tải `ffprobe` 9.0.2 (gyan.dev full_build) ổn định cho CI, khớp SHA-256 trong `packaging/tool-manifest.public.json`; thiếu ffprobe làm 3 ca test thất bại. **Đạt khi:** PR vào `main` có check xanh, không cần secret Google.

**Effort:** M
**Priority:** P3
**Depends on:** None
**Completed:** 2026-11-14 — `.github/workflows/ci.yml` tải ffmpeg từ GitHub release `GyanD/codexffmpeg` 9.0.2, kiểm SHA-256 file nén và `ffprobe.exe`; bỏ qua job khi chỉ sửa `.md`.

### Phiên bản hiển thị còn cứng trong mã

**What:** Cho tiêu đề cửa sổ và User-Agent lấy từ phiên bản assembly.

**Why:** `native/TimVideoHay.Desktop/MainWindow.xaml` ghi "Tìm video hay 0.3.42", `App.xaml.cs` dùng User-Agent 0.3.35, trong khi `VERSION` là 0.3.44.

**Context:** Phiên bản assembly đã đọc từ `VERSION` qua `native/Directory.Build.props`. **Đạt khi:** cả hai lấy từ phiên bản assembly, có test hoặc smoke xác nhận.

**Effort:** S
**Priority:** P3
**Depends on:** None
**Completed:** v0.3.45 (2026-11-14) — `AppInfo` trong Core cấp tiêu đề cửa sổ và User-Agent (cả bản dịch tên kênh) từ phiên bản assembly; test runner đối chiếu với `VERSION`.

### Release đầu tiên v0.3.44

**What:** Chạy `scripts/Publish-Release.ps1` trên `main` để tạo tag `v0.3.44` và GitHub Release đầu tiên.

**Why:** Chưa có mốc phát hành nào; bản cài hiện chỉ nằm trong `artifacts/` trên máy này.

**Context:** Chạy `-DryRun` trước. Release đính kèm `TimVideoHay-0.3.44-windows-x64.zip` (gói `-ForSharing`, không có OAuth) và `.sha256`. Tạo tag và upload cần chủ repo xác nhận. **Đạt khi:** release `v0.3.44` có zip + checksum khớp, ghi chú lấy từ `CHANGELOG.md`.

**Effort:** S
**Priority:** P1
**Depends on:** PR thêm `Publish-Release.ps1` đã merge
**Completed:** v0.3.44 (2026-11-06) trên repo private cũ (zip + `.sha256`, SHA-256 zip `ece34b98fc21ddca29f3f188b0c4c1b615a42cbf8a800b37ce9b03d4df7c6faf` khớp digest GitHub). Từ 2026-11-14 repo được tạo lại ở chế độ public; release v0.3.44 chỉ còn trong bản sao lưu local của chủ repo.
