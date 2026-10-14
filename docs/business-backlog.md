# Business Backlog - PawStay

Tài liệu này ghi nhận các vấn đề nghiệp vụ và quy trình vận hành cần hoàn thiện hoặc bổ sung cơ chế tự động hóa trong các phiên bản tiếp theo.

---

## 1. Đơn `ACCEPTED` quá `NgayKetThuc` nhưng Sitter không bấm Hoàn tất

### Hiện trạng và Vấn đề
- Khi đơn đặt lịch đã được Sitter chấp nhận (`TrangThai == "ACCEPTED"`), hệ thống gán `IsActive = true`.
- Chỉ số `IsActive = true` kết hợp với index độc nhất `(PetId, IsActive)` ngăn chặn thú cưng bị đặt trùng lịch nhiều lần trong cùng một khoảng thời gian.
- Hiện tại, chỉ Sitter mới có quyền chuyển đơn từ `ACCEPTED` sang `COMPLETION_PENDING` thông qua endpoint `PATCH /api/sitter/bookings/{id}/complete` sau khi kết thúc dịch vụ (`vnToday > NgayKetThuc`).
- **Rủi ro phát sinh**: Nếu Sitter quên, bỏ bê, hoặc không chủ động đăng nhập để bấm "Hoàn thành", đơn đặt lịch sẽ bị "treo" vĩnh viễn ở trạng thái `ACCEPTED`. Khi đó:
  1. `IsActive` vẫn tiếp tục giữ giá trị `true`, khiến chủ nuôi không thể đặt lịch mới cho thú cưng này (bị chặn bởi unique constraint index).
  2. Chủ nuôi không thể xác nhận hoàn tất hay viết đánh giá/khiếu nại vì đơn chưa chuyển sang `COMPLETION_PENDING`.
  3. Lịch rảnh của Sitter trong khoảng ngày này vẫn bị khóa.

### Đề xuất giải pháp (Nhắc nhở + Timeout)

1. **Cơ chế Nhắc nhở (Notification / Reminder)**:
   - **T+0 (NgayKetThuc)**: Gửi thông báo / email nhắc Sitter: *"Dịch vụ đã đến ngày kết thúc, vui lòng xác nhận hoàn tất và bàn giao thú cưng."*
   - **T+1 đến T+3**: Gửi thông báo nhắc nhở hàng ngày cho Sitter nếu đơn vẫn ở `ACCEPTED`. Đồng thời gửi thông báo cho Chủ nuôi để nắm tình hình.

2. **Cơ chế Tự động xử lý quá hạn (Auto-timeout / Auto-escalate)**:
   - **Sau T+N ngày (ví dụ: N = 7 hoặc 14 ngày)** kể từ `NgayKetThuc`:
     - Nếu Sitter vẫn hoàn toàn không có tương tác:
       - **Phương án A (Tự động chuyển `COMPLETION_PENDING`)**: Hệ thống coi như Sitter đã hoàn tất chăm sóc và chuyển đơn sang `COMPLETION_PENDING` với `SitterCompletedAt = now`, `CompletionDeadline = now + 3 days`, `History: Role = SYSTEM, Action = AUTO_COMPLETION_PENDING`. Chủ nuôi sau đó có 3 ngày để duyệt hoặc khiếu nại.
       - **Phương án B (Tự động chuyển vào hàng đợi tranh chấp `DISPUTED` / Cần can thiệp CSKH)**: Do việc không bàn giao thú cưng là rủi ro an toàn nghiêm trọng, tự động đánh dấu để Admin/CSKH liên hệ trực tiếp Sitter và Chủ nuôi qua điện thoại trước khi đóng đơn.
   - Khi hoàn tất hoặc hủy qua cơ chế timeout, hệ thống sẽ nhả `IsActive = false`, giải phóng thú cưng để chủ nuôi có thể tiếp tục đặt các dịch vụ khác.

3. **Cơ chế cho phép Chủ nuôi yêu cầu can thiệp**:
   - Nếu quá `NgayKetThuc + 1 ngày` mà Sitter chưa bấm hoàn tất, hiển thị thêm nút *"Yêu cầu hỗ trợ / Báo chưa nhận thú cưng"* trên giao diện Chủ nuôi (`my-bookings.html`) để Admin tiếp nhận xử lý ngay mà không phải chờ timeout dài hạn.

---

## 2. Quy trình xử lý hoàn tiền và bồi hoàn khi tranh chấp (Dispute Settlement)
- Hiện tại: Admin duyệt `CANCELLED` sẽ nhả `IsActive=false`.
- Tương lai: Tích hợp cổng thanh toán trực tuyến hoặc quy trình hoàn cọc/phạt Sitter nếu vi phạm thỏa thuận dịch vụ.

---

## 3. Hệ thống Thông báo (In-app Notification & Email) khi thay đổi trạng thái đơn
- **Đã hoàn thành ở Bước 4**: Hệ thống thông báo in-app (chuông thông báo, bộ đếm chưa đọc, danh sách phân trang con trỏ `before`, đánh dấu đã đọc/tất cả, phân quyền điều hướng thông minh theo vai trò và sự kiện, cơ chế DedupeKey chống trùng lặp, quét nhắc hạn lười theo request).
- **Tồn đọng tương lai**: Tích hợp thêm kênh gửi Email tự động và thông báo đẩy (Web Push / SignalR real-time).

---

## 4. Thống kê `GetStatsAsync`: Lọc tài khoản Admin và tài khoản bị vô hiệu hóa / đã xóa
- **Hiện trạng:** Trong `SitterService.GetStatsAsync()`, số lượng người dùng hiện đếm toàn bộ `Users.CountDocumentsAsync(_ => true)`.
- **Rủi ro:** Bao gồm cả các tài khoản nội bộ Admin, tài khoản test, hoặc các tài khoản bị khóa/soft-deleted (`IsActive == false`), làm số liệu thống kê công khai trên trang chủ bị sai lệch so với lượng người dùng thực tế.
- **Đề xuất cải tiến:**
  - Lọc điều kiện tài khoản hợp lệ: `Users.CountDocumentsAsync(u => u.Role != "ADMIN" && u.IsActive != false)`.
  - Tương tự với Sitter: chỉ tính các hồ sơ đang hoạt động và không bị tạm khóa.

---

## 5. BackgroundService quét định kỳ (Periodic Background Worker cho Quét Hết Hạn & Nhắc Hạn)
- **Hiện trạng Bước 4**: Hệ thống sử dụng cơ chế **Lazy Evaluation** (quét lười) tích hợp vào các request API:
  - Khi người dùng (Owner hoặc Sitter) thực hiện các thao tác booking hoặc mở danh sách/chi tiết đơn, `ProcessLazyBookingUpdatesAsync` sẽ quét các đơn hết hạn (`ExpiresAt <= now` -> `EXPIRED`, `CompletionDeadline <= now` -> `AUTO_COMPLETED`) và tạo thông báo kèm đối soát 7 ngày.
  - Khi người dùng tải danh sách thông báo hoặc kiểm tra số lượng chưa đọc, `ScanAndGenerateRemindersAsync` được gọi kèm cơ chế throttle 5 phút/user để sinh các thông báo nhắc hạn.
- **Hạn chế của cơ chế Lazy**:
  - Nếu cả Chủ nuôi và Người chăm sóc không đăng nhập hoặc không phát sinh request đọc trong một khoảng thời gian dài, trạng thái đơn chưa được lật trong DB và thông báo in-app chưa được tạo kịp thời vào đúng thời điểm hết hạn.
- **Kế hoạch triển khai BackgroundService**:
  - Xây dựng một `IHostedService` / `BackgroundService` trong ASP.NET Core (ví dụ: `BookingMaintenanceBackgroundService`).
  - Thiết lập chu kỳ chạy ngầm độc lập (ví dụ: mỗi 5 đến 15 phút):
    1. Quét toàn bộ DB để chuyển trạng thái các đơn `PENDING` quá hạn 48h sang `EXPIRED`.
    2. Chuyển các đơn `COMPLETION_PENDING` quá `CompletionDeadline` sang `COMPLETED` (`AUTO_COMPLETED`).
    3. Tạo thông báo in-app và gửi Email/Push notification chủ động tới cả hai bên ngay khi hết hạn mà không cần chờ người dùng tương tác.
    4. Quét nhắc nhở cho các đơn sắp hết hạn và đơn `ACCEPTED` quá ngày kết thúc.
  - Sử dụng hàng đợi phân tán hoặc distributed lock (nếu triển khai nhiều instance backend) để tránh xung đột giữa các background workers.
