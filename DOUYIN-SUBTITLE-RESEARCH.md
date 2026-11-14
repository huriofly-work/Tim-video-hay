# Khả năng tải phụ đề nguồn Douyin — 03/10/2026

Quyết định: chưa phát triển chức năng tải phụ đề Douyin. Người dùng chỉ muốn file phụ đề do nguồn cung cấp; không OCR, nhận dạng giọng nói hoặc dịch/tạo phụ đề. Chỉ xem xét lại khi tìm được video Douyin thực tế cung cấp file phụ đề hoặc dữ liệu câu chữ có thời gian, tải được qua luồng local.

## Kết quả kiểm tra trực tiếp

Kiểm tra sáu video `7659702794928147739`, `7673782911702748458`, `7449263169505086746`, `7559056316510850340`, `7534679152504376595`, `7684959632527248683` qua detail API web (aid=6383) và Douyin Select (aid=568863), dùng cookie đã lưu. Cả 12 response trả đúng aweme_id. Không tìm thấy trường dữ liệu phụ đề theo thời gian (`subtitle`, `auto_captions`, `caption_infos`, `auto_video_caption_info`, `utterances`). Không lưu cookie hoặc URL CDN trong bằng chứng: `test-output/douyin-subtitle-feasibility.json`.

File MOV đã tải của video bọ ngựa không có stream subtitle qua ffprobe. Trường caption của hai video người dùng cung cấp là nội dung bài đăng/hashtag, không phải lời thoại có thời gian. Không tải thêm video trong đợt nghiên cứu này.

Kết quả chỉ chứng minh các nguồn và mẫu đã kiểm tra; không chứng minh mọi video, phiên bản app hoặc endpoint nội bộ Douyin đều không có dữ liệu phụ đề.

## Nguồn đối chiếu

- [Câu hỏi về AI 文稿 trên diễn đàn Douyin](https://developer.open-douyin.com/forum/question/post/65e2e90b30865fd39dd3c684): mô tả chức năng bản chép lời trong app; trang đọc được không hiển thị câu trả lời xác nhận API xuất phụ đề.
- [Mã nguồn yt-dlp](https://github.com/yt-dlp/yt-dlp/blob/master/yt_dlp/extractor/tiktok.py): Douyin kế thừa bộ đọc TikTok; có mã xử lý phụ đề không phải bằng chứng Douyin thực tế trả các trường đó. Đề xuất triển khai ban đầu dựa trên cơ chế dùng chung là chưa đủ cơ sở.
- [Báo cáo thử nghiệm của tác giả ai-video-downloader](https://github.com/Shaun520/ai-video-downloader/blob/main/docs/%E6%8A%96%E9%9F%B3%E5%AD%97%E5%B9%95%E6%8F%90%E5%8F%96%E4%B8%8Ettwid-Cookie%E4%BF%AE%E5%A4%8D%E8%AE%B0%E5%BD%95.md): ngày 14/09/2026 tác giả báo cáo trên 80 video không có video.subtitle, không tìm được track phụ đề qua web/share; sau đó dùng ASR. Đây là kết quả của dự án đó, không phải cam kết chính thức của Douyin.
- [Douyin Subtitles Downloader của BibiGPT](https://bibigpt.co/features/douyin-subtitle-downloader): dịch vụ tự mô tả việc tạo SRT bằng AI chép âm thanh. Không dùng lời quảng cáo tải phụ đề làm bằng chứng có file phụ đề nguồn Douyin.

## Phạm vi chương trình

Giữ bản Windows 0.3.3 và luồng video/ảnh bìa hiện tại. Không thêm nút, trạng thái, dependency hoặc request phụ đề khi chưa có mẫu tải thành công. Hỗ trợ phụ đề nguồn cho YouTube/Bilibili được đánh giá riêng khi phát triển adapter các nền tảng đó.
