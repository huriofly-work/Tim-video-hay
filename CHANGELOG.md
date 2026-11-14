# Changelog

Mỗi bản ghi một mục, mới nhất ở trên. Bằng chứng kiểm tra chi tiết của từng phiên bản nằm ở `VALIDATION.md`, đặc tả ở `SPEC.md`.

## [Unreleased]

### Added
- CI GitHub Actions (`.github/workflows/ci.yml`) chạy `scripts/Test-Native.ps1` trên `windows-latest` cho mỗi PR và push vào `main`; bỏ qua khi chỉ sửa file `.md`. CI tải ffmpeg 9.0.2 full_build từ GitHub release của gyan.dev, so SHA-256 của file nén rồi `Prepare-NativeTools.ps1` so tiếp SHA-256 của `ffprobe.exe` theo manifest. Action ghim theo SHA commit.
- Dependabot nâng cấp các GitHub Action hằng tháng, gộp một PR.

### Changed
- Nâng `Microsoft.Data.Sqlite` và `System.Security.Cryptography.ProtectedData` 10.0.12 → 11.0.0 (SQLitePCLRaw 3.0.5), `Microsoft.Web.WebView2` 1.0.4258.31 → 1.0.4314.35. Tạo lại lock file của Desktop và Tests để restore khoá phiên bản chạy được. Giấy phép WebView2 không đổi.

## [0.3.46] - 2026-11-14

Sửa lỗi cài đặt dừng giữa chừng và gộp về một bản build không chứa Google OAuth. Chức năng tải, bìa, Google Sheets và YouTube giữ nguyên như 0.3.45.

### Changed
- Chỉ còn một bản build, không nhúng Google OAuth, dùng chung cho máy mình và file đính kèm release. Bỏ chế độ build cá nhân (`-GoogleClientPath`, tự nhúng `packaging/google-oauth.desktop.json`) và cờ `-ForSharing`/`-NoBundledGoogle`. Hồ sơ đã kết nối Google dùng tiếp OAuth client đã lưu; hồ sơ mới nhập tệp Desktop JSON một lần ở Thiết lập nâng cao.
- App không còn đọc OAuth client nhúng trong exe. Gate `--check-bundle` từ chối gói có manifest resource tên Google/OAuth.
- Build tự xoá bản sao OAuth client cũ ở `tools/dotnet-home/google-oauth.bundle.json` nếu còn.

### Fixed
- Cài đặt không còn dừng ở bước tạo shortcut "Gỡ Tìm video hay <phiên bản>": WScript.Shell không lưu/đọc được tên có chữ ngoài bảng mã ANSI (vd. "ỡ"), nên `Install.ps1`/`Uninstall.ps1` lưu và đọc shortcut qua tên ASCII tạm.

## [0.3.45] - 2026-11-14

Bản đầu tiên trên repo public. Chức năng tải, bìa, Google Sheets và YouTube giữ nguyên như 0.3.44.

### Changed
- Tiêu đề cửa sổ hiện đúng phiên bản đang chạy (trước đây vẫn ghi 0.3.42). App gửi User-Agent `TimVideoHay/<phiên bản>` theo đúng bản cài, thay cho số cũ 0.3.35 và 0.3.16.
- Repo chuyển sang public, tạo lại từ một commit đầu sạch; chưa cấp license (mọi quyền được bảo lưu). Lược bỏ email, đường dẫn máy và tên project Google khỏi `VALIDATION.md` và tài liệu.
- `Publish-Release.ps1` không còn đòi repo private; thay vào đó chỉ release khi tài khoản `gh` là admin của repo, kiểm tra cả trước và sau build.
- `Test-ReleasePackage.ps1` không còn viết cứng ID project và email riêng; đọc thêm dấu riêng từ file local `packaging/personal-markers.txt` (bị ignore), thiếu file thì chỉ chạy kiểm tra chung.
- Script release siết thêm: mọi lệnh `gh` ghim host `github.com` (không theo `GH_HOST`), `gh auth status` chỉ xét `github.com`, báo "gh chưa cài" rõ ràng, từ chối khi URL push của `origin` khác URL fetch, và chỉ coi release là chưa có khi `gh api` trả HTTP 404 (lỗi `gh` khác thì dừng).

### Added
- `.gitattributes` cố định CRLF cho `.ps1` và `.cmd` khi checkout, kể cả máy dùng `core.autocrlf=false`.
- Hook Claude Code (`.claude/hooks/Check-Ps1Encoding.ps1`) kiểm UTF-8 BOM và CRLF ngay sau mỗi lần Claude ghi hoặc sửa file `.ps1`.
- `.github/dependabot.yml`: Dependabot mở PR nâng cấp gói NuGet hằng tuần, gộp chung một PR.

### Fixed
- Test tải phân đoạn đếm số request bằng phép cộng nguyên tử, không còn thỉnh thoảng báo sai khi bốn luồng chạy song song.

### Removed
- Gỡ gstack khỏi repo: xóa `.gstack/pregate.json` và dòng ignore `.gstack/tmp/`, bỏ mục gstack và skill routing trong `CLAUDE.md`/`AGENTS.md`.

## [0.3.44] - 2026-11-02

### Added
- Tạo release bằng một lệnh: `scripts/Publish-Release.ps1` build gói không chứa Google OAuth, nén zip kèm SHA-256, gắn tag `v<VERSION>` và tạo GitHub Release. Chạy `-DryRun` để thử toàn bộ mà không gắn tag hay upload. Script từ chối chạy khi không ở `main`, còn thay đổi chưa commit, chưa push, thiếu mục CHANGELOG hoặc tag đã ở commit khác; zip được giải nén và kiểm tra lại trước khi upload.
- Build dừng ngay khi `VERSION` không có dạng 3 số, thay vì chỉ báo lỗi lúc cài.

### Changed
- Tải video nhanh hơn và tiếp tục được kiểu IDM: tối đa bốn luồng cùng tải các vùng tối đa 4 MiB, dùng lại kết nối và ghi đúng vị trí. Khi tạm dừng, thoát app rồi mở lại, app chỉ tải phần còn thiếu.
- Gặp lỗi mạng, app tự giảm số luồng 4 → 2 → 1. CDN không hỗ trợ tải theo vùng thì chuyển sang tải tuần tự và chỉ giữ phần đầu đã xác minh.

## [0.3.43] - 2026-11-02

### Changed
- Bìa đọc đúng ID video, ưu tiên poster được chỉ định và giữ nguyên ảnh nguồn. Nút Bổ sung bìa bỏ cache cũ để lấy lại ảnh.

## [0.3.42] - 2026-10-19

### Changed
- Đọc dữ liệu Douyin theo thứ tự HTTP công khai → WebView2 khách → phiên tài khoản khi thiếu dữ liệu bắt buộc, có thông báo khi dùng tài khoản.

Các phiên bản trước 0.3.42 xem `SPEC.md` và `VALIDATION.md`.
