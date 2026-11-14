# Changelog

Mỗi bản ghi một mục, mới nhất ở trên. Bằng chứng kiểm tra chi tiết của từng phiên bản nằm ở `VALIDATION.md`, đặc tả ở `SPEC.md`.

## [Unreleased]

### Added
- Tạo release cá nhân bằng một lệnh: `scripts/Publish-Release.ps1` build gói không chứa Google OAuth, nén zip kèm SHA-256, gắn tag `v<VERSION>` và tạo GitHub Release để quản lý phiên bản và sao lưu bản cài. Chạy `-DryRun` để thử toàn bộ mà không gắn tag hay upload.
- Script release từ chối chạy khi không ở `main`, còn thay đổi chưa commit, chưa push, thiếu mục CHANGELOG, tài khoản `gh` không phải admin của repo hoặc tag đã ở commit khác. Nếu lượt trước dừng sau khi đã gắn tag, chạy lại sẽ làm tiếp phần còn thiếu.
- Zip release được giải nén và kiểm tra lại trước khi upload, nên bản sao lưu luôn cài được.
- `.gitattributes` cố định CRLF cho `.ps1` và `.cmd` khi checkout, kể cả máy dùng `core.autocrlf=false`; trước đó 12 file trên máy đang ở LF dù Git báo sạch.
- Hook Claude Code (`.claude/hooks/Check-Ps1Encoding.ps1`) kiểm UTF-8 BOM và CRLF ngay sau mỗi lần Claude ghi hoặc sửa file `.ps1`, kể cả file mới chưa có trong danh sách của `Test-WindowsLaunchers.ps1`.

### Changed
- Repo chuyển sang public, tạo lại từ một commit đầu sạch; chưa cấp license (mọi quyền được bảo lưu). Lược bỏ email, đường dẫn máy và tên project Google khỏi `VALIDATION.md` và tài liệu.
- `Publish-Release.ps1` không còn đòi repo private; thay vào đó chỉ release khi tài khoản `gh` là admin của repo, kiểm tra cả trước và sau build.
- `Test-ReleasePackage.ps1` không còn viết cứng ID project và email riêng; đọc thêm dấu riêng từ file local `packaging/personal-markers.txt` (bị ignore), thiếu file thì chỉ chạy kiểm tra chung.
- Script release siết thêm: mọi lệnh `gh` ghim host `github.com` (không theo `GH_HOST`), `gh auth status` chỉ xét `github.com`, báo "gh chưa cài" rõ ràng, kiểm tra lại quyền admin ngay sau build trước khi gắn tag, từ chối khi URL push của `origin` khác URL fetch, và chỉ coi release là chưa có khi `gh api` trả HTTP 404 (lỗi `gh` khác thì dừng).
- Build dừng ngay khi `VERSION` không có dạng 3 số, thay vì chỉ báo lỗi lúc cài.
- Tài liệu (`AGENTS.md`, `CLAUDE.md`, HANDOFF, `TODOS.md`) ghi rõ app chỉ dùng cá nhân, cách release, cài và khôi phục, và trạng thái Git hiện tại.

### Removed
- Gỡ gstack khỏi repo: xóa `.gstack/pregate.json` và dòng ignore `.gstack/tmp/`, bỏ mục gstack và skill routing trong `CLAUDE.md`/`AGENTS.md`.
- Bỏ quy trình bắt buộc mỗi thay đổi (nhánh riêng, review, tăng `VERSION`, mở PR) khỏi `CLAUDE.md`, `AGENTS.md` và HANDOFF. Lệnh test chuẩn vẫn là `scripts/Test-Native.ps1`.

## [0.3.44] - 2026-11-02

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
