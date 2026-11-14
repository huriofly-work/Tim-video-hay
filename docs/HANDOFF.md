# Bàn giao kỹ thuật · Tìm video hay 0.3.45

Cập nhật ngày 14/11/2026. Dành cho Claude Code tiếp tục trên thư mục local chứa repo này.

**Phạm vi sử dụng:** app do chủ repo tự dùng trên máy mình; mã nguồn công khai trên GitHub nhưng chưa cấp license (mọi quyền được bảo lưu). Release (tag `v<VERSION>` + GitHub Release, đính kèm gói `-ForSharing`) dùng để quản lý phiên bản và sao lưu bản cài; vì repo public nên file đính kèm ai cũng tải được. Quy trình ở mục "Release và cài bản mới" của `CLAUDE.md`.

## 1. Mã nguồn cần tiếp nhận

- **Bản Windows 0.3.44 nằm ở `main`** trên GitHub `huriofly-work/Tim-video-hay` (repo public). Ngày 14/11/2026 repo được tạo lại từ một commit đầu sạch; lịch sử, Issue, PR và release v0.3.44 của repo private cũ chỉ còn trong bản sao lưu local của chủ repo.
- **Repo:** Phiên bản duy nhất ở `VERSION`, tồn đọng ở `TODOS.md`, lịch sử ở `CHANGELOG.md`. `git worktree list` chỉ có worktree chính.
- Được ignore và không có trong commit: `artifacts/` (gói build), `tools/` (ffprobe, cache), `test-output/`, `data/`, `bin/`/`obj/`, log, `packaging/google-oauth.desktop.json`. Clone mới cần chạy `./scripts/Prepare-NativeTools.ps1 -FfprobePath <ffprobe-đúng-hash>` trước khi test (ba ca cần `tools/ffprobe.exe`) và dùng thư mục fixture có đường dẫn ngắn: fixture dài khoảng 250 ký tự từng làm SQLite báo lỗi 14. Với hai điều kiện này, clone sạch của snapshot native 0.3.44 đã đạt build Release 0 cảnh báo/0 lỗi và **332/332** ca test.

## 2. Sản phẩm và kiến trúc

Ứng dụng Windows tiếng Việt cho quy trình lấy video mẫu Douyin, lưu video và bìa, ghi Google Sheets và tùy chọn đăng YouTube. Có một biểu mẫu, một lịch sử và năm tổ hợp thao tác hợp lệ. YouTube yêu cầu Tải. Chỉ điền bảng dùng thời lượng Douyin, không tạo thư mục tải hay dùng `ffprobe`. Luồng native chạy trực tiếp trên máy, dùng API Google và WebView2 riêng cho Douyin.

| Khu vực | Điểm vào và trách nhiệm |
| --- | --- |
| [`native/TimVideoHay.Desktop`](../native/TimVideoHay.Desktop) | `App.xaml.cs` khởi tạo dịch vụ, single instance theo profile. `MainWindow`/`MainViewModel` điều khiển WPF/XAML, tray, cài đặt, wizard Google, tiến độ, thông báo và lịch sử. `DouyinBrowserReader` đọc WebView2. |
| [`native/TimVideoHay.Core`](../native/TimVideoHay.Core) | `Models.cs`, `Contracts.cs`, `JobCoordinator*.cs`. Kiểm tra trùng, xếp hàng, snapshot đích, checkpoint từng thao tác, đối soát và tiếp tục. Tải/đăng chạy tuần tự. Các lượt ghi bảng riêng, xóa Google và xác nhận YouTube dùng nhóm nền tối đa bốn, có tuần tự hóa ghi/xóa cùng bảng. |
| [`native/TimVideoHay.Infrastructure`](../native/TimVideoHay.Infrastructure) | `LocalStore`/`PrivateFiles` quản lý SQLite và dữ liệu riêng. `DouyinDataReader`, `DouyinPublicReader`, `DouyinSource`, `DouyinCovers`, `MediaDownload*.cs` xử lý nguồn, chọn rendition và tải. `GoogleAuth`/`GoogleConnectionService`, `GoogleSheets`, `YouTubeDestination` thực hiện OAuth, ghi bảng và resumable upload. |
| [`native/TimVideoHay.Tests`](../native/TimVideoHay.Tests) | Test runner console trong `Program.cs`, HTTP giả lập và fixture SQLite, DPAPI, file Windows. Smoke WPF/WebView2 nằm trong Desktop. |
| [`scripts/`](../scripts), [`packaging/`](../packaging), [`legacy/browser-v022`](../legacy/browser-v022) | PowerShell đóng gói win-x64 tự chứa, gate phát hành, cài/gỡ theo user và manifest công cụ. Bản Node/Chromium cũ là kho khôi phục, tách khỏi đường build/chạy native. |

Stack hiện tại là C#/.NET 10 (`net10.0-windows`), WPF + WinForms cho tray, WebView2 SDK `1.0.4258.31`, `Microsoft.Data.Sqlite` và `ProtectedData` `10.0.12`, `ffprobe` standalone ghim SHA-256. Mặc định hồ sơ ở `%LOCALAPPDATA%\TimVideoHay`, schema SQLite **8**, WAL và migration giao dịch. Payload jobs/settings, quyền Google, metadata tải và receipt dùng DPAPI CurrentUser. WebView2 quản lý phiên trong profile có ACL riêng. Sao lưu sau khi thoát app hoặc giữ bộ DB/WAL/SHM nhất quán. Chuyển profile mã hóa sang máy hoặc user Windows khác chưa hỗ trợ.

## 3. Trạng thái và các quyết định cần giữ

| Trạng thái | Phạm vi |
| --- | --- |
| Có trong mã | Tải/tiếp tục và bìa độc lập, nhập link và chống trùng, lịch sử/lọc/tìm, bổ sung/xóa chọn phần, tray và khôi phục, đối soát file/thư mục di chuyển. Sheets ghi tab video và upsert tab kênh nguồn. YouTube upload nối tiếp, xác nhận theo ID và xóa theo đúng kênh. Kết nối bảng/kênh độc lập và OAuth dùng trình duyệt hệ thống, PKCE/state, Picker. Có hai gói portable 0.3.44 local. |
| Có mã, còn nghiệm thu | Tải phân đoạn 0.3.44 trên CDN thật và qua Tạm dừng/Tiếp tục sau khi thoát app. Nút Bổ sung bìa trên lịch sử thật. OAuth → Sheets/YouTube trên đích được phép, cài/nâng cấp/gỡ trên Windows sạch. Các smoke và fixture chỉ bao phủ môi trường thử. |
| Chưa triển khai hoặc ngoài phạm vi | Phụ đề nguồn, nguồn video ngoài Douyin, API tải bên thứ ba, nhập video local, chuyển mã và AI. Không có MSI/Setup.exe/MSIX hay chữ ký phát hành. Chưa có luồng chuyển lịch sử giữa máy/user hoặc import quyền từ bản trình duyệt. |

- **Giữ quy trình gọn.** Một biểu mẫu/lịch sử, UI chỉ hiện thông tin cần thao tác. Tên job tối đa 30 ký tự Unicode, giữ cụm dấu/emoji. Short/Long mặc định 5 phút từ metadata nguồn, cho đổi mốc, job giữ snapshot. Ghi cột đã gán dưới dạng literal, giữ cấu trúc và dropdown của bảng. Tên kênh ngoài Latinh dịch sang tiếng Anh, loại nội dung kênh từ tối đa 10 video mới nhất. YouTube mặc định Không công khai cho cấu hình mới và đặt không dành cho trẻ em. Giữ snapshot đúng đích khi kết nối lại.
- **Tránh side effect lặp.** Checkpoint/đối soát trước khi gửi lại ghi bảng hoặc upload có kết quả chưa rõ. Xóa từng phần sau xác nhận, xử lý file trước, giữ lịch sử/phần còn dở khi lỗi. App kiểm tra chủ sở hữu, file hash và junction. Khôi phục công việc dở ở trạng thái dừng, cho người dùng tiếp tục.
- **Douyin 0.3.42.** Ưu tiên HTTP công khai → WebView2 khách → phiên tài khoản khi thiếu dữ liệu video bắt buộc. Tự chuyển phiên theo yêu cầu người dùng, thông báo khi dùng tài khoản. Đối chiếu đúng ID. Giữ dừng/chờ khi 429, CAPTCHA đang chờ, nguồn báo mất hoặc người dùng hủy. Thiếu bìa không kích hoạt tài khoản. Alien 02 từng trả 403 thiếu UIFID và feed gợi ý sai ID. Thêm riêng header `x-tt-argus` từng không giải quyết UIFID. Bản 0.3.42 đã đọc metadata Alien 02 thật qua fallback, chưa phải tải hoàn chỉnh hay nghiệm thu Google.
- **Bìa 0.3.43.** Người dùng chỉ ra Jingxuan thường có bìa ngang. Đọc bìa đúng ID, ưu tiên poster được chỉ định và giữ nguyên ảnh nguồn. Chấp nhận `source_cover` khi `cover` và `dynamic_cover` cùng tài nguyên, khác `origin_cover`, qua kiểm tra ảnh tĩnh. `RefreshCoversAsync` bỏ cache cũ để bổ sung ảnh riêng. Alien 03 đã lấy được bìa dọc WebP 750×1002 bằng phép thử trực tiếp, chưa nghiệm thu nút lịch sử. Feed Select trả ID khác, detail Jingxuan đúng ID trong lượt thử chỉ trả nhóm dọc. Nguyên nhân thiếu bìa ngang vẫn chưa rõ. Không dùng ảnh video khác, khung hình hay ảnh cắt thay thế.
- **Tải 0.3.44.** Theo yêu cầu tăng tốc và resume kiểu IDM, thay batch 16 MiB chờ đủ bằng tối đa bốn worker nhận vùng tối đa 4 MiB khi rảnh, tái sử dụng kết nối, ghi theo vị trí. Receipt DPAPI lưu vùng đã flush, resume phần thiếu, đọc được receipt prefix cũ. Range/validator phải khớp. Giảm 4→2→1 khi lỗi mạng, fallback tuần tự khi CDN bỏ Range, chỉ giữ prefix đã xác minh. Mất điện giữa hai checkpoint có thể làm tải lại một phần. Chưa có benchmark 0.3.44 trên CDN thật.
- **Các hướng đã khảo sát.** Bộ tải HTTP nội bộ thay yt-dlp, giữ `ffprobe` để kiểm tra stream/ảnh, không đổi codec. BugPk không cho bản tốt hơn trên hai mẫu, bìa có độ phân giải thấp hơn. Devtool trả lỗi ứng dụng trên hai link thử. TikHub chưa thử vì cần quyền truy cập và chưa có bằng chứng vượt chất lượng hiện tại. Phụ đề được hoãn vì chưa tìm được mẫu dữ liệu phụ đề nguồn có mốc thời gian. Người dùng chỉ muốn phụ đề nguồn, không OCR/ASR/dịch tạo phụ đề. Xem [nghiên cứu chất lượng](../DOUYIN-QUALITY-API-RESEARCH.md), [phiên](../DOUYIN-SESSION-RESEARCH.md) và [phụ đề](../DOUYIN-SUBTITLE-RESEARCH.md).

## 4. Cách chạy và kết quả 05/11/2026

Chạy từ gốc repo trên Windows x64. Máy phát triển dùng .NET SDK 10 (`10.0.401` đã kiểm tra). Gói portable cần WebView2 Runtime cho đường WebView, kèm runtime .NET và `ffprobe`. Không cần Node, Python hoặc yt-dlp cho bản native. NuGet dùng lock file và `native/NuGet.Config`. Chuẩn bị `ffprobe` khi thiếu bằng `./scripts/Prepare-NativeTools.ps1 -FfprobePath <đường-dẫn-ffprobe-đúng-hash>`.

```powershell
$env:DOTNET_CLI_HOME = Join-Path $PWD 'tools/dotnet-home'
$env:NUGET_PACKAGES = Join-Path $PWD 'tools/nuget-packages'
foreach ($name in 'Desktop', 'Tests') {
    $project = "native/TimVideoHay.$name"
    dotnet restore $project --locked-mode --configfile native/NuGet.Config
    if ($LASTEXITCODE) { throw "Restore thất bại: $project" }
    dotnet build $project -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false -p:GoogleOAuthClientPath=
    if ($LASTEXITCODE) { throw "Build thất bại: $project" }
}
$fixture = 'test-output/native-' + [guid]::NewGuid().ToString('N')
dotnet run --project native/TimVideoHay.Tests -c Release --no-build --no-restore -- $fixture
```

- **Restore/build:** lượt sandbox dừng vì bị từ chối đọc cấu hình NuGet user. Lượt chạy có quyền phù hợp restore hai project từ dependency hiện có và build Release đều đạt, **0 cảnh báo, 0 lỗi**. Restore báo up-to-date, chưa xác nhận truy vấn audit lỗ hổng trực tuyến.
- **Backend:** console runner Release chạy hết **332 ca đạt, 0 lỗi**, exit 0. Fixture riêng tại `test-output/handoff-native-f937d5b192db49519b858da3b13300ff`. Các ca HTTP/CDN/Google dùng giả lập. Lỗi tạo junction của sandbox từng xuất hiện ở 0.3.43, hai ca đó chạy lại ngoài sandbox đã đạt. Lỗi kỳ vọng offset phải luôn tăng trong test phân đoạn ban đầu đã được sửa ở 0.3.44 vì worker có thể bị hủy trước khi nhận byte.
- **UI:** smoke gói sharing 0.3.44 exit 0. Chạy WPF/WebView2 với profile riêng, fixture lịch sử 127 job, kiểm tra điều khiển và wizard ba bước dùng Google giả lập. Báo cáo tại `test-output/handoff-wpf-497ea85756b04a0bbad5f5e26953cf02/app.png.json` và `wizard.json`. Lượt này chưa nghiệm thu qua launcher mặc định, đăng nhập hoặc tải/đăng thật.
- **Lint/check:** `./scripts/Test-WindowsLaunchers.ps1` qua kiểm tra BOM và parser cho 9 script trên PowerShell 5.1 và 7.6.5. `git diff --check` exit 0 với cảnh báo LF/CRLF của file cũ. Không thấy lệnh lint C# riêng hay CI trong cây nguồn. Build coi warning là error.
- **Run:** launcher người dùng là `./Start.ps1`, `Mo-ung-dung.cmd` hoặc `TimVideoHay.exe` trong gói. Khi kiểm tra, gọi exe trực tiếp với `--data-dir <profile-fixture>` và `--smoke-test <ảnh-png> --smoke-wizard`. Launcher mặc định dùng profile thật.
- **Đóng gói:** `./scripts/Build-Native.ps1 -ForSharing -OutputDirectory <thư-mục-mới>` build/test/publish bản chia sẻ. `-SkipPublish -NoBundledGoogle` dùng khi chỉ cần build/test qua wrapper. Khi publish, gate OAuth gọi runner với `--no-build` ngay cả khi dùng `-SkipTests`, nên cần runner Release đã build. Bản cá nhân có thể nhúng Desktop OAuth JSON qua `-GoogleClientPath`. Cấu hình riêng qua wizard hoặc file riêng được ignore, không cần biến môi trường bí mật. `DOTNET_CLI_HOME` và `NUGET_PACKAGES` dùng cho cache khi build, `LOCALAPPDATA` dành cho hồ sơ/cài đặt.
- **Gói có sẵn:** `./scripts/Test-ReleasePackage.ps1 -PackageDirectory artifacts/sharing-windows-x64-0.3.44` đạt kiểm tra privacy/file policy, tool integrity và manifest, **501 file**. `--check-bundle` của console runner xác nhận gói sharing không nhúng Google OAuth. Lượt này dùng gói đã có và build mã local, chưa publish mới, chưa chạy lifecycle cài/gỡ, chưa thao tác trên tài khoản hay công việc thật. Bằng chứng lịch sử 0.3.44 cho cả personal/sharing nằm cuối [VALIDATION.md](../VALIDATION.md).

## 5. Việc tiếp theo và tiêu chí hoàn thành

Danh sách việc đang theo dõi ở `TODOS.md`; mục dưới đây giữ bối cảnh và tiêu chí gốc.

1. **Giữ mốc mã và đưa vào quy trình làm việc: đã xong ngày 05/11/2026.** Từ 14/11/2026 repo là public (xem mục 1). Khi chuyển máy, không đưa `artifacts` cá nhân, credential, profile hay log thô vào bản chuyển.
2. **Nghiệm thu tải và bìa trên công việc thử được phép.** So thời gian/throughput trên cùng rendition và điều kiện mạng. Dừng giữa tải, thoát, mở lại và tiếp tục. Đạt khi file đủ byte, probe/hash đối chiếu được, chỉ tải các vùng thiếu hoặc fallback an toàn, và không tạo kết quả trùng. Thử Bổ sung bìa Alien 03 riêng, xác minh video giữ nguyên và không gọi Google. Chỉ kết luận có bìa ngang khi có poster nguồn đúng ID.
3. **Nghiệm thu Google bằng đích thử riêng.** Xác thực bằng wizard, ghi đúng tab/cột, không trùng và không hỏng công thức. Upload rồi đối soát cùng ID khi gián đoạn, thử kết nối lại đúng bảng/kênh và xóa từng phần sau xác nhận. Tách kết quả fixture khỏi bằng chứng Google thật. Trạng thái OAuth, hạn mức và điều kiện upload hiện tại chưa xác minh lại. [GOOGLE-SETUP.md](../GOOGLE-SETUP.md) ghi quy trình và bằng chứng lịch sử.
4. **Release và độ bền khi tự dùng.** Repo public nên file đính kèm release là phân phối công khai: gói phải kèm đúng giấy phép công cụ (ffprobe GPLv3 và nguồn FFmpeg, WebView2) theo [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md); chưa cần ký gói. Release `v0.3.44` cũ chỉ còn trong bản sao lưu; bản tiếp theo tạo bằng `scripts/Publish-Release.ps1`. Cần nghiệm thu khôi phục từ zip release (giải nén, cài, kết nối Google lại), nâng cấp tại chỗ giữ nguyên hồ sơ, cách sao lưu/khôi phục hồ sơ (DPAPI gắn với user Windows, release không chứa hồ sơ), thỉnh thoảng audit lỗ hổng dependency. Gói hiện chưa ký, checksum chỉ chứng minh tính toàn vẹn theo manifest. DPAPI/ACL không bảo vệ khỏi tiến trình cùng tài khoản hoặc admin. WebView2 có mạng riêng, không áp đủ chính sách DNS của downloader. Xóa YouTube cần scope rộng hơn upload, Sheets có rủi ro chỉnh sửa đồng thời và giới hạn 20.000 dòng/200 cột. Chưa có bằng chứng kiểm toán độc lập. Xem [SECURITY.md](../SECURITY.md), [INSTALLATION.md](../INSTALLATION.md) và [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md).
5. **Dọn lệch tài liệu khi có lượt chỉnh riêng.** `REBUILD-PLAN.md` còn mô tả yt-dlp/cookie thủ công, `SECURITY.md` còn mô tả segment 32 MiB, `App.xaml.cs` còn User-Agent 0.3.35. Khi tài liệu cũ khác triển khai, đọc mã hiện tại, `README.md` và mục 0.3.44 ở cuối `SPEC.md`/`VALIDATION.md` trước. Các file `*-PLAN.md` lưu lịch sử quyết định, trạng thái triển khai lấy từ mã và kiểm tra.

## Tiếp nhận trong Claude Desktop

Mở tab **Code**, chọn local folder chứa repo và gửi:

> Đọc `AGENTS.md`, `CLAUDE.md`, `docs/HANDOFF.md` và `TODOS.md`. Đối chiếu Git (nhánh `main`) với mã native trong working tree. Tóm tắt kết quả tiếp nhận và chọn bước đầu theo `TODOS.md`, kèm tiêu chí kiểm tra. Giữ mọi thay đổi có sẵn. Chỉ dùng fixture local trong lượt tiếp nhận. Chờ chỉ dẫn riêng trước khi thao tác trên tài khoản/dữ liệu thật hoặc thay đổi Git.

Nguồn đối chiếu gồm cây mã local, Git, các tài liệu liên kết và các trao đổi truy cập được về fallback Alien 02, bìa Alien 03, tải kiểu IDM và cách mở Claude Desktop. Các bằng chứng lịch sử chỉ áp dụng cho phiên bản và điều kiện thử được ghi trong từng lượt.
