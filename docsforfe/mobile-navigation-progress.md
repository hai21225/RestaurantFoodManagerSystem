# Điều hướng điện thoại — Tổng quan và Quản trị

Cập nhật 2026-11-01 trên `dong`, theo phản hồi thanh menu điện thoại phải kéo ngang và bị cắt mục.

## Đã chỉnh

- Ở rộng tối đa 760px, thanh dưới có hai nút lớn **Tổng quan** và **Quản trị**, không cuộn ngang.
- **Tổng quan** mở 6 mục: Tổng quan, Bàn ăn, Đặt món, Bếp, Thanh toán và Món ăn.
- **Quản trị** giữ các mục quản lý; Báo cáo tiếp tục nằm trong Quản trị.
  Các mục Nhân viên/Phân quyền/Báo cáo và Nguyên liệu/Công thức tiếp tục lọc theo vai trò đã có.
- Mỗi lần chỉ mở một popup trên điện thoại. Đóng khi chọn mục, bấm lại nút nhóm,
  bấm bên ngoài hoặc nhấn Escape; Escape đưa tiêu điểm về nút nhóm.
- Chọn Tổng quan khi đang ở trang chủ cũng đóng popup, không giữ menu bị kẹt.
- Menu có giới hạn chiều cao và cuộn dọc khi cần; chữ dài xuống dòng.
  Chừa chỗ cho thanh dưới và safe-area của thiết bị, có focus rõ khi dùng bàn phím.
- Đổi giữa mobile/desktop tự đồng bộ trạng thái mở/đóng và aria-expanded.
  Trên desktop, 6 mục chính vẫn hiển thị trực tiếp ở sidebar, Quản trị dùng nút mở nhóm.
- Sửa chung `js/common/navigation.js`, `css/layout.css`; cập nhật phiên bản asset ở 13 trang có điều hướng.
  Không thay API hoặc logic nghiệp vụ.

## Kiểm tra thực chạy

| Kiểm tra | Kết quả |
|---|---|
| 390 × 844 | Hai nút footer hiển thị đủ; menu Tổng quan 6 mục; mở nhóm này đóng nhóm kia |
| 320 × 568 | Thanh menu và popup Quản trị không tràn ngang; đủ 7 mục của Admin |
| 740 × 360 | Popup nằm trong chiều cao màn hình; thanh dưới không cuộn ngang |
| 1280 × 800 | Sidebar có 6 mục chính trực tiếp; toggle Tổng quan ẩn; Quản trị mở riêng |
| Chuyển trang | Menu → Bàn ăn; Quản trị → Nguyên liệu/Công thức; quay về Tổng quan đúng đường dẫn |
| Đóng menu | Click ngoài, chọn lại trang chủ, Enter/Escape; Escape từ mục con trả focus đúng nút nhóm |
| DOM/asset/cú pháp | Không trùng ID ở trang đã kiểm tra; 34 JS, 15 HTML, 318 đường dẫn tài nguyên đạt |
| Bộ test frontend hiện có | **183/183 đạt**, không bỏ qua |
| git diff --check | Đạt |

Kiểm tra bằng trình duyệt với kích thước CSS nêu trên, chưa kiểm thử trên điện thoại vật lý.
Ảnh và kết quả kiểm tra nằm ngoài Git tại `D:\Hoc_Tap\quanliduan1111\_codex_reports\20261101\mobile-nav-*`.
Sau khi cập nhật source, tải lại trang để nhận phiên bản CSS/JS mới.
