document.addEventListener('DOMContentLoaded', async function() {
  const urlParams = new URLSearchParams(window.location.search);
  const bookingId = urlParams.get('id') || urlParams.get('bookingId');
  const container = document.getElementById('confirmation-content');

  // Event delegation cho các nút hành động trong container
  container.addEventListener('click', function(e) {
    const payBtn = e.target.closest('[data-action="pay-now"]');
    if (payBtn) {
      const bId = payBtn.dataset.bookingId;
      if (bId) payNow(bId);
      return;
    }
    const printBtn = e.target.closest('[data-action="print"]');
    if (printBtn) {
      window.print();
      return;
    }
  });

  if (!bookingId) {
    renderNoBookingData(container, "Không tìm thấy mã đặt lịch.");
    return;
  }

  try {
    const booking = await apiFetch(`/bookings/${bookingId}`);
    renderConfirmation(container, booking);
  } catch (error) {
    if (error.status === 404) {
      renderNoBookingData(container, "Booking không tồn tại hoặc đã bị xóa.");
    } else if (error.status === 403 || (error.message && error.message.includes('quyền'))) {
      renderNoBookingData(container, "Bạn không có quyền xem thông tin đặt lịch này.");
    } else {
      renderNoBookingData(container, error.message || "Đã có lỗi xảy ra.");
    }
  }
});

// Render state when directly accessing without booking
function renderNoBookingData(container, message) {
  container.innerHTML = `
    <div class="error-card">
      <p class="error-card-icon">🔍</p>
      <h2 class="error-card-title">Lỗi truy xuất</h2>
      <p class="error-card-desc">${escapeHtml(message)}</p>
      <a href="sitters.html" class="btn btn-primary">
        Đặt lịch ngay
      </a>
    </div>
  `;
}

// Render detailed confirmation details
function renderConfirmation(container, booking) {
  const initials = getInitials(booking.sitterName || "?");
  const avatarClass = booking.sitterAvatarUrl ? 'sitter-avatar-transparent' : 'sitter-avatar-default';

  // Format dates
  const formatNgayDep = (str) => {
    const d = new Date(str);
    const thu = ['Chủ Nhật', 'Thứ 2', 'Thứ 3', 'Thứ 4', 'Thứ 5', 'Thứ 6', 'Thứ 7'][d.getDay()];
    return `${thu}, ngày ${d.getDate()}/${d.getMonth()+1}/${d.getFullYear()}`;
  };

  // Addons details
  let addonsHtml = '';
  if (booking.addons && booking.addons.length > 0) {
    addonsHtml = booking.addons.map(add => {
      const detailSuffix = add.isDaily ? ` (${formatTien(add.donGia)} × ${/* safe */ booking.soNgay} ngày)` : '';
      return `
        <div class="summary-row">
          <span class="summary-row-label">
            <span>✨</span>
            <div>
              <span>${escapeHtml(add.ten)}</span>
              <small class="addon-detail-sub">
                Dịch vụ bổ sung${/* safe */ detailSuffix}
              </small>
            </div>
          </span>
          <span class="summary-row-value">+${formatTien(add.thanhTien)}</span>
        </div>
      `;
    }).join('');
  }

  const avatarHtml = booking.sitterAvatarUrl 
    ? `<img src="${/* safe */ booking.sitterAvatarUrl}" alt="Avatar" class="avatar-img">` 
    : escapeHtml(initials);
    
  const noteHtml = booking.ghiChu ? `
    <div class="summary-row summary-row-align-top">
      <span class="summary-row-label">
        <span>📝</span> Ghi chú
      </span>
      <span class="summary-row-value summary-row-note">
        "${escapeHtml(booking.ghiChu)}"
      </span>
    </div>
  ` : '';

  container.innerHTML = `
    <!-- Big Success Circle -->
    <div class="success-icon-wrap">
      <div class="success-circle">✓</div>
    </div>

    <!-- Heading text -->
    <div class="success-heading">
      <h1>Đặt lịch thành công! 🎉</h1>
      <p>
        Yêu cầu chăm sóc đã được gửi tới sitter. Sitter sẽ phản hồi và liên hệ xác nhận trong vòng <strong>48 giờ</strong>.
      </p>
    </div>

    <!-- Booking Code Display -->
    <div class="booking-code-wrap">
      <p class="booking-code-label">Mã đặt lịch của bạn</p>
      <div class="booking-code-value" id="booking-code">${escapeHtml(booking.maDatLich)}</div>
      <p class="booking-code-hint">
        Vui lòng lưu lại mã này để tra cứu hoặc liên hệ hỗ trợ.
      </p>
    </div>

    ${booking.paymentStatus === 'PENDING_PAYMENT' ? `
    <!-- Banner thanh toán ngay nếu đơn đã được duyệt và đang chờ thanh toán -->
    <div class="payment-banner">
      <h3 class="payment-banner-title">💳 Sitter đã chấp nhận đơn - Chờ thanh toán giữ chỗ</h3>
      <p class="payment-banner-desc">
        Vui lòng hoàn tất thanh toán qua VietQR (PayOS) để kích hoạt giữ chỗ đảm bảo.
      </p>
      <div class="payment-banner-actions">
        <button class="btn btn-primary btn-lg btn-pay-now" data-action="pay-now" data-booking-id="${/* safe */ booking.bookingId}">
          💳 Thanh toán ngay (VietQR)
        </button>
        <a href="my-bookings.html?bookingId=${/* safe */ booking.bookingId}" class="btn btn-outline btn-lg">
          Xem chi tiết tại Lịch của tôi
        </a>
      </div>
    </div>
    ` : ''}

    <!-- SUMMARY CARD -->
    <div class="summary-card">
      <div class="summary-header">
        <h2 class="summary-title">Tóm tắt chuyến đi</h2>
        <div class="summary-status">
          <span class="status-dot"></span> ${escapeHtml(booking.trangThai || 'Chờ xác nhận')}
        </div>
      </div>

      <!-- Sitter Info Box -->
      <div class="sitter-info-box">
        <div class="sitter-avatar-small ${avatarClass}">
          ${/* safe */ avatarHtml}
        </div>
        <div>
          <p class="sitter-label">
            Pet-sitter
          </p>
          <p class="sitter-name-val">
            ${escapeHtml(booking.sitterName)}
          </p>
        </div>
        <a href="sitter-profile.html?id=${/* safe */ booking.sitterId}" class="btn btn-outline btn-sm sitter-view-btn">
          Xem hồ sơ
        </a>
      </div>

      <!-- Divider -->
      <div class="summary-divider"></div>

      <!-- Dates & Pets -->
      <div class="summary-row">
        <span class="summary-row-label">
          <span>📅</span> Thời gian
        </span>
        <span class="summary-row-value text-right">
          ${formatNgayDep(booking.ngayBatDau)}<br/>
          <span class="date-to">đến</span><br/>
          ${formatNgayDep(booking.ngayKetThuc)}<br/>
          <span class="date-days">(${/* safe */ booking.soNgay} ngày)</span>
        </span>
      </div>

      <div class="summary-row">
        <span class="summary-row-label">
          <span>🐾</span> Thú cưng
        </span>
        <span class="summary-row-value">
          ${escapeHtml(booking.pet?.tenPet || '')} (${escapeHtml(booking.pet?.loaiPet || '')}, ${escapeHtml(booking.pet?.kichCo || '')})
        </span>
      </div>
      
      ${/* safe */ addonsHtml}
      ${/* safe */ noteHtml}

      <div class="summary-divider"></div>

      <!-- Prices -->
      <div class="summary-row">
        <span class="summary-row-label">Phí chăm sóc (${/* safe */ booking.soNgay} ngày)</span>
        <span class="summary-row-value">${formatTien(booking.giaSitter)}</span>
      </div>

      ${/* safe */ (booking.tongAddons && booking.tongAddons > 0) ? `
      <div class="summary-row">
        <span class="summary-row-label">Dịch vụ bổ sung</span>
        <span class="summary-row-value">${formatTien(booking.tongAddons)}</span>
      </div>
      ` : ''}

      <div class="summary-divider"></div>

      <!-- Total -->
      <div class="summary-total">
        <span>Chi phí ước tính</span>
        <span>${formatTien(booking.tongTien)}</span>
      </div>
    </div>

    <!-- Quy trình Escrow an toàn -->
    <div class="escrow-box">
      <h3 class="escrow-title">
        🛡️ Quy trình thanh toán ký quỹ an toàn (Escrow)
      </h3>
      <ul class="escrow-list">
        <li class="escrow-item">
          <span class="escrow-bullet">•</span>
          <span><strong>Bước 1:</strong> Sitter xác nhận đơn (trong vòng 48 giờ).</span>
        </li>
        <li class="escrow-item">
          <span class="escrow-bullet">•</span>
          <span><strong>Bước 2:</strong> Bạn thanh toán 100% qua VietQR (PayOS), tiền được PawStay giữ an toàn.</span>
        </li>
        <li class="escrow-item">
          <span class="escrow-bullet">•</span>
          <span><strong>Bước 3:</strong> Tiền chỉ giải ngân cho Sitter sau khi chuyến gửi hoàn tất. <em>Không thanh toán tiền mặt cho Sitter.</em></span>
        </li>
      </ul>
    </div>

    <!-- Actions -->
    <div class="action-buttons">
      <a href="my-bookings.html" class="btn btn-outline btn-lg">
        Xem lịch của tôi
      </a>
      <a href="index.html" class="btn btn-primary btn-lg">
        Về trang chủ
      </a>
      <button class="btn btn-outline btn-lg" data-action="print">
        🖨️ In biên nhận
      </button>
    </div>
  `;
}

async function payNow(bookingId) {
  try {
    const res = await apiFetch(`/bookings/${bookingId}/pay`, { method: 'POST' });
    if (res && res.checkoutUrl) {
      window.location.href = res.checkoutUrl;
    } else {
      window.location.href = `my-bookings.html?bookingId=${encodeURIComponent(bookingId)}`;
    }
  } catch (err) {
    alert(err.message || 'Không thể tạo liên kết thanh toán.');
  }
}

