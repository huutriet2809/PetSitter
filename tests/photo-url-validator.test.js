const assert = require('assert');
const fs = require('fs');
const path = require('path');

// Đọc trực tiếp định nghĩa hàm từ wwwroot/js/stay-updates.js để kiểm thử đúng code thực tế
const stayUpdatesJsPath = path.join(__dirname, '..', 'PetSitter', 'wwwroot', 'js', 'stay-updates.js');
const fileContent = fs.readFileSync(stayUpdatesJsPath, 'utf8');

// Trích xuất mã hàm isValidPhotoUrl
const fnMatch = fileContent.match(/function\s+isValidPhotoUrl\s*\([^)]*\)\s*\{[\s\S]*?\n\}/);
if (!fnMatch) {
    throw new Error('Không tìm thấy hàm isValidPhotoUrl trong stay-updates.js!');
}

const isValidPhotoUrl = new Function(`${fnMatch[0]}; return isValidPhotoUrl;`)();

console.log('--- KIỂM THỬ BẢO MẬT: VALIDATE URL ẢNH (isValidPhotoUrl) ---');

// 1. Phải chặn các pseudo-protocol nguy hiểm
assert.strictEqual(isValidPhotoUrl("javascript:alert(1)"), false, "Phải chặn javascript:");
assert.strictEqual(isValidPhotoUrl("JAVASCRIPT:alert(document.cookie)"), false, "Phải chặn case-insensitive JAVASCRIPT:");
assert.strictEqual(isValidPhotoUrl("data:text/html,<script>alert(1)</script>"), false, "Phải chặn data:");
assert.strictEqual(isValidPhotoUrl("vbscript:msgbox(1)"), false, "Phải chặn vbscript:");
assert.strictEqual(isValidPhotoUrl("  javascript:void(0)"), false, "Phải chặn javascript: có khoảng trắng đầu");

// 2. Phải chặn protocol-relative URLs
assert.strictEqual(isValidPhotoUrl("//evil.com/fake.jpg"), false, "Phải chặn //evil.com");

// 3. Phải chặn dữ liệu không hợp lệ
assert.strictEqual(isValidPhotoUrl(""), false, "Phải chặn chuỗi rỗng");
assert.strictEqual(isValidPhotoUrl(null), false, "Phải chặn null");
assert.strictEqual(isValidPhotoUrl(undefined), false, "Phải chặn undefined");

// 4. Chỉ chấp nhận đường dẫn nội bộ /uploads/... (chặn các đường dẫn khác)
assert.strictEqual(isValidPhotoUrl("/uploads/photos/pet1.jpg"), true, "Phải chấp nhận đường dẫn /uploads/...");
assert.strictEqual(isValidPhotoUrl("/uploads/avatars/user.png"), true, "Phải chấp nhận /uploads/...");
assert.strictEqual(isValidPhotoUrl("/assets/images/placeholder.png"), false, "Phải chặn đường dẫn ngoài /uploads/...");
assert.strictEqual(isValidPhotoUrl("/etc/passwd"), false, "Phải chặn /etc/passwd");

// 5. Chỉ cho phép https:// (chặn http:// không mã hóa)
assert.strictEqual(isValidPhotoUrl("https://example.com/pet.jpg"), true, "Phải chấp nhận https://");
assert.strictEqual(isValidPhotoUrl("http://cdn.pawstay.vn/images/dog.webp"), false, "Phải chặn http:// không mã hóa");

console.log('>>> [PASS] TẤT CẢ TEST CASES VALIDATE URL ẢNH ĐỀU ĐẠT CHUẨN AN TOÀN! <<<');

