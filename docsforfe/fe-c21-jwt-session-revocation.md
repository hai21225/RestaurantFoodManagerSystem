# FE-C21 — JWT cũ còn hiệu lực sau khi khóa tài khoản hoặc thu quyền

| Nội dung | Giá trị |
|---|---|
| Ngày ghi nhận | 2026-11-01 |
| Người ghi nhận | Đông — tích hợp frontend F6 |
| Người xử lý đề xuất | Hải — backend |
| Ưu tiên | **P0 — kiểm soát truy cập** |
| Trạng thái | **Đã tái hiện; chờ backend sửa, chưa nghiệm thu bản sửa** |
| Mốc mã nguồn | `dong`, commit `ef0327f` |
| Môi trường đã kiểm tra | `Auth:Enforce=true`, database riêng `RestaurantFoodManager_Integration_20261029` |

## 1. Vấn đề

Nhân viên đã đăng nhập được cấp JWT chứa các vai trò tại thời điểm đăng nhập.
Khi Admin khóa tài khoản, thu một vai trò hoặc đặt lại mật khẩu, JWT đã cấp trước đó vẫn được backend chấp nhận.
Người dùng có thể tiếp tục gọi API bằng phiên cũ khi token còn hạn.

Ẩn menu hoặc xóa token ở frontend chỉ tác động trình duyệt đó, không thu hồi token trên thiết bị/phiên khác.
Giao diện Nhân viên đã có thông báo giới hạn này; đây vẫn là việc backend cần xử lý.

## 2. Kết quả thực tế đã tái hiện

| Thao tác Admin | Đăng nhập mới | Dùng JWT đã cấp trước thao tác |
|---|---|---|
| Khóa tài khoản | Bị chặn: login trả **401** | `GET /api/menu` vẫn trả **200** |
| Thu vai trò Chef khỏi người có Waiter + Chef | JWT mới chỉ còn Waiter; đọc Bếp bị **403** | JWT Chef cũ đọc `GET /api/management/kitchen/orders?status=open` vẫn **200** |
| Đặt lại mật khẩu | Mật khẩu cũ bị **401**, mật khẩu mới đăng nhập được | JWT cũ gọi `GET /api/menu` vẫn **200** |

Các kết quả trên thuộc lượt **13/13 kiểm tra API F6 Nhân viên/Vai trò** đã chạy.
PASS ở đây nghĩa là đã kiểm chứng được hành vi hiện tại, **không có nghĩa lỗi thu hồi JWT đã được sửa**.
Xem [bàn giao F6 phần 2](f6-staff-progress.md).

## 3. Cách tái hiện để Hải kiểm tra

Dùng tài khoản và database kiểm thử riêng; không khóa/reset tài khoản nghiệp vụ thật hoặc Admin cuối cùng.
Giữ riêng JWT của Admin và JWT của nhân viên, không ghi token/mật khẩu vào Git hoặc log bàn giao.

### A. Khóa tài khoản nhưng phiên cũ vẫn gọi API

1. Nhân viên thử đăng nhập bằng `POST /api/auth/login`; giữ JWT là **T0**.
2. Dùng JWT **Admin khác** gọi `PATCH /api/users/{userId}/active?active=false` — **query, không body**.
3. Dùng **T0** gọi `GET /api/menu`: hiện tại vẫn **200**.
4. Đăng nhập mới bằng tài khoản đã khóa: **401**. Sau kiểm tra mở lại bằng `?active=true`.

### B. Thu vai trò nhưng token cũ vẫn có quyền Bếp

1. Cho tài khoản thử có Waiter + Chef, đăng nhập và giữ JWT **T0**.
2. Dùng JWT Admin gọi `DELETE /api/users/{userId}/roles/Chef`.
3. Dùng **T0** gọi `GET /api/management/kitchen/orders?status=open`: hiện tại vẫn **200**.
4. Nhân viên đăng nhập lại để nhận JWT **T1**; gọi cùng API bằng **T1**: **403**.
5. Khôi phục vai trò thử nếu cần bằng `POST /api/users/{userId}/roles`, body `{ "roleName": "Chef" }`.

### C. Đặt lại mật khẩu nhưng token cũ chưa bị thu hồi

1. Nhân viên thử đăng nhập, giữ JWT **T0**.
2. Dùng JWT Admin gọi `POST /api/users/{userId}/reset-password`, body `{ "newPassword": "<mật-khẩu-thử-mới>", "confirmPassword": "<mật-khẩu-thử-mới>" }`.
3. Mật khẩu cũ không đăng nhập mới được, mật khẩu mới đăng nhập được.
4. Dùng **T0** gọi `GET /api/menu`: hiện tại vẫn **200**.

## 4. Vị trí backend cần rà soát

| File / phương thức | Điều đã đối chiếu |
|---|---|
| [Program.cs](../RestaurantFoodManagerSystem/Program.cs) — AddJwtBearer | Kiểm tra chữ ký/issuer/audience/thời hạn; chưa có bước đối chiếu thu hồi phiên/trạng thái user hiện tại trong cấu hình này |
| [TokenService.cs](../RestaurantFoodManagerSystem/Application/Services/Auth/TokenService.cs) — CreateToken | Đưa role vào claim khi tạo JWT; có jti/iat/exp, chưa có token version/security stamp để đối chiếu thu hồi |
| [UserService.cs](../RestaurantFoodManagerSystem/Application/Services/User/UserService.cs) — SetActiveAsync | Đổi IsActive trong DB và bảo vệ tự khóa/Admin cuối; chưa thu hồi JWT đã cấp |
| [AuthService.cs](../RestaurantFoodManagerSystem/Application/Services/Auth/AuthService.cs) — RemoveRoleFromUserAsync / ResetPasswordAsync | Xóa liên kết role hoặc đổi hash mật khẩu; chưa thu hồi JWT cũ |

## 5. Hành vi cần đạt và hướng xử lý đề xuất

- **Sau khi khóa:** request bảo vệ tiếp theo bằng JWT của tài khoản đó bị từ chối **401**; mở khóa không làm token đã bị thu hồi tự hợp lệ trở lại.
- **Sau reset mật khẩu:** các token đã cấp trước khi reset không tiếp tục truy cập API bảo vệ; mật khẩu mới đăng nhập và nhận token mới được.
- **Sau thu vai trò:** JWT cũ không được gọi API của vai trò đã thu. Có thể trả **401** nếu thu hồi token và yêu cầu đăng nhập lại, hoặc **403** nếu kiểm tra quyền hiện tại theo từng request.
- **Gán thêm vai trò:** chốt rõ thời điểm cập nhật claim/quyền; FE hiện hướng dẫn đăng nhập lại để nhận JWT mới.
- Các lỗi vẫn theo hợp đồng `{ "message": "..." }`; không thu hồi nhầm phiên của user khác.

Đề xuất Hải chọn cơ chế có trạng thái bền vững, ví dụ token version/security stamp theo user trong DB
và kiểm tra ở bước xác thực request. Khi khóa/reset/đổi role thì cập nhật version theo chính sách đã chốt.
Nếu chọn kiểm tra role trực tiếp từ DB, cần bổ sung cách vô hiệu hóa token cũ khi reset mật khẩu.
Rà soát cả đổi mật khẩu cá nhân để thống nhất chính sách; luồng đó chưa được tái hiện riêng trong ghi chú này.

## 6. Checklist nghiệm thu sau khi sửa

- [ ] Tái hiện lại A/B/C: các JWT cũ bị chặn đúng theo mục 5, kiểm tra trên ít nhất hai phiên độc lập.
- [ ] User bị khóa không đăng nhập mới; mở khóa và đăng nhập mới hoạt động, JWT đã thu hồi vẫn bị chặn.
- [ ] Reset mật khẩu: mật khẩu cũ không đăng nhập; mật khẩu mới đăng nhập được; token cũ không truy cập được.
- [ ] Thu Chef không ảnh hưởng Waiter còn lại; không thu hồi nhầm token của Admin/người dùng khác.
- [ ] Dữ liệu thu hồi không mất hiệu lực sau khi backend khởi động lại; kiểm tra đa instance nếu có sử dụng.
- [ ] Bảo vệ tự khóa và Admin đang hoạt động cuối cùng vẫn đúng.
- [ ] FE kiểm tra lại thao tác với chính mình: sau phản hồi ghi thành công có thể nhận 401/403 khi tải lại danh sách Admin; chuyển đăng nhập/thông báo phù hợp, không tự gửi lại lệnh ghi đã thành công.
- [ ] Thêm/chạy test backend và kiểm tra API thật cho token trước/sau thay đổi; cập nhật kết quả thực chạy rồi mới đánh dấu hoàn tất.

## 7. Phạm vi cập nhật hiện tại

Ghi chú này là yêu cầu bàn giao backend, chưa sửa C#, migration hoặc cấu hình.
Danh sách theo dõi: [fe-work-plan.md — FE-C21](fe-work-plan.md).
Frontend F6 và kết quả kiểm tra trước đó được ghi trong [f6-staff-progress.md](f6-staff-progress.md).
