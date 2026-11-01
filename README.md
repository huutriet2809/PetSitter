# PetSitter Project

Hệ thống đặt lịch chăm sóc thú cưng PawStay (ASP.NET Core Web API + MongoDB + Static Frontend).

## Cấu hình ứng dụng (appsettings.json)

Ứng dụng cấu hình kết nối MongoDB thông qua `ConnectionStrings` trong `appsettings.json` hoặc biến môi trường:

```json
{
  "ConnectionStrings": {
    "MongoDb": "mongodb+srv://<username>:<password>@<cluster>.mongodb.net/?retryWrites=true&w=majority",
    "Database": "PetSitterDB",
    "DefaultConnection": ""
  },
  "JwtSettings": {
    "Secret": "YOUR_JWT_SECRET_AT_LEAST_32_CHARACTERS_LONG",
    "Issuer": "PetSitterApp",
    "Audience": "PetSitterUsers",
    "ExpirationMinutes": 1440
  }
}
```

*Lưu ý:* Khóa tên database được thống nhất dùng `ConnectionStrings:Database` (hoặc `ConnectionStrings__Database` khi đặt qua biến môi trường).

## Hướng dẫn kiểm tra chất lượng JavaScript & HTML Frontend

Để kiểm tra toàn diện mã JavaScript trong `PetSitter/wwwroot` (bao gồm các file `.js` trong `assets` và các đoạn thẻ `<script>` inline trong các file `.html`), chạy lệnh sau:

```bash
node tools/check-js.js
```

Script thực hiện các bước kiểm tra:
1. **Self-test**: Xác nhận khả năng phát hiện lỗi cú pháp và runtime trước khi kiểm tra toàn diện.
2. **Kiểm tra cú pháp (node --check)**:
   - Trích xuất toàn bộ script inline trong các file HTML sang file tạm và kiểm tra cú pháp AST bằng `node --check`.
   - Kiểm tra cú pháp toàn bộ file `.js` trong thư mục `assets/` bằng `node --check`.
3. **Kiểm tra biến không khai báo (ESLint rule `no-undef`)**:
   - Quét môi trường browser và các globals thực tế được import theo từng trang (`data.js`, `api.js`, `components.js`).
   - Đảm bảo mỗi trang import đủ các script cần thiết, tránh mọi lỗi `ReferenceError`.
4. **Quét các mẫu lỗi cú pháp đặc thù (Grep)**:
   - Dính chuỗi `` `r`n `` hoặc `rn</body>` sau thẻ đóng
   - Gán `innerHTML = <` không bọc nháy
   - Gọi `apiFetch(/` thiếu nháy / template literal
   - Gán `textContent = ` chuỗi trần không bọc nháy
   - Cặp backtick rỗng liên tiếp (`` `` `` ``)

Yêu cầu nghiệm thu đạt: **0 lỗi cú pháp**, **0 lỗi no-undef** và **0 kết quả grep lỗi**.
