# Lưu trữ bản trình duyệt

browser-v022 chứa mã của bản Node + Chromium extension trước khi chuyển sang app Windows. Bản này không thuộc build/đường chạy app 0.3. Node runtime và node_modules ở đây chỉ phục vụ khôi phục. Bản sao source độc lập còn ở data/rebuild-backups/before-native-20261003/browser-v022.zip.

Không tiếp tục phát triển hoặc tải extension từ khu này. Dữ liệu cũ data/state.json giữ nguyên ở gốc; chưa import quyền/đích trình duyệt vào OAuth. Lúc chuyển đổi đã kiểm tra dữ liệu schema 2 có 0 job, vì vậy không có công việc đang dở cần chuyển. Với một bản dữ liệu khác có job, phải đối soát Sheets/Studio và thiết kế chuyển đổi riêng trước khi chạy lại; không autoqueue.

Chỉ xóa khu lưu trữ sau khi tải Douyin và OAuth/Sheets/YouTube đã được nghiệm thu trên tài khoản thật. Không xóa video người dùng cùng mã ứng dụng.
