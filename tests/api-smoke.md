# Kịch Bản Kiểm Thử Khói API (API Smoke Test) - PawStay

Tài liệu liệt kê các cặp `Request -> Mã HTTP mong đợi` dùng cho việc kiểm thử thủ công và tự động hóa qua Postman / cURL / Swagger.

---

## 1. Đánh giá chuyến đi (Add Review)

| STT | Phương thức & URL | Header / Quyền | Request Body / Điều kiện | Mã HTTP mong đợi | Response Message / Ghi chú |
| :--- | :--- | :--- | :--- | :---: | :--- |
| 1.1 | `POST /api/bookings/{id}/review` | Bearer Token (USER) | `{ "rating": 5, "comment": "Tốt" }` khi booking không tồn tại | **404 Not Found** | `Booking không tồn tại.` |
| 1.2 | `POST /api/bookings/{id}/review` | Bearer Token (USER khác) | User gọi không phải chủ `OwnerId` của booking | **404 Not Found** | `Không tìm thấy thông tin đặt lịch.` |
| 1.3 | `POST /api/bookings/{id}/review` | Bearer Token (Chủ booking) | Booking chưa ở trạng thái `COMPLETED` (ví dụ PENDING, ACCEPTED) | **400 Bad Request** | `Chỉ có thể đánh giá booking đã hoàn thành.` |
| 1.4 | `POST /api/bookings/{id}/review` | Bearer Token (Chủ booking) | Booking đã có đánh giá trước đó (đã tồn tại trong DB) | **409 Conflict** | `Booking này đã được đánh giá.` |
| 1.5 | `POST /api/bookings/{id}/review` | Bearer Token (Chủ booking) | Đánh giá hợp lệ, booking COMPLETED | **200 OK** | Trả về `ReviewDto` |

---

## 2. Đặt lịch chăm sóc (Create Booking)

| STT | Phương thức & URL | Header / Quyền | Request Body / Điều kiện | Mã HTTP mong đợi | Response Message / Ghi chú |
| :--- | :--- | :--- | :--- | :---: | :--- |
| 2.1 | `POST /api/bookings` | Bearer Token (USER) | `ngayBatDau` trong quá khứ | **400 Bad Request** | `Ngày bắt đầu không được ở quá khứ.` |
| 2.2 | `POST /api/bookings` | Bearer Token (USER) | `ngayKetThuc` < `ngayBatDau` | **400 Bad Request** | `Ngày kết thúc không hợp lệ.` |
| 2.3 | `POST /api/bookings` | Bearer Token (USER) | Có truyền `petId` nhưng Pet không thuộc sở hữu của user | **404 Not Found** | `Không tìm thấy thông tin thú cưng.` |
| 2.4 | `POST /api/bookings` | Bearer Token (USER) | `sitterId` không tồn tại trong hệ thống | **404 Not Found** | `Sitter không tồn tại.` |
| 2.5 | `POST /api/bookings` | Bearer Token (USER) | `sitterId` chưa được Admin duyệt (`Status != "Approved"` hoặc `!IsVerified`) | **400 Bad Request** | `Sitter chưa được duyệt.` |
| 2.6 | `POST /api/bookings` | Bearer Token (USER) | Sitter không mở lịch đủ các ngày đặt trong `SitterAvailabilities` | **409 Conflict** | `Sitter không có lịch rảnh trong toàn bộ khoảng thời gian này.` |
| 2.7 | `POST /api/bookings` | Bearer Token (USER) | Trùng lịch đã được đặt trước đó của Sitter (chồng lấn hoặc unique index) | **409 Conflict** | `Bạn đã có một yêu cầu đang chờ hoặc đã được chấp nhận trùng khoảng thời gian với người chăm sóc này.` |
| 2.8 | `POST /api/bookings` | Bearer Token (USER) | Dữ liệu ngày, sitter, pet hợp lệ | **200 OK** | Trả về `BookingDetailDto` |

---

## 3. Hoàn thành chuyến đi & Nghiệp vụ trung gian (Sitter Complete)

| STT | Phương thức & URL | Header / Quyền | Request Body / Điều kiện | Mã HTTP mong đợi | Response Message / Ghi chú |
| :--- | :--- | :--- | :--- | :---: | :--- |
| 3.1 | `PATCH /api/sitter/bookings/{id}/complete` | Bearer Token (SITTER) | Booking chưa `ACCEPTED` | **400 Bad Request** | `Booking không ở trạng thái đã xác nhận.` |
| 3.2 | `PATCH /api/sitter/bookings/{id}/complete` | Bearer Token (SITTER) | Chưa qua ngày kết thúc lưu trú | **400 Bad Request** | `Chưa thể hoàn thành booking do chưa qua ngày kết thúc.` |
| 3.3 | `PATCH /api/sitter/bookings/{id}/complete` | Bearer Token (SITTER) | Đã qua ngày kết thúc lưu trú, trạng thái `ACCEPTED` | **200 OK** | `Đã hoàn thành booking.` |
| 3.4 | `PATCH /api/sitter/bookings/{id}/complete` | Bearer Token (SITTER khác) | Sitter không phụ trách booking | **404 Not Found** | `Không tìm thấy booking.` |

---

## 4. Nhật ký chăm sóc thú cưng (Stay Updates)

| STT | Phương thức & URL | Header / Quyền | Request Body / Điều kiện | Mã HTTP mong đợi | Response Message / Ghi chú |
| :--- | :--- | :--- | :--- | :---: | :--- |
| 4.1 | `POST /api/sitter/bookings/{id}/updates` | Bearer Token (SITTER) | Booking `id` không tồn tại | **404 Not Found** | `Không tìm thấy booking.` |
| 4.2 | `POST /api/sitter/bookings/{id}/updates` | Bearer Token (SITTER khác) | Sitter không nhận chăm sóc booking này | **404 Not Found** | `Không có quyền với booking này.` |
| 4.3 | `POST /api/sitter/bookings/{id}/updates` | Bearer Token (Sitter của booking) | Booking không ở trạng thái `ACCEPTED` | **400 Bad Request** | `Booking không ở trạng thái ACCEPTED.` |
| 4.4 | `POST /api/sitter/bookings/{id}/updates` | Bearer Token (Sitter của booking) | Không có ghi chú và không có ảnh | **400 Bad Request** | `Cần có ghi chú hoặc ít nhất một ảnh.` |
| 4.5 | `POST /api/sitter/bookings/{id}/updates` | Bearer Token (Sitter của booking) | Đăng quá 3 ảnh | **400 Bad Request** | `Tối đa 3 ảnh.` |
| 4.6 | `POST /api/sitter/bookings/{id}/updates` | Bearer Token (Sitter của booking) | Đã đạt số lượng cập nhật tối đa (`MaxPerBooking` - mặc định 30) | **409 Conflict** | `Đã vượt quá số lượng cập nhật tối đa cho chuyến đi này.` |
| 4.7 | `DELETE /api/sitter/bookings/{id}/updates/{updateId}` | Bearer Token (Sitter khác) | Cập nhật không phải do Sitter này đăng | **404 Not Found** | `Bài đăng không phải của bạn.` |
| 4.8 | `DELETE /api/sitter/bookings/{id}/updates/{updateId}` | Bearer Token (Sitter đăng bài) | Đã quá 24 giờ kể từ khi đăng | **409 Conflict** | `Chỉ có thể xóa bài đăng trong vòng 24 giờ.` |
| 4.9 | `GET /api/bookings/{id}/updates` | Bearer Token (Người ngoài) | Không phải chủ pet, không phải sitter nhận, không phải admin | **404 Not Found** | `Không có quyền xem.` |

---

## 5. Người dùng & Hồ sơ (User & Profile)

| STT | Phương thức & URL | Header / Quyền | Request Body / Điều kiện | Mã HTTP mong đợi | Response Message / Ghi chú |
| :--- | :--- | :--- | :--- | :---: | :--- |
| 5.1 | `PUT /api/auth/profile` | Bearer Token (USER) | Tên ngắn hơn 2 hoặc dài hơn 60 ký tự | **400 Bad Request** | `Tên phải từ 2 đến 60 ký tự.` |
| 5.2 | `PUT /api/auth/profile` | Bearer Token (USER) | Số điện thoại không hợp lệ (không đúng format 10 số bắt đầu bằng 0) | **400 Bad Request** | `Số điện thoại không hợp lệ (Bắt đầu bằng 0 và gồm 10 số).` |
| 5.3 | `POST /api/auth/change-password` | Bearer Token (USER) | `currentPassword` sai | **400 Bad Request** | `Mật khẩu hiện tại không đúng.` |
| 5.4 | `POST /api/auth/change-password` | Bearer Token (USER) | `newPassword` ngắn hơn 8 ký tự | **400 Bad Request** | `Mật khẩu mới phải có ít nhất 8 ký tự.` |
| 5.5 | `POST /api/auth/change-password` | Bearer Token (USER) | `newPassword` trùng `currentPassword` | **400 Bad Request** | `Mật khẩu mới phải khác mật khẩu hiện tại.` |
| 5.6 | `PUT /api/sitter/availability` | Bearer Token (SITTER) | Cố gắng đổi ngày đã có khách đặt thành "Không nhận" | **409 Conflict** | `Một số ngày đã có người đặt, không thể đổi thành bận: ...` |
