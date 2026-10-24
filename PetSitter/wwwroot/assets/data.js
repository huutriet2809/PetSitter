// ================================================
// PAWSTAY - Hệ thống dữ liệu trung tâm (data.js)
// Phiên bản: 2.0
//
// Cấu trúc:
//   FAQ_DATA  — Câu hỏi thường gặp
//   Tiện ích  — formatTien, renderSao, getInitials, tinhSoNgay, showToast
//   Auth      — isLoggedIn, getCurrentUser, setLoggedIn, logout
// ================================================

// ------------------------------------------------
// 1. CÂU HỎI THƯỜNG GẶP (FAQ_DATA)
// ------------------------------------------------
const FAQ_DATA = [
  {
    id: 1,
    cauHoi: "Làm sao để tôi đặt lịch với một pet-sitter?",
    traLoi: "Bạn có thể vào trang Danh sách Sitter, chọn người phù hợp theo khu vực và mức giá, sau đó nhấn 'Đặt lịch ngay'. Điền đầy đủ ngày bắt đầu, ngày kết thúc và thông tin thú cưng để hoàn tất gửi yêu cầu."
  },
  {
    id: 2,
    cauHoi: "Chính sách hủy lịch và hoàn tiền như thế nào?",
    traLoi: "Chính sách hủy đơn căn cứ theo mốc 00:00 ngày bắt đầu dịch vụ: Hủy trước ≥ 48 giờ hoàn 100%; hủy từ 24 đến dưới 48 giờ hoàn 50%; hủy dưới 24 giờ hoàn 0%. Từ 00:00 ngày bắt đầu dịch vụ trở đi, chức năng hủy trực tuyến sẽ tự động khóa; trường hợp khẩn cấp vui lòng liên hệ bộ phận hỗ trợ PawStay."
  },
  {
    id: 3,
    cauHoi: "Quyền lợi tài chính và an toàn của tôi được đảm bảo như thế nào?",
    traLoi: "Toàn bộ tiền thanh toán được PawStay giữ an toàn qua cơ chế Escrow trong suốt thời gian dịch vụ. Khách hàng có thể gửi Khiếu nại (Dispute) sau khi dịch vụ kết thúc, trong thời hạn chờ xác nhận hoàn tất; Quản trị viên sẽ xem xét và phân xử theo chính sách hoàn tiền. Trong lúc dịch vụ đang diễn ra, vui lòng liên hệ hỗ trợ PawStay."
  },
  {
    id: 4,
    cauHoi: "Làm thế nào để trở thành Pet-sitter trên PawStay?",
    traLoi: "Bạn truy cập trang 'Trở thành Sitter', điền đầy đủ hồ sơ giới thiệu bản thân, kinh nghiệm và bảng giá dịch vụ. Quản trị viên PawStay sẽ xem xét và phê duyệt hồ sơ trong vòng 24 giờ làm việc."
  },
  {
    id: 5,
    cauHoi: "Tôi có thể theo dõi thú cưng khi đang gửi không?",
    traLoi: "Có! Sitter sẽ thường xuyên đăng ảnh và nhật ký lưu trú (Stay Updates) trên hệ thống. Bạn có thể xem hình ảnh và tình trạng của bé bất kỳ lúc nào từ trang Chi tiết đặt lịch."
  }
];

// ------------------------------------------------
// 2. UTILITY FUNCTIONS (Hàm tiện ích dùng chung)
// ------------------------------------------------

// Hàm escape HTML chống XSS
function escapeHtml(s) {
  if (s === null || s === undefined) return '';
  return String(s)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

// Hàm format tiền tệ Việt Nam
function formatTien(so) {
  if (typeof so !== 'number') so = Number(so) || 0;
  return so.toLocaleString('vi-VN') + 'đ';
}

// Hàm render sao rating (trả về HTML string)
function renderSao(rating, size) {
  const diem = parseFloat(rating) || 0;
  const fs = size || '0.85rem';
  let html = `<span class="stars" style="font-size:${/* safe */ fs}">`;
  for (let i = 1; i <= 5; i++) {
    if (i <= Math.floor(diem)) html += '★';
    else if (i - diem < 1 && i - diem > 0) html += '½';
    else html += '<span style="opacity:0.25">★</span>';
  }
  html += '</span>';
  return html;
}

// Hàm tạo initials từ họ tên (VD: "Nguyễn Thị Lan" → "LN")
function getInitials(ten) {
  if (!ten) return 'PS';
  const parts = ten.trim().split(/\s+/);
  const last = parts[parts.length - 1].charAt(0);
  const first = parts.length > 1 ? parts[0].charAt(0) : '';
  return (last + first).toUpperCase();
}

// Hàm tính ngày giữa 2 mốc (quy ước bao gồm cả 2 đầu ngày [Start, End])
function tinhSoNgay(batDau, ketThuc) {
  if (!batDau || !ketThuc) return 0;
  const dStart = new Date(batDau);
  const dEnd = new Date(ketThuc);
  const diffDays = Math.round((dEnd - dStart) / (1000 * 60 * 60 * 24));
  return diffDays >= 0 ? diffDays + 1 : 0;
}

// Hàm hiển thị toast notification
function showToast(message, duration) {
  let toast = document.getElementById('global-toast');
  if (!toast) {
    toast = document.createElement('div');
    toast.id = 'global-toast';
    toast.className = 'toast';
    document.body.appendChild(toast);
  }
  toast.textContent = message;
  toast.classList.add('show');
  setTimeout(() => toast.classList.remove('show'), duration || 3000);
}

// ------------------------------------------------
// 3. AUTH GIẢ LẬP / LOCAL STORAGE HELPERS
// ------------------------------------------------

function isLoggedIn() {
  return !!localStorage.getItem('pawstay_token');
}

function getCurrentUser() {
  const raw = localStorage.getItem('pawstay_user');
  return raw ? JSON.parse(raw) : null;
}

function setLoggedIn(user, token) {
  localStorage.setItem('pawstay_user', JSON.stringify(user));
  if (token) localStorage.setItem('pawstay_token', token);
}

function getBookingHistory() {
  const raw = localStorage.getItem('pawstay_booking_history');
  return raw ? JSON.parse(raw) : [];
}

function addToBookingHistory(booking) {
  const history = getBookingHistory();
  history.unshift(booking);
  localStorage.setItem('pawstay_booking_history', JSON.stringify(history));
}
