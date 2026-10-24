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

---

## 7. Quy Trình Chi Tiền Rút Sitter Thủ Công (SOP Manual Payout)

Hệ thống mặc định vận hành rút tiền ở chế độ **THỦ CÔNG (MANUAL)** để đảm bảo an toàn tuyệt đối nguồn tiền.

### 7.1. Chu trình trạng thái yêu cầu rút tiền (Withdrawal Lifecycle)
1. **REQUESTED (Chờ duyệt)**: Sitter tạo yêu cầu. Khóa nguyên tử số tiền vào `EarningsAccounts.ReservedAmount`. **KHÔNG ghi sổ cái ở bước này**. Khả dụng rút = `Tổng ghi có SITTER_EARNINGS (AvailableAt <= nowUtc) - Tổng đã PAID - ReservedAmount`.
2. **APPROVED (Đã duyệt)**: Admin kiểm tra tính hợp lệ và duyệt yêu cầu. Hệ thống chặn Admin tự duyệt yêu cầu rút tiền của chính mình.
3. **PROCESSING (Nhận xử lý chi - Claim Processing)**:
   - Admin bấm "Nhận xử lý chi" (`POST /admin/withdrawals/{id}/claim-processing`).
   - Claim nguyên tử gán `ClaimedByAdminUserId` và `ClaimedAt`.
   - Nếu Admin B cố bấm cùng lúc, hệ thống chặn với mã lỗi 409 Conflict: *"Yêu cầu đang được xử lý bởi Admin X"*.
4. **PAID (Đã chuyển tiền)**:
   - Admin thực hiện chuyển khoản ngoài hệ thống ngân hàng (Vietcombank, MB, Techcombank,...).
   - Nhập mã giao dịch ngân hàng duy nhất (`BankTransactionRef`).
   - Hệ thống kiểm tra tính duy nhất của `BankTransactionRef` (chống nhập trùng).
   - Bút toán sổ cái kép chính thức được ghi:
     - **DEBIT** `SITTER_EARNINGS:{SitterUserId}` (Amount)
     - **CREDIT** `GATEWAY_CLEARING` (Amount)
   - Nhả `ReservedAmount` tương ứng trên `EarningsAccount`.
5. **REJECTED / CANCELLED / FAILED**:
   - Chỉ giải phóng `ReservedAmount` trở lại số dư khả dụng của Sitter.
   - **0 bút toán sổ cái nào được sinh ra**.

---

## 8. Quy Trình Rút Doanh Thu Nền Tảng (Platform Revenue Sweep)

- **Nguyên tắc quản lý dòng tiền sàn**:
  Toàn bộ doanh thu hoa hồng 10% được hạch toán vào tài khoản sổ cái `PLATFORM_REVENUE`.
- **Tài liệu chốt nguyên tử (`PlatformAccount`)**:
  - Ghi nhận `SweptTotal` lũy kế và `Version` để chống xung đột cập nhật đồng thời (Optimistic Concurrency Control).
  - Khả dụng rút sàn (`TransferableAmount`):
    `TransferableAmount = Net(PLATFORM_REVENUE) - SweptTotal - RevenueReserveBuffer`.
- **Khoản đệm an toàn (`RevenueReserveBuffer`)**:
  - Tính bằng tổng doanh thu các đơn COMPLETED trong cửa sổ `RevenueReserveHours` (mặc định 48 giờ) gần nhất.
  - **Lý do đệm đúng theo thiết kế**:
    1. Phòng ngừa hoàn tiền thủ công các phát sinh sau chuyến đi.
    2. Dự phòng giải quyết giao dịch bất thường trong tài khoản `SUSPENSE`.
    3. Bù trừ sai số phí cổng thanh toán (`GATEWAY_FEES`).
- **Bút toán sổ cái kép khi Sweep**:
  - **DEBIT** `PLATFORM_REVENUE` (Amount)
  - **CREDIT** `GATEWAY_CLEARING` (Amount)

---

## 9. Xử Lý Giao Dịch Bất Thường (SUSPENSE)

Giao dịch bất thường là giao dịch chuyển khoản vào cổng PayOS nhưng sai số tiền hoặc không tìm thấy đơn tương ứng, được ghi nhận vào tài khoản `SUSPENSE`.

1. **Phương án 1: Gán vào đơn đặt lịch hợp lệ (`AssignSuspensePaymentAsync`)**:
   - Admin nhập `BookingId` và ghi chú giải trình.
   - Ghi sổ:
     - **DEBIT** `SUSPENSE` (ReceivedAmount)
     - **CREDIT** `ESCROW_HELD` (ReceivedAmount)
   - Cập nhật `Payment.Status = PAID`, `Payment.IsAbnormal = false`, `Payment.BookingId = bookingId`.
2. **Phương án 2: Hoàn tiền cho người chuyển (`RefundSuspensePaymentAsync`)**:
   - Admin nhập lý do hoàn tiền.
   - Tạo yêu cầu hoàn tiền `RefundRequest` với `Status = REFUND_PENDING`.
   - Ghi sổ:
     - **DEBIT** `SUSPENSE` (ReceivedAmount)
     - **CREDIT** `REFUND_PENDING` (ReceivedAmount)
   - Cập nhật `Payment.Status = REFUND_PENDING`.
   - Admin thực hiện hoàn tiền theo quy trình mục 5 (ghi sổ `REFUND_PENDING -> GATEWAY_CLEARING` khi chuyển khoản xong).

---

## 10. Đẳng Thức Đối Soát Toàn Thể Sổ Cái (Reconciliation Identity)

### 10.1. Quy ước dấu các tài khoản Sổ cái
| Tài khoản | Loại tài khoản | Số dư bình thường | Chiều ghi tăng | Chiều ghi giảm |
|---|---|---|---|---|
| `GATEWAY_CLEARING` | Tài sản (Tiền mặt tại ngân hàng/cổng) | DEBIT | DEBIT (+) | CREDIT (-) |
| `ESCROW_HELD` | Nợ phải trả (Tiền giữ hộ khách) | CREDIT | CREDIT (+) | DEBIT (-) |
| `SITTER_EARNINGS` | Nợ phải trả (Thu nhập giữ hộ Sitter) | CREDIT | CREDIT (+) | DEBIT (-) |
| `REFUND_PENDING` | Nợ phải trả (Chờ hoàn khách) | CREDIT | CREDIT (+) | DEBIT (-) |
| `SUSPENSE` | Nợ phải trả (Tạm giữ bất thường) | CREDIT | CREDIT (+) | DEBIT (-) |
| `PLATFORM_REVENUE` | Vốn chủ / Doanh thu | CREDIT | CREDIT (+) | DEBIT (-) |
| `GATEWAY_FEES` | Chi phí ngoài tiền mặt | DEBIT | DEBIT (+) | CREDIT (-) |

### 10.2. Đẳng thức Số dư Tiền mặt Ngân hàng Kỳ vọng (ExpectedBankBalance)
```
ExpectedBankBalance = Net(GATEWAY_CLEARING)
                     = Sum(DEBIT GATEWAY_CLEARING) - Sum(CREDIT GATEWAY_CLEARING)
```

### 10.3. Đẳng thức Cân bằng Kế toán Toàn bộ Hệ thống
```
Net(GATEWAY_CLEARING) == ESCROW_HELD
                       + Total(SITTER_EARNINGS)
                       + REFUND_PENDING
                       + SUSPENSE
                       + (PLATFORM_REVENUE - SweptTotal)
```
*(Ghi chú: `GATEWAY_FEES` giả định chưa bị ngân hàng trừ trực tiếp vào tiền mặt tài khoản thanh toán, được theo dõi riêng ở ngoài đẳng thức tiền mặt).*

---

## 11. Hướng Dẫn Sao Lưu & Khôi Phục Khóa Data Protection

Hệ thống sử dụng ASP.NET Core Data Protection với phương thức `PersistKeysToFileSystem` lưu trữ tại thư mục cố định ngoài mã nguồn (`DataProtection:KeyDirectory`).
Mặc định: `%APPDATA%\PetSitterKeys` (trên Windows) hoặc cấu hình trong `appsettings.json`.

### 11.1. Quy trình sao lưu (Backup)
1. Thư mục chứa khóa: kiểm tra giá trị trong `appsettings.json` tại `Payments:DataProtection:KeyDirectory`.
2. Sao lưu toàn bộ các tệp `.xml` trong thư mục này vào kho lưu trữ mã hóa an toàn offline.
3. Không commit thư mục này vào Git repository (đã có trong `.gitignore`).

### 11.2. Quy trình khôi phục (Restore / Disaster Recovery)
1. Khi triển khai lên máy chủ mới hoặc khôi phục sau sự cố, sao chép toàn bộ các tệp khóa `.xml` vào thư mục đã cấu hình.
2. Đảm bảo Application Pool / Service Worker có quyền đọc ghi vào thư mục này.
3. Khởi động lại ứng dụng. Hệ thống sẽ tự động nạp lại khóa và giải mã chính xác toàn bộ STK ngân hàng và thông tin người trả tiền.

---

## 12. Tình Trạng Xác Minh Tài Liệu Kênh Chi PayOS (PayOS Payout Documentation Verification)

| Hạng mục / Endpoint | Tình trạng xác minh | Nguồn tài liệu | Hành vi hệ thống |
|---|---|---|---|
| **Kênh thu (Pay-in)**: `/v2/payment-requests`, Webhook HMAC-SHA256 | ✅ **ĐÃ XÁC MINH** | https://payos.vn/docs/ | Đã triển khai đầy đủ và kiểm thử tự động. |
| **Kênh chi (Pay-out)**: Thông cáo ra mắt kênh chi (hợp tác Baokim, Shinhan, PVcomBank, OCB) | ✅ **ĐÃ XÁC MINH** | https://payos.vn/ra-mat-kenh-chi-tren-payos-giai-phap-chi-tu-dong-qua-api/ | Đã xác nhận PayOS có hỗ trợ kênh chi tự động. |
| **Kênh chi**: API `/v1/payouts`, `/balance` | ⚠️ **CHƯA XÁC MINH** | Không có trong tài liệu công khai https://payos.vn/docs/ (chỉ có trong cổng quản trị kín `my.payos.vn`) | Triển khai chế độ **MANUAL (Mặc định)**. Code API chi tự động được đặt trong trạng thái chờ (TODO) với `FakePaymentGateway` phục vụ kiểm thử. |
| **Kênh chi**: Header `x-idempotency-key`, `x-signature` kênh chi | ⚠️ **CHƯA XÁC MINH** | Cần kích hoạt Kênh chi và ký quỹ trên `my.payos.vn` | Giữ nguyên mock và runbook. |
| **Kênh chi**: Webhook trạng thái chi tiền | ⚠️ **CHƯA XÁC MINH** | Chưa có đặc tả payload webhook chi | Sử dụng cơ chế truy vấn trạng thái theo yêu cầu (Polling/On-demand check) thay vì webhook chi. |
