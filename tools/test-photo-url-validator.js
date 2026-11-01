const assert = require('assert');

// Trích xuất hàm isValidPhotoUrl để kiểm thử đơn vị
function isValidPhotoUrl(url) {
    if (!url || typeof url !== 'string') return false;
    const trimmed = url.trim();
    const lower = trimmed.toLowerCase();

    if (lower.startsWith('javascript:') || lower.startsWith('data:') || lower.startsWith('vbscript:')) {
        return false;
    }

    if (lower.startsWith('http://') || lower.startsWith('https://')) {
        return true;
    }
    if (trimmed.startsWith('/') && !trimmed.startsWith('//')) {
        return true;
    }
    return false;
}

console.log('--- KIỂM THỬ BẢO MẬT: VALIDATE URL ẢNH (isValidPhotoUrl) ---');

// 1. Phải chặn các pseudo-protocol nguy hiểm
assert.strictEqual(isValidPhotoUrl("javascript:alert(1)"), false, "Phải chặn javascript:");
assert.strictEqual(isValidPhotoUrl("JAVASCRIPT:alert(document.cookie)"), false, "Phải chặn case-insensitive JAVASCRIPT:");
assert.strictEqual(isValidPhotoUrl("data:text/html,<script>alert(1)</script>"), false, "Phải chặn data:");
assert.strictEqual(isValidPhotoUrl("vbscript:msgbox(1)"), false, "Phải chặn vbscript:");
assert.strictEqual(isValidPhotoUrl("  javascript:void(0)"), false, "Phải chặn javascript: có khoảng trắng đầu");

// 2. Phải chặn protocol-relative URLs dẫn ra ngoài domain độc hại
assert.strictEqual(isValidPhotoUrl("//evil.com/fake.jpg"), false, "Phải chặn //evil.com");

// 3. Phải chặn dữ liệu không hợp lệ
assert.strictEqual(isValidPhotoUrl(""), false, "Phải chặn chuỗi rỗng");
assert.strictEqual(isValidPhotoUrl(null), false, "Phải chặn null");
assert.strictEqual(isValidPhotoUrl(undefined), false, "Phải chặn undefined");

// 4. Phải chấp nhận đường dẫn nội bộ hợp lệ
assert.strictEqual(isValidPhotoUrl("/uploads/photos/pet1.jpg"), true, "Phải chấp nhận đường dẫn /uploads/...");
assert.strictEqual(isValidPhotoUrl("/assets/images/placeholder.png"), true, "Phải chấp nhận /assets/...");

// 5. Phải chấp nhận http/https hợp lệ
assert.strictEqual(isValidPhotoUrl("https://example.com/pet.jpg"), true, "Phải chấp nhận https://");
assert.strictEqual(isValidPhotoUrl("http://cdn.pawstay.vn/images/dog.webp"), true, "Phải chấp nhận http://");

console.log('>>> TẤT CẢ TEST CASES VALIDATE URL ẢNH ĐỀU ĐẠT CHUẨN AN TOÀN! <<<');

