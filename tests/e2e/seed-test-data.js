/**
 * Script seed dữ liệu test cho E2E PawStay (chỉ trên DB test/development)
 * 1. REFUND_PENDING: Yêu cầu hoàn tiền đang chờ Admin xử lý
 * 2. DISPUTED: Đơn đặt lịch đang tranh chấp/khiếu nại
 * 3. REQUESTED: Lệnh rút tiền Sitter đang chờ duyệt
 * 4. SUSPENSE: Giao dịch ngân hàng bất thường (ABNORMAL)
 */
const http = require('http');

const BASE_URL = process.env.E2E_BASE_URL || 'http://localhost:5138';
const ADMIN_EMAIL = process.env.E2E_ADMIN_EMAIL || 'admin@pawstay.vn';
const ADMIN_PASSWORD = process.env.E2E_ADMIN_PASSWORD || 'Admin@123456';

async function request(url, options = {}, body = null) {
    return new Promise((resolve, reject) => {
        const u = new URL(url);
        const req = http.request(u, options, res => {
            let data = '';
            res.on('data', chunk => data += chunk);
            res.on('end', () => resolve({ status: res.statusCode, body: data }));
        });
        req.on('error', reject);
        if (body) req.write(typeof body === 'string' ? body : JSON.stringify(body));
        req.end();
    });
}

async function runSeed() {
    console.log(`\n======================================================`);
    console.log(`🐾 PAWSTAY E2E TEST DATA SEEDER`);
    console.log(`Target: ${BASE_URL}`);
    console.log(`======================================================\n`);

    // 1. Đăng nhập ADMIN lấy token
    console.log(`🔑 Đang đăng nhập ADMIN (${ADMIN_EMAIL})...`);
    const loginRes = await request(`${BASE_URL}/api/auth/login`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' }
    }, { email: ADMIN_EMAIL, matKhau: ADMIN_PASSWORD });

    let token = null;
    if (loginRes.status === 200) {
        try {
            const data = JSON.parse(loginRes.body);
            token = data.token;
            console.log(`✅ Đăng nhập Admin thành công.`);
        } catch {}
    }

    if (!token) {
        console.error(`❌ Đăng nhập thất bại (HTTP ${loginRes.status}): ${loginRes.body}`);
        console.log(`Vui lòng kiểm tra biến E2E_ADMIN_EMAIL và E2E_ADMIN_PASSWORD.`);
        process.exit(1);
    }

    // 2. Gọi endpoint seed
    console.log(`🌱 Đang khởi tạo 4 bản ghi test E2E qua POST /api/admin/seed-e2e-data...`);
    const seedRes = await request(`${BASE_URL}/api/admin/seed-e2e-data`, {
        method: 'POST',
        headers: {
            'Authorization': `Bearer ${token}`,
            'Content-Type': 'application/json'
        }
    });

    if (seedRes.status === 200) {
        console.log(`✅ KẾT QUẢ: ${seedRes.body}`);
        console.log(`\n4 bản ghi đã sẵn sàng:`);
        console.log(`  1. Hoàn tiền:  Mã BOOKING_E2E_01 (Trạng thái: REFUND_PENDING, Số tiền: 150.000₫)`);
        console.log(`  2. Khiếu nại:  Mã BOOKING_E2E_DISPUTE_01 (Trạng thái: DISPUTED, Số tiền: 450.000₫)`);
        console.log(`  3. Rút tiền:   Sitter SITTER_E2E_01 (Trạng thái: REQUESTED, Số tiền: 300.000₫)`);
        console.log(`  4. SUSPENSE:   OrderCode 99887766 (Trạng thái: ABNORMAL, Nhận: 300.000₫)`);
        console.log(`======================================================\n`);
    } else {
        console.error(`❌ Lỗi seed (HTTP ${seedRes.status}): ${seedRes.body}`);
        process.exit(1);
    }
}

runSeed().catch(err => {
    console.error('Lỗi khi chạy seed:', err);
    process.exit(1);
});

