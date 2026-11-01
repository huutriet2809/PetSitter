# Kế Hoạch Tồn Đọng Bảo Mật (Security Backlog)

Tài liệu này ghi nhận các hạng mục cải tiến bảo mật kỹ thuật cần thực hiện sau khi hoàn tất Bước 2 (Rate Limiting, Security Headers & JWT Refresh Token Rotation) của dự án PawStay.

---

## 1. Khóa tài khoản tạm thời (Account Lockout)

### Hiện trạng
- Đã có rate limiting ở tầng IP / Partition (`auth-strict`: 5 request/phút) tại endpoint `/api/auth/login`.
- Tuy nhiên, kẻ tấn công phân tán (distributed botnet) sử dụng nhiều IP khác nhau vẫn có thể nhắm vào một tài khoản cụ thể (targeted brute-force / credential stuffing).

### Kế hoạch triển khai
- **Cơ chế**: Theo dõi số lần đăng nhập thất bại theo tài khoản (`FailedLoginAttempts`, `LockoutEndUtc`).
- **Ngưỡng khóa**: Khóa tạm thời 15 phút sau 5 lần nhập sai mật khẩu liên tiếp.
- **Tự động mở khóa**: Hết 15 phút tài khoản tự động mở lại; đăng nhập thành công sẽ reset `FailedLoginAttempts` về 0.
- **Bảo vệ chống enumeration**: Trả về thông báo đồng nhất `Email hoặc mật khẩu không chính xác` hoặc `Tài khoản tạm thời bị khóa vì lý do an toàn. Vui lòng thử lại sau 15 phút`.

---

## 2. Bổ sung cờ `IsActive` cho Model `User`

### Hiện trạng
- Model `User` hiện chưa có cờ trạng thái hoạt động (`IsActive`). Khi tài khoản bị vô hiệu hóa bởi Admin, hệ thống chỉ có thể đổi Role hoặc cần xóa tài khoản.
- Quá trình refresh token mới chỉ kiểm tra sự tồn tại của User và cấp token mới theo Role hiện tại.

### Kế hoạch triển khai
- Thêm trường `public bool IsActive { get; set; } = true;` vào model `User`.
- Kiểm tra `IsActive == true` tại:
  1. `AuthController.Login`: Chặn đăng nhập nếu `!user.IsActive` (HTTP 403 Forbidden).
  2. `TokenService.RotateRefreshTokenAsync`: Nếu `!user.IsActive`, từ chối refresh token ngay lập tức, thu hồi toàn bộ token liên quan và trả về 401/403.
  3. `OnTokenValidated` (JwtBearer): Xác thực tức thời mỗi request API. Sử dụng `IMemoryCache` (TTL = 30s) để giảm tải truy vấn MongoDB. Khi Admin khóa tài khoản, cache key được xóa ngay lập tức (`InvalidateCache`) để chặn access token cũ tức thì.
  4. **Lưu ý kiến trúc đa máy chủ (Multi-instance Architecture)**: Bộ nhớ đệm `IMemoryCache` cho `IsActive` (30 giây) hiện tại là **per-instance** (cục bộ trong bộ nhớ tiến trình của từng máy chủ). Trong trường hợp scale ngang nhiều instance backend phía sau Load Balancer, nếu Admin khóa tài khoản ở một instance, các instance khác có thể trễ tối đa 30 giây để nhận biết nếu không có cơ chế invalidate phân tán. Khi mở rộng sang hạ tầng đa cụm/đa node, cần tích hợp Redis Cache hoặc Pub/Sub message broker (Redis Pub/Sub / RabbitMQ) để phát sự kiện xóa cache đồng bộ trên tất cả các instance.
  5. Khi Admin khóa tài khoản (`IsActive = false`), lập tức gọi `TokenService.RevokeAllUserTokensAsync(userId)` để hủy toàn bộ phiên làm việc trên các thiết bị.

---

## 3. Cấu hình `KnownNetworks` / CIDR cho `ForwardedHeaders`

### Hiện trạng
- `Program.cs` hiện hỗ trợ cấu hình danh sách IP proxy tin cậy đơn lẻ thông qua `ForwardedHeaders:KnownProxies` (dạng `IPAddress.TryParse`).
- Trong các hạ tầng reverse proxy động hoặc CDN (như Docker network, Kubernetes ingress, AWS ALB, Cloudflare), dải IP proxy thường được cấp theo subnet CIDR (ví dụ: `10.0.0.0/8`, `172.16.0.0/12`).

### Kế hoạch triển khai
- Bổ sung cấu hình `ForwardedHeaders:KnownNetworks` trong `appsettings.json` (dạng chuỗi CIDR: `Prefix/PrefixLength`, ví dụ: `10.0.0.0/8`, `172.16.0.0/12`).
- Trong `Program.cs`, phân tích chuỗi CIDR và thêm vào `options.KnownNetworks.Add(new IPNetwork(prefix, prefixLength))`.
- Cung cấp danh sách CIDR chuẩn của Cloudflare nếu ứng dụng đưa ra môi trường Production có CDN bảo vệ.

---

## 4. Lộ trình siết chặt CSP (Content Security Policy)

### Thống kê kiểm toán hiện trạng (`PetSitter/wwwroot`)
Đã thực hiện quét kiểm toán tự động trên toàn bộ 29 trang HTML tĩnh:
- **Tổng số file HTML**: 29 files
- **Thẻ `<script>` inline**: 23 thẻ
- **Thẻ `<style>` inline**: 27 thẻ
- **Thuộc tính `style="..."` inline**: 174 thuộc tính
- **Inline event listeners (`onclick`, `onchange`, `onsubmit`, v.v.)**: 40 vị trí
- **Tổng cộng điểm vi phạm CSP nghiêm ngặt**: **264 điểm**

### Hiện trạng chính sách
- Header hiện tại đang đặt ở chế độ giám sát: `Content-Security-Policy-Report-Only` với `'unsafe-inline'`.

### Lộ trình siết chặt (3 giai đoạn)
1. **Giai đoạn 1 - Tách JavaScript inline (Prompt hoàn thiện giao diện / Refactoring)**:
   - Chuyển 23 khối `<script>` inline trong các trang (như `login.html`, `booking.html`, `admin.html`) ra các file `.js` riêng biệt trong `wwwroot/assets/js/`.
   - Thay thế toàn bộ 40 inline event listeners (`onclick="doSomething()"`) bằng `addEventListener` chuẩn trong file JS.
2. **Giai đoạn 2 - Chuẩn hóa CSS**:
   - Chuyển 27 khối `<style>` inline và 174 thuộc tính `style="..."` vào file CSS dùng chung (`assets/style.css` hoặc CSS module theo trang).
3. **Giai đoạn 3 - Siết chặt Policy**:
   - Chuyển header từ `Content-Security-Policy-Report-Only` sang `Content-Security-Policy` thực thi chặn.
   - Loại bỏ hoàn toàn `'unsafe-inline'` khỏi `script-src` và `style-src`. Áp dụng `nonce` tự sinh trên mỗi request nếu bắt buộc phải dùng inline script động.

---

## 5. Đăng xuất khỏi mọi thiết bị (Sign out all devices)

### Hiện trạng
- Người dùng hiện có thể đăng xuất thiết bị hiện tại qua `POST /api/auth/logout` (thu hồi refresh token ở cookie hiện tại và xóa cookie).
- Khi đổi mật khẩu hoặc reset mật khẩu, hệ thống đã tự động thu hồi toàn bộ refresh token (`RevokeAllUserTokensAsync`).

### Kế hoạch triển khai
- Bổ sung endpoint chủ động: `POST /api/auth/logout-all` (yêu cầu Authorization Bearer Token).
- Endpoint sẽ gọi `_tokenService.RevokeAllUserTokensAsync(userId)` và xóa cookie refresh token trên trình duyệt hiện tại.
- Giúp người dùng xử lý tình huống nghi ngờ bị lộ thông tin hoặc bị mất thiết bị.

---

## 6. Test tự động cho Refresh Token Rotation & Race Condition (Prompt 6)

### Hiện trạng
- Kịch bản concurrency test (50 cặp request refresh song song đồng thời) đã được nghiệm thu qua công cụ scratch kiểm thử, đạt 100% (0 lần đăng xuất oan, đúng 1 refresh token mới).

### Kế hoạch triển khai (Prompt 6 - Testing Suite)
- Đưa kịch bản này vào bộ unit / integration test tự động chính thức (`PetSitter.Tests`):
  - **Test Case 1 - Rotation tuần tự**: Token hợp lệ đổi được access token + refresh token mới, token cũ bị gán `RevokedAt`.
  - **Test Case 2 - Phát hiện tái sử dụng (Reuse Detection)**: Gọi refresh bằng token đã bị revoke trước cửa sổ ân hạn -> kiểm tra toàn bộ token của User bị thu hồi và trả về 401.
  - **Test Case 3 - Cửa sổ ân hạn (Grace Window)**: Request đến trong 15s sau khi token bị thay thế bởi request hợp lệ khác -> trả về access token mới, không cấp thêm refresh token, không thu hồi oan.
  - **Test Case 4 - Concurrency Race**: `Task.WhenAll` trên 50 luồng đồng thời -> chỉ đúng 1 luồng thắng cấp token mới, các luồng còn lại nhận grace access token hoặc 401 theo đúng quy chuẩn.

---

## 7. Quy trình đổi Secret JWT định kỳ (Key Rotation)

### Hiện trạng
- `JwtSettings:Secret` được cấu hình qua biến môi trường hoặc `appsettings.json`.
- Khi thay đổi secret, toàn bộ access token cũ (thời hạn 15 phút) sẽ lập tức bị invalid (Signature verification failure).

### Kế hoạch triển khai
- **Hỗ trợ Multi-Key Verification**: Cấu hình danh sách `ValidIssuingKeys` chứa cả key hiện tại (dùng để ký) và key cũ (dùng để giải mã trong thời gian chuyển tiếp 15 phút).
- Khi rotate secret:
  1. Thêm secret mới làm primary key để cấp token.
  2. Giữ secret cũ trong danh sách key hợp lệ để xác thực trong 15-30 phút.
  3. Sau 30 phút, gỡ bỏ secret cũ hoàn toàn. Khách hàng sử dụng refresh token sẽ tự động nhận access token được ký bằng key mới mà không bị gián đoạn phiên làm việc.
