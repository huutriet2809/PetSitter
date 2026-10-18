# Sổ Tay Vận Hành Thanh Toán & Đối Soát (Payments Runbook)

Tài liệu này quy định các hằng số tài chính, quy trình đối soát tự động, xử lý ngoại lệ thanh toán PayOS và hướng dẫn vận hành hoàn tiền trên hệ thống PawStay.

---

## 1. Các Hằng Số Tài Chính & Tham Số Hệ Thống

| Tham số | Giá trị | Giải thích |
|---|---|---|
| **Grace Period (Ân hạn)** | **5 phút** | Thời gian ân hạn sau khi `Payment.ExpiresAt` hết hạn trước khi hệ thống hủy Payment Link và chuyển trạng thái đơn sang EXPIRED. Tránh hủy nhầm khi người dùng đang quét mã chuyển khoản. |
| **Earnings Escrow Hold** | **48 giờ** | Số tiền thu nhập của Sitter được giữ ở trạng thái `Pending` trong 48 giờ kể từ khi hoàn tất dịch vụ, đề phòng Chủ nuôi khiếu nại (Dispute). Sau 48 giờ không có khiếu nại, số dư chuyển sang `Available`. |
| **Cancellation Threshold >= 48h (Nấc 1)** | **>= 48 giờ** | Hủy trước giờ bắt đầu (00:00:00 GMT+7) từ 48 giờ trở lên: Hoàn 100% tiền cho Chủ nuôi. `PaymentStatus = REFUND_PENDING`. |
| **Cancellation Threshold 24h-48h (Nấc 2)** | **24 - 48 giờ** | Hủy từ 24 đến 48 giờ: Hoàn 50% cho Chủ nuôi, 50% đền bù cho Sitter (Sitter chịu hoa hồng 10% trên phần đền bù). `PaymentStatus = PARTIALLY_REFUNDED`. |
| **Cancellation Threshold < 24h (Nấc 3)** | **< 24 giờ** | Hủy sát giờ (< 24h): Hoàn 0% cho Chủ nuôi, 100% đền bù cho Sitter (trừ hoa hồng sàn). `PaymentStatus = RELEASED`. |
| **Sitter Cancellation** | **Mọi thời điểm** | Sitter hủy: Hoàn 100% tiền cho Chủ nuôi. `PaymentStatus = REFUND_PENDING`. |
| **Platform Commission** | **1,000 bps (10%)** | Hoa hồng sàn tính trên doanh thu giải ngân hoặc tiền đền bù của Sitter. |
| **Minimum PayOS Amount** | **2,000 VND** | Số tiền đơn hàng tối thiểu mà cổng thanh toán PayOS chấp nhận tạo link VietQR. |
| **Webhook Window** | **15 phút** | Giới hạn lệch thời gian webhook cho phép để chống replay attack. |
| **Reconcile Lookback** | **7 ngày** | Cửa sổ thời gian rà soát đơn hàng trong luồng đối soát tự động bù bút toán sổ cái. |
| **Reconcile Throttle** | **30 giây** | Giới hạn tần suất chạy đối soát nền tự động để bảo vệ I/O cơ sở dữ liệu. |

---

## 2. Hệ Thống Khóa IdempotencyKey Sổ Cái Kép

Tất cả các bút toán sổ cái kép tuân thủ quy tắc một hàm sinh IdempotencyKey thống nhất (`LedgerIdempotencyKeys`), dùng chung cho cả luồng ghi nhận chính và luồng đối soát tự sửa bù:

1. **Ghi giữ tiền tạm giữ (Payment Held)**: `{BookingId}:PAYMENT_HELD`
   - DEBIT `GATEWAY_CLEARING`
   - CREDIT `ESCROW_HELD`
2. **Giải ngân hoàn tất đơn (Escrow Release)**: `{BookingId}:ESCROW_RELEASE`
   - DEBIT `ESCROW_HELD` (Tổng tiền)
   - CREDIT `PLATFORM_REVENUE` (Hoa hồng 10%)
   - CREDIT `SITTER_EARNINGS:{SitterUserId}` (Thu nhập sau hoa hồng, AvailableAt = +48h)
3. **Hủy nấc 1 (>=48h) hoặc Sitter hủy**: `{BookingId}:CANCEL_OWNER_100` / `{BookingId}:CANCEL_SITTER`
   - DEBIT `ESCROW_HELD` (100%)
   - CREDIT `REFUND_PENDING` (100%)
4. **Hủy nấc 2 (24h - 48h)**: `{BookingId}:CANCEL_OWNER_50`
   - DEBIT `ESCROW_HELD` (100%)
   - CREDIT `REFUND_PENDING` (50%)
   - CREDIT `PLATFORM_REVENUE` (Hoa hồng trên phần 50% của Sitter)
   - CREDIT `SITTER_EARNINGS:{SitterUserId}` (Phần còn lại sau hoa hồng)
5. **Hủy nấc 3 (<24h)**: `{BookingId}:CANCEL_OWNER_0`
   - DEBIT `ESCROW_HELD` (100%)
   - CREDIT `PLATFORM_REVENUE` (Hoa hồng 10%)
   - CREDIT `SITTER_EARNINGS:{SitterUserId}` (90%)
6. **Hoàn tiền thực tế chuyển khoản xong**: `{RefundId}:REFUND_COMPLETED`
   - DEBIT `REFUND_PENDING`
   - CREDIT `GATEWAY_CLEARING`
7. **Thanh toán muộn cho đơn đã đóng**: `{BookingId}:LATE_PAYMENT_REFUND`
   - DEBIT `GATEWAY_CLEARING`
   - CREDIT `REFUND_PENDING`
8. **Phân xử tranh chấp (Dispute Split)**: `{BookingId}:DISPUTE_SPLIT`
   - DEBIT `ESCROW_HELD` (Tổng tiền)
   - CREDIT `REFUND_PENDING` (Phần hoàn cho Owner)
   - CREDIT `PLATFORM_REVENUE` (Hoa hồng trên phần Sitter)
   - CREDIT `SITTER_EARNINGS:{SitterUserId}` (Phần thực nhận của Sitter)

---

## 3. Quy Trình Vận Hành Đối Soát Tự Động (Reconciliation)

- **Cơ chế tự sửa bù (Self-healing)**:
  Hàm `ReconcileMissingLedgerEntriesAsync` rà soát tất cả các đơn có `PaymentStatus` là `RELEASED`, `REFUND_PENDING`, hoặc `PARTIALLY_REFUNDED` trong vòng 7 ngày gần nhất.
  Nếu phát hiện đơn đã claim thành công trạng thái thanh toán đích nhưng sổ cái bị thiếu bút toán (ví dụ do sập server hoặc mạng ngắt giữa chừng), hệ thống sẽ tái tạo lại chính xác bút toán sổ cái theo đúng loại và ghi vào `JournalEntries` bằng cùng một IdempotencyKey chuẩn.
- **Tính lũy đẳng (Idempotency)**:
  Chạy đối soát lặp lại nhiều lần trên tập dữ liệu đã đủ sổ sẽ tạo ra **0** bút toán mới. Nếu cố ý xóa 1 bút toán, đối soát sẽ bù đúng **1** bút toán bị thiếu.

---

## 4. Xử Lý Giao Dịch Bất Thường (Abnormal Webhooks)

- Khi PayOS gửi webhook có chữ ký số HMAC-SHA256 hợp lệ nhưng **số tiền thanh toán (`data.amount`) không khớp** với số tiền của đơn hàng (`payment.Amount` hoặc `booking.TotalAmount`):
  1. Đánh dấu `Payment.IsAbnormal = true` và `Payment.Status = "ABNORMAL"`.
  2. Ghi nhận `ReceivedAmount` và `AbnormalReason`.
  3. Gửi thông báo khẩn cấp tới tất cả Quản trị viên (`NotificationType.PAYMENT_ABNORMAL`).
  4. Trả về HTTP `200 OK` cho PayOS để tránh PayOS retry liên tục.
  5. **Tuyệt đối KHÔNG ghi nhận doanh thu hoặc giải ngân** cho giao dịch bất thường này.

---

## 5. Quy Trình Hoàn Tiền Thủ Công (Manual Refund Flow)

- Khi yêu cầu hoàn tiền (`RefundRequest`) được Admin xử lý:
  1. Admin chuyển khoản hoàn tiền cho Chủ nuôi theo thông tin STK trong yêu cầu hoặc tài khoản nhận hoàn đã cung cấp.
  2. Admin nhập mã tham chiếu giao dịch ngân hàng (`BankTransactionRef`).
  3. Hệ thống chặn Admin tự duyệt hoàn tiền cho đơn của chính mình (Owner hoặc Sitter).
  4. Claim nguyên tử `RefundRequest` sang `COMPLETED`.
  5. Ghi sổ `REFUND_PENDING -> GATEWAY_CLEARING` (idempotent theo `RefundId`).
  6. Cập nhật `Booking.PaymentStatus` sang `REFUNDED` (hoặc `PARTIALLY_REFUNDED`).
  7. Ghi nhật ký kiểm toán (Audit log) kèm mã tham chiếu và timestamp `nowUtc`.
  8. Gửi thông báo có `TargetRole: "OWNER"` và `DedupeKey` cho Chủ nuôi.

---

## 6. Lưu Ý Về Cổng Thanh Toán PayOS Portal

- Tài liệu tích hợp PayOS chính thức:
  - Webhook: https://payos.vn/docs/du-lieu-tra-ve/webhook/
  - API tạo link thanh toán: https://payos.vn/docs/
- **Lưu ý kiểm chứng giao diện PayOS Portal**:
  Các bước thiết lập thủ công trên giao diện quản trị `my.payos.vn` (như cấu hình Kênh chi, Kênh đối tác, Mô hình ví nạp trước - Prefunded Balance) thuộc quyền kiểm soát của đối tác thứ ba và chưa được kiểm chứng tự động từ môi trường AI. **Cần chủ tài khoản kiểm tra trực tiếp trên portal của PayOS** để kích hoạt các tính năng chi tiền tự động nếu muốn nâng cấp từ hoàn tiền thủ công sang webhook chi tiền tự động.
