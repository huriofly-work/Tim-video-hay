# API Douyin và chất lượng tải xuống

Ngày đối chiếu: 08/10/2026. Phạm vi: video, ảnh bìa ngang/dọc và phụ đề nguồn; không OCR, nhận dạng giọng nói hoặc nâng chất lượng bằng AI.

## Kết luận

Có API bên thứ ba đáng thử, đặc biệt TikHub có endpoint riêng cho chất lượng mà nhà cung cấp gọi là “original upload quality”. Chưa có phép thử chứng minh API trả bản tốt hơn bộ tải hiện tại. API miễn phí BugPk đã thử trên cả hai video mẫu không cho thấy chất lượng video cao hơn và trả ảnh bìa có độ phân giải thấp hơn.

Chưa thay đổi bộ tải hoặc tích hợp dịch vụ trả phí vào app. Giữ tải local miễn phí làm mặc định; chỉ thêm API tùy chọn nếu phép thử trực tiếp chứng minh có lợi ích.

## Đối chiếu thực tế API miễn phí

Đã gọi [BugPk](https://api.bugpk.com/doc-douyin-search.html) bằng hai link công khai, không gửi cookie cá nhân hoặc khóa Google.

| Video mẫu | Bản app đã tải | BugPk trả về |
|---|---|---|
| 7673782911702748458 — ô tô | 1920×1080; 277.138.676 byte | Cùng độ phân giải và số byte |
| 7659702794928147739 — bọ ngựa | 3840×2160; 915.502.655 byte | Cùng độ phân giải và số byte |

Với mỗi video, đã đọc ba đoạn 65.536 byte ở đầu, giữa và cuối file từ link CDN do API trả về, so sánh với file app đã tải. Cả sáu đoạn trùng nhau. Kết quả phù hợp với việc API và app lấy cùng một bản video, nhưng chưa đối chiếu hash toàn bộ file từ API; không khẳng định toàn bộ file giống tuyệt đối.

Ảnh bìa BugPk trả về chỉ 640×360 cho cả hai video. Không có trường bìa ngang riêng trong phản hồi đã thử. Với video ô tô, ảnh này không trùng hash với cả hai ảnh bìa đúng mà app đang giữ: dọc 1078×1440 và ngang 1440×1080. Vì vậy không dùng trường `cover` chung này thay thế hai bìa nguồn của app.

Không thấy trường phụ đề có mốc thời gian trong các phản hồi đã thử. Đây là kết quả của hai mẫu, không phải kết luận rằng mọi video Douyin đều không có phụ đề nguồn.

Bằng chứng được lưu, không chứa cookie, API key hoặc link CDN có chữ ký:

- `test-output/douyin-api-research-20261008/public-api-result.json`
- `test-output/douyin-api-research-20261008/quality-comparison.json`
- Các bản tải local và thông số đối chiếu được ghi tại `VALIDATION.md`.

## TikHub: ứng viên cần thử

[Tài liệu endpoint App V3](https://docs.tikhub.io/312096107e0):

```text
GET https://api.tikhub.io/api/v1/douyin/app/v3/fetch_video_high_quality_play_url
```

- Nhận `aweme_id` hoặc `share_url`; cần API key của TikHub.
- Trả `original_video_url` và `video_data` theo tài liệu.
- Giá công bố: 0,005 USD/lần gọi endpoint này, tương đương 5 USD/1.000 lần gọi trước các điều chỉnh giá khác.
- Có [endpoint Web tương đương](https://docs.tikhub.io/312096106e0).
- [Trang dịch vụ](https://tikhub.io/douyin-api) quảng cáo 0,05 USD credit đăng ký miễn phí, không cần thẻ. Chưa tạo tài khoản để xác nhận việc cấp credit.

“Chất lượng gốc lúc tải lên” là mô tả của nhà cung cấp. Chưa gọi API vì chưa có API key; chưa xác nhận file là bản tác giả đã tải lên, chưa chứng minh vượt bản 4K mà app đang lấy, và chưa xác nhận trả đủ hai ảnh bìa đúng ở độ phân giải cao hơn. Không dùng từ “gốc” hoặc dung lượng lớn làm bằng chứng duy nhất về chất lượng.

## Các hướng khác đã khảo sát

- [Devtool](https://www.devtool.top/doc/douyin/parse.html): thử hai link đầy đủ, HTTP 200 nhưng mã ứng dụng 400 và không có dữ liệu. Chưa thử link rút gọn; không kết luận dịch vụ luôn lỗi.
- [EasyDown](https://docs.easydown.org/zh/api/douyin): có API phân tích trả nhiều bản video và metadata, cần token và tính credit. Chưa thử; tài liệu chưa chứng minh chất lượng cao hơn bộ tải hiện tại.
- [Douyin Open API video/data](https://open.douyin.com/platform/resource/docs/openapi/video-management/douyin/search-video/video-data/): yêu cầu quyền ứng dụng và người dùng cấp quyền, phục vụ thông tin video của người dùng đã cấp quyền. Chưa tìm thấy trong tài liệu đã khảo sát một API chính thức đáp ứng tải bản gốc của link Douyin bất kỳ.
- [Douyin_TikTok_Download_API](https://github.com/Evil0ctal/Douyin_TikTok_Download_API): có thể tự dựng API tải; chưa có bằng chứng trả chất lượng cao hơn. Thêm máy chủ và bảo trì chưa có lợi ích rõ ràng cho app Windows dùng cá nhân.

## Điều kiện trước khi tích hợp

1. Thử endpoint chất lượng cao TikHub trên đúng hai video mẫu; xác nhận ID video và đường dẫn media thực tế.
2. Đo độ phân giải, FPS, codec, âm thanh, bitrate; kiểm tra hình ảnh thực tế khi file khác. File lớn hơn hoặc có nhãn “original” chưa đủ kết luận tốt hơn.
3. Kiểm tra đủ hai bìa do tác giả thiết kế, so sánh kích thước và hình ảnh; không thay bằng frame video hoặc ảnh cắt/nội suy.
4. Chỉ nhận phụ đề nguồn nếu có dữ liệu/file có mốc thời gian; không thêm OCR hoặc ASR.
5. Nếu API có lợi ích, thiết kế thành nguồn tùy chọn, lưu khóa riêng bằng cơ chế bảo vệ của Windows và dùng lại bộ tải có tiếp tục tải dở. Không đưa khóa vào log hoặc bản chia sẻ; không gửi token Google/cookie cá nhân sang API.

Với bằng chứng hiện tại, API miễn phí có thể hữu ích như một nguồn dự phòng lấy link, nhưng chưa đáng thay bộ tải hiện tại chỉ vì mục tiêu nâng chất lượng.
