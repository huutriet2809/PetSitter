# Hướng dẫn Chạy Kiểm thử Playwright E2E CSP cho PawStay

Thư mục này chứa kịch bản kiểm thử E2E tự động sử dụng **Playwright Headless Chromium** để tải từng trang web, ghi nhận toàn bộ `console.error`, sự kiện `SecurityPolicyViolationEvent` và các request báo cáo vi phạm gửi về `/api/csp-report`.

> **Lưu ý:** Kịch bản này đã được chuẩn bị đầy đủ mã nguồn và cấu hình, nhưng **chưa được thực thi trong môi trường sandbox** (do sandbox cô lập mạng Internet ra npm registry). Bạn có thể chạy trực tiếp trên máy cá nhân theo các bước dưới đây.

---

## 1. Yêu cầu môi trường
- Đã cài đặt [Node.js](https://nodejs.org/) (phiên bản 18+).
- Đang khởi chạy ứng dụng backend PawStay tại máy (mặc định cổng `http://localhost:5000` hoặc cổng bạn cấu hình).

## 2. Các bước cài đặt & chạy kiểm thử

### Bước 1: Mở Terminal tại thư mục `tests/e2e`
```bash
cd tests/e2e
```

### Bước 2: Cài đặt thư viện Playwright
```bash
npm install
```

### Bước 3: Cài đặt binary trình duyệt Chromium
```bash
npx playwright install chromium
```

### Bước 4: Chạy kiểm thử
Nếu server đang chạy ở `http://localhost:5000`:
```bash
npm test
```

Nếu server chạy ở cổng khác (ví dụ `http://localhost:5173` hoặc `http://127.0.0.1:8080`), truyền biến môi trường `BASE_URL`:
- **Windows PowerShell:**
  ```powershell
  $env:BASE_URL="http://localhost:5173"; npm test
  ```
- **Linux / macOS:**
  ```bash
  BASE_URL=http://localhost:5173 npm test
  ```

---

## 3. Kết quả đầu ra
Kịch bản sẽ tự động mở từng trang trong danh sách, theo dõi và in ra bảng tổng kết:
- Số lỗi Console
- Số sự kiện `SecurityPolicyViolationEvent`
- Số request gửi về `/api/csp-report`
- Trạng thái PASS / FAIL cho từng trang.

