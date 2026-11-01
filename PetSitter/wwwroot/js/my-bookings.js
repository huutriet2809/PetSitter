let loadedBookings = [];

document.addEventListener('DOMContentLoaded', () => {
  if (!localStorage.getItem('pawstay_token')) {
    window.location.href = 'login.html';
    return;
  }

  // Global event delegation cho tất cả hành động click
  document.addEventListener('click', (e) => {
    // Modal closes
    if (e.target.closest('[data-action="close-review-modal"]')) {
      closeReviewModal();
      return;
    }
    if (e.target.closest('[data-action="close-dispute-modal"]')) {
      closeDisputeModal();
      return;
    }
    if (e.target.closest('[data-action="close-refund-modal"]')) {
      closeRefundAccountModal();
      return;
    }
    if (e.target.closest('[data-action="close-cancel-modal"]')) {
      closeCancelModal();
      return;
    }

    // Booking actions
    const cancelBtn = e.target.closest('[data-action="open-cancel-modal"]');
    if (cancelBtn) {
      openCancelModal(cancelBtn.dataset.bookingId);
      return;
    }

    const disputeBtn = e.target.closest('[data-action="open-dispute-modal"]');
    if (disputeBtn) {
      openDisputeModal(disputeBtn.dataset.bookingId);
      return;
    }

    const payBtn = e.target.closest('[data-action="pay-booking"]');
    if (payBtn) {
      payBooking(payBtn.dataset.bookingId);
      return;
    }

    const refundBtn = e.target.closest('[data-action="open-refund-modal"]');
    if (refundBtn) {
      openRefundAccountModal(refundBtn.dataset.bookingId);
      return;
    }

    const completionBtn = e.target.closest('[data-action="confirm-completion"]');
    if (completionBtn) {
      confirmCompletion(completionBtn.dataset.bookingId);
      return;
    }

    const reviewBtn = e.target.closest('[data-action="open-review-modal"]');
    if (reviewBtn) {
      openReviewModal(reviewBtn.dataset.bookingId);
      return;
    }
  });

  // Gắn sự kiện submit cho các forms
  const disputeForm = document.getElementById('dispute-form');
  if (disputeForm) {
    disputeForm.addEventListener('submit', async function(e) {
      e.preventDefault();
      const bookingId = document.getElementById('dispute-booking-id').value;
      const reason = document.getElementById('dispute-reason').value.trim();

      if (reason.length < 10 || reason.length > 500) {
        showError('Lý do khiếu nại phải từ 10 đến 500 ký tự.');
        return;
      }

      try {
        await apiFetch(`/bookings/${bookingId}/dispute`, {
          method: 'PATCH',
          body: JSON.stringify({ lyDo: reason })
        });
        showToast('Đã gửi khiếu nại tới Admin xử lý!', 3000, '⚠️');
        closeDisputeModal();
        loadBookings();
      } catch (err) {
        showError(err.message || 'Không thể gửi khiếu nại.');
      }
    });
  }

  const reviewForm = document.getElementById('review-form');
  if (reviewForm) {
    reviewForm.addEventListener('submit', async function(e) {
      e.preventDefault();
      const bookingId = document.getElementById('review-booking-id').value;
      const rating = parseInt(document.getElementById('review-rating').value);
      const comment = document.getElementById('review-comment').value;

      try {
        await apiFetch(`/bookings/${bookingId}/review`, {
          method: 'POST',
          body: JSON.stringify({ rating, comment })
        });
        showToast('Đánh giá thành công!', 3000, '⭐');
        closeReviewModal();
        loadBookings();
      } catch (err) {
        showToast(err.message, 3000, '❌');
      }
    });
  }

  const cancelForm = document.getElementById('cancel-booking-form');
  if (cancelForm) {
    cancelForm.addEventListener('submit', async function(e) {
      e.preventDefault();
      const bookingId = document.getElementById('cancel-booking-id').value;
      const reason = document.getElementById('cancel-booking-reason').value.trim();

      try {
        await apiFetch(`/bookings/${bookingId}/cancel`, {
          method: 'POST',
          body: JSON.stringify({ lyDo: reason })
        });
        showToast('Đã hủy chuyến đi thành công!', 3000, '✅');
        closeCancelModal();
        loadBookings();
      } catch (err) {
        showError(err.message || 'Không thể hủy chuyến đi.');
      }
    });
  }

  const refundForm = document.getElementById('refund-account-form');
  if (refundForm) {
    refundForm.addEventListener('submit', async function(e) {
      e.preventDefault();
      const bookingId = document.getElementById('refund-booking-id').value;
      const bankName = document.getElementById('refund-bank').value.trim();
      const accountNumber = document.getElementById('refund-account-num').value.trim();
      const accountName = document.getElementById('refund-account-name').value.trim();

      try {
        await apiFetch(`/bookings/${bookingId}/refund-account`, {
          method: 'POST',
          body: JSON.stringify({ bankName, accountNumber, accountName })
        });
        showToast('Đã lưu thông tin tài khoản nhận hoàn tiền!', 3000, '✅');
        closeRefundAccountModal();
        loadBookings();
      } catch (err) {
        showError(err.message || 'Không thể cập nhật thông tin tài khoản hoàn tiền.');
      }
    });
  }

  loadBookings();
});

function showError(msg) {
  const el = document.getElementById('error-msg');
  el.textContent = msg;
  el.style.display = 'block';
  setTimeout(() => { el.style.display = 'none'; }, 5000);
}

const formatNgayDep = (str) => {
  if (!str) return '';
  const d = new Date(str);
  return `${d.getDate()}/${d.getMonth()+1}/${d.getFullYear()}`;
};

function getStatusLabel(status) {
  switch (status) {
    case 'PENDING': return 'Chờ xác nhận';
    case 'ACCEPTED': return 'Đã xác nhận';
    case 'COMPLETION_PENDING': return 'Chờ xác nhận hoàn tất';
    case 'COMPLETED': return 'Đã hoàn thành';
    case 'DISPUTED': return 'Đang khiếu nại';
    case 'CANCELLED': return 'Đã hủy';
    case 'REJECTED': return 'Từ chối';
    case 'EXPIRED': return 'Đã hết hạn';
    default: return status || '';
  }
}

function formatTimeRemaining(isoDate) {
  if (!isoDate) return '';
  const diffMs = new Date(isoDate).getTime() - Date.now();
  if (diffMs <= 0) return 'Đã hết hạn';
  const days = Math.floor(diffMs / (1000 * 60 * 60 * 24));
  const hours = Math.floor((diffMs % (1000 * 60 * 60 * 24)) / (1000 * 60 * 60));
  if (days > 0) return `còn ${days} ngày ${hours} giờ`;
  const minutes = Math.floor((diffMs % (1000 * 60 * 60)) / (1000 * 60));
  return `còn ${hours} giờ ${minutes} phút`;
}

async function loadBookings() {
  const container = document.getElementById('booking-list');
  try {
    const bookings = await apiFetch('/bookings/my');
    loadedBookings = bookings || [];
    if (loadedBookings.length === 0) {
      container.innerHTML = '<p>Bạn chưa có chuyến đi nào.</p>';
      return;
    }

    container.innerHTML = loadedBookings.map(b => {
      let statusClass = 'status-pending';
      if (b.trangThai === 'ACCEPTED') statusClass = 'status-accepted';
      if (b.trangThai === 'COMPLETION_PENDING') statusClass = 'status-completion-pending';
      if (b.trangThai === 'COMPLETED') statusClass = 'status-completed';
      if (b.trangThai === 'DISPUTED') statusClass = 'status-disputed';
      if (b.trangThai === 'CANCELLED' || b.trangThai === 'REJECTED') statusClass = 'status-rejected';
      if (b.trangThai === 'EXPIRED') statusClass = 'status-expired';

      let cancelBtn = '';
      const vnTodayStr = new Date(Date.now() + 7 * 3600000).toISOString().split('T')[0];
      const startVnStr = new Date(new Date(b.ngayBatDau).getTime() + 7 * 3600000).toISOString().split('T')[0];
      const endVnStr = new Date(new Date(b.ngayKetThuc).getTime() + 7 * 3600000).toISOString().split('T')[0];

      // Cho phép hủy nếu đơn đang PENDING hoặc ACCEPTED (trước ngày bắt đầu)
      if (b.trangThai === 'PENDING' || (b.trangThai === 'ACCEPTED' && startVnStr > vnTodayStr)) {
        cancelBtn = `<button class="btn btn-outline btn-sm btn-danger-outline" data-action="open-cancel-modal" data-booking-id="${/* safe */ b.bookingId}">Hủy chuyến</button>`;
      }

      let supportBtn = '';
      // Nếu đơn ACCEPTED đã qua ngày kết thúc mà Sitter chưa hoàn tất -> cho phép Owner yêu cầu hỗ trợ / khiếu nại
      if (b.trangThai === 'ACCEPTED' && endVnStr < vnTodayStr) {
        supportBtn = `<button class="btn btn-outline btn-sm btn-warning-outline" data-action="open-dispute-modal" data-booking-id="${/* safe */ b.bookingId}">⚠️ Yêu cầu hỗ trợ</button>`;
      }

      let payBtn = '';
      let paymentBadge = '';
      if (b.paymentStatus === 'PENDING_PAYMENT') {
        payBtn = `<button class="btn btn-primary btn-sm btn-success" data-action="pay-booking" data-booking-id="${/* safe */ b.bookingId}">💳 Thanh toán ngay (VietQR)</button>`;
        paymentBadge = `<span class="payment-badge badge-pending-pay">Chờ thanh toán</span>`;
      } else if (b.paymentStatus === 'HELD') {
        paymentBadge = `<span class="payment-badge badge-held">Đã thanh toán (Giữ an toàn)</span>`;
      } else if (b.paymentStatus === 'REFUND_PENDING') {
        paymentBadge = `<span class="payment-badge badge-refund-pending">Chờ hoàn tiền</span>`;
        payBtn = `<button class="btn btn-outline btn-sm" data-action="open-refund-modal" data-booking-id="${/* safe */ b.bookingId}">🏦 Nhập STK hoàn tiền</button>`;
      } else if (b.paymentStatus === 'REFUNDED') {
        paymentBadge = `<span class="payment-badge badge-refunded">Đã hoàn tiền</span>`;
      } else if (b.paymentStatus === 'PARTIALLY_REFUNDED') {
        paymentBadge = `<span class="payment-badge badge-partially-refunded">Hoàn một phần</span>`;
      } else if (b.paymentStatus === 'EXPIRED') {
        paymentBadge = `<span class="payment-badge badge-expired">Hết hạn thanh toán</span>`;
      } else if (b.paymentStatus === 'RELEASED') {
        paymentBadge = `<span class="payment-badge badge-released">Đã giải ngân</span>`;
      }

      let completionBtns = '';
      let noticeHtml = '';

      if (b.paymentStatus === 'PENDING_PAYMENT') {
        const payTimeRem = formatTimeRemaining(b.paymentDeadline);
        noticeHtml = `
          <div class="booking-notice booking-notice-green">
            💳 <strong>Sitter đã chấp nhận:</strong> Vui lòng hoàn tất thanh toán ${escapeHtml(payTimeRem)} để xác nhận giữ chỗ dịch vụ.
          </div>
        `;
      } else if (b.trangThai === 'COMPLETION_PENDING') {
        const timeRem = formatTimeRemaining(b.completionDeadline);
        completionBtns = `
          <button class="btn btn-primary btn-sm" data-action="confirm-completion" data-booking-id="${/* safe */ b.bookingId}">Xác nhận hoàn tất</button>
          <button class="btn btn-outline btn-sm btn-warning-outline" data-action="open-dispute-modal" data-booking-id="${/* safe */ b.bookingId}">Báo vấn đề</button>
        `;
        noticeHtml = `
          <div class="booking-notice booking-notice-purple">
            ⏳ <strong>Sitter đã hoàn thành dịch vụ:</strong> Bạn ${escapeHtml(timeRem)} để xác nhận hoặc báo vấn đề trước khi hệ thống tự động hoàn tất.
          </div>
        `;
      } else if (b.trangThai === 'ACCEPTED' && endVnStr < vnTodayStr) {
        noticeHtml = `
          <div class="booking-notice booking-notice-amber">
            ⏰ <strong>Đã qua ngày kết thúc:</strong> Chuyến đi đã kết thúc ngày ${formatNgayDep(b.ngayKetThuc)} nhưng Sitter chưa bấm Hoàn tất. Nếu chưa nhận lại thú cưng hoặc cần hỗ trợ khẩn cấp, vui lòng bấm "Yêu cầu hỗ trợ".
          </div>
        `;
      } else if (b.trangThai === 'ACCEPTED' && b.paymentStatus === 'HELD' && startVnStr <= vnTodayStr) {
        noticeHtml = `
          <div class="booking-notice booking-notice-blue">
            🏡 <strong>Chuyến đi đang diễn ra:</strong> Sitter đang chăm sóc bé cưng của bạn. Cần hỗ trợ? <a href="contact.html" class="booking-notice-link">Liên hệ PawStay</a>
          </div>
        `;
      } else if (b.trangThai === 'DISPUTED') {
        noticeHtml = `
          <div class="booking-notice booking-notice-orange">
            ⚠️ <strong>Đang chờ Admin xử lý khiếu nại:</strong> ${escapeHtml(b.disputeReason || 'Đã gửi yêu cầu can thiệp.')}
          </div>
        `;
      }

      let actionBtns = `
        <div class="booking-actions">
          ${/* safe */ payBtn}
          ${/* safe */ cancelBtn}
          ${/* safe */ supportBtn}
          ${/* safe */ completionBtns}
          <a href="stay-updates.html?bookingId=${/* safe */ b.bookingId}" class="btn btn-outline btn-sm">Nhật ký chăm sóc</a>
          ${/* safe */ b.trangThai === 'COMPLETED' ? (b.hasReview ? '<span class="reviewed-badge">Đã đánh giá</span>' : `<button class="btn btn-primary btn-sm" data-action="open-review-modal" data-booking-id="${/* safe */ b.bookingId}">Viết đánh giá</button>`) : ''}
          <a href="sitter-profile.html?id=${/* safe */ b.sitterId}" class="btn btn-ghost btn-sm">Xem Sitter</a>
        </div>
      `;

      return `
        <div class="booking-card" id="card-${/* safe */ b.bookingId}">
          <div class="booking-header">
            <div>
              <strong>Mã: ${escapeHtml(b.maDatLich)}</strong> - Sitter: <a href="sitter-profile.html?id=${/* safe */ b.sitterId}">${escapeHtml(b.sitterName)}</a>
            </div>
            <div class="header-status-wrap">
              <span class="status-badge ${/* safe */ statusClass}">${escapeHtml(getStatusLabel(b.trangThai))}</span>
              ${/* safe */ paymentBadge}
            </div>
          </div>
          <div class="booking-grid-info">
            <div>
              <p><strong>Ngày:</strong> ${formatNgayDep(b.ngayBatDau)} - ${formatNgayDep(b.ngayKetThuc)}</p>
              <p><strong>Thú cưng:</strong> ${escapeHtml(b.pet?.tenPet || '')}</p>
              ${/* safe */ (b.trangThai === 'PENDING' && b.expiresAt) ? `<p class="expiry-hint"><strong>Hạn phản hồi:</strong> ${formatNgayDep(b.expiresAt)}</p>` : ''}
            </div>
            <div>
              <p><strong>Tổng tiền:</strong> ${formatTien(b.tongTien)}</p>
              ${/* safe */ b.lyDoTuChoi ? `<p class="reject-reason"><strong>Lý do:</strong> ${escapeHtml(b.lyDoTuChoi)}</p>` : ''}
            </div>
            ${/* safe */ noticeHtml}
          </div>
          ${/* safe */ actionBtns}
        </div>
      `;
    }).join('');

    const urlParams = new URLSearchParams(window.location.search);
    const targetBookingId = urlParams.get('bookingId');
    if (targetBookingId) {
      const targetCard = document.getElementById(`card-${targetBookingId}`);
      if (targetCard) {
        targetCard.scrollIntoView({ behavior: 'smooth', block: 'center' });
        targetCard.classList.add('booking-highlight');
      }
    }
  } catch (err) {
    showError(err.message || 'Lỗi tải danh sách.');
    container.innerHTML = '';
  }
}

async function confirmCompletion(id) {
  if (!confirm('Bạn xác nhận chuyến đi đã hoàn thành tốt đẹp?')) return;
  try {
    await apiFetch(`/bookings/${id}/confirm-completion`, { method: 'PATCH' });
    showToast('Đã xác nhận hoàn tất dịch vụ!', 3000, '✅');
    loadBookings();
  } catch (e) {
    showError(e.message || 'Không thể xác nhận hoàn tất.');
  }
}

function openDisputeModal(bookingId) {
  document.getElementById('dispute-booking-id').value = bookingId;
  document.getElementById('dispute-reason').value = '';
  document.getElementById('dispute-modal').classList.add('open');
}

function closeDisputeModal() {
  document.getElementById('dispute-modal').classList.remove('open');
}

function openReviewModal(bookingId) {
  document.getElementById('review-booking-id').value = bookingId;
  document.getElementById('review-rating').value = '5';
  document.getElementById('review-comment').value = '';
  document.getElementById('review-modal').classList.add('open');
}

function closeReviewModal() {
  document.getElementById('review-modal').classList.remove('open');
}

function openCancelModal(bookingId) {
  const b = loadedBookings.find(item => item.bookingId === bookingId);
  if (!b) return;

  document.getElementById('cancel-booking-id').value = bookingId;
  document.getElementById('cancel-booking-code').textContent = `#${b.maDatLich || b.bookingId}`;
  document.getElementById('cancel-start-date').textContent = formatNgayDep(b.ngayBatDau);
  document.getElementById('cancel-total-amount').textContent = formatTien(b.tongTien);
  document.getElementById('cancel-booking-reason').value = '';

  const policyBox = document.getElementById('cancel-policy-box');
  const submitBtn = document.getElementById('btn-submit-cancel');
  submitBtn.disabled = false;

  // Tính mốc 00:00:00 ngày bắt đầu theo giờ Việt Nam (UTC+7)
  const startDate = new Date(b.ngayBatDau);
  const startYear = startDate.getFullYear();
  const startMonth = startDate.getMonth();
  const startDay = startDate.getDate();
  // 00:00:00 GMT+7 tương đương Date.UTC(startYear, startMonth, startDay, 0, 0, 0) - 7 giờ
  const startMidnightVnMs = Date.UTC(startYear, startMonth, startDay, 0, 0, 0) - (7 * 3600000);
  const nowUtcMs = Date.now();
  const remainingMs = startMidnightVnMs - nowUtcMs;
  const remainingHours = remainingMs / 3600000;

  const vnTodayStr = new Date(Date.now() + 7 * 3600000).toISOString().split('T')[0];
  const startVnStr = new Date(new Date(b.ngayBatDau).getTime() + 7 * 3600000).toISOString().split('T')[0];

  if (startVnStr <= vnTodayStr) {
    submitBtn.disabled = true;
    policyBox.className = 'policy-box policy-box-disabled';
    policyBox.innerHTML = `
      <div class="policy-title-red">⛔ Không thể hủy đơn trực tuyến</div>
      <p class="policy-note">
        Đã đến hoặc qua ngày bắt đầu dịch vụ (từ 00:00 ngày ${formatNgayDep(b.ngayBatDau)}). Cần hỗ trợ? <a href="contact.html" class="policy-link-red">Liên hệ PawStay ngay</a>
      </p>
    `;
  } else if (b.paymentStatus !== 'HELD' || b.trangThai === 'PENDING') {
    // Đơn chưa thanh toán hoặc chưa cọc
    policyBox.className = 'policy-box policy-box-unpaid';
    policyBox.innerHTML = `
      <div class="policy-title-blue">ℹ️ Đơn chưa thanh toán tiền cọc</div>
      <div class="policy-row">
        <span>Số tiền hoàn:</span>
        <strong class="text-green">0 ₫ (Không mất chi phí)</strong>
      </div>
      <div class="policy-row">
        <span>Số tiền mất:</span>
        <strong class="text-muted">0 ₫</strong>
      </div>
      <p class="policy-note">
        Bạn có thể hủy đơn miễn phí vì chưa phát sinh giao dịch thanh toán tạm giữ.
      </p>
    `;
  } else {
    // Đơn đã thanh toán (paymentStatus === 'HELD')
    const total = b.tongTien || 0;
    if (remainingHours >= 48) {
      policyBox.className = 'policy-box policy-box-full';
      policyBox.innerHTML = `
        <div class="policy-title-green">✅ Hủy trước ≥ 48 giờ: Hoàn tiền 100%</div>
        <div class="policy-row">
          <span>Số tiền hoàn lại cho bạn:</span>
          <strong class="text-green font-lg">100% (${formatTien(total)})</strong>
        </div>
        <div class="policy-row">
          <span>Số tiền mất:</span>
          <strong class="text-muted">0 ₫</strong>
        </div>
        <p class="policy-note">
          Thời gian còn lại đến ngày bắt đầu: <strong>${Math.floor(remainingHours)} giờ</strong>. Hệ thống sẽ tạo yêu cầu hoàn tiền 100% về tài khoản của bạn.
        </p>
      `;
    } else if (remainingHours >= 24) {
      const refund50 = Math.floor(total / 2);
      const loss50 = total - refund50;
      policyBox.className = 'policy-box policy-box-half';
      policyBox.innerHTML = `
        <div class="policy-title-orange">⚠️ Hủy trong khoảng 24h - 48h: Hoàn tiền 50%</div>
        <div class="policy-row">
          <span>Số tiền hoàn lại cho bạn:</span>
          <strong class="text-orange font-lg">50% (${formatTien(refund50)})</strong>
        </div>
        <div class="policy-row">
          <span>Số tiền mất (hỗ trợ Sitter):</span>
          <strong class="text-red font-lg">50% (${formatTien(loss50)})</strong>
        </div>
        <p class="policy-note">
          Thời gian còn lại: <strong>${Math.floor(remainingHours)} giờ</strong>. 50% số tiền còn lại được chuyển bồi thường cho người chăm sóc và phí dịch vụ nền tảng.
        </p>
      `;
    } else {
      policyBox.className = 'policy-box policy-box-zero';
      policyBox.innerHTML = `
        <div class="policy-title-red">🚨 Hủy gấp dưới 24 giờ: Không hoàn tiền (0%)</div>
        <div class="policy-row">
          <span>Số tiền hoàn lại cho bạn:</span>
          <strong class="text-danger font-lg">0 ₫ (0%)</strong>
        </div>
        <div class="policy-row">
          <span>Số tiền mất:</span>
          <strong class="text-red font-lg">100% (${formatTien(total)})</strong>
        </div>
        <p class="policy-note-red">
          Thời gian còn lại: <strong>${Math.floor(remainingHours)} giờ</strong>. Do hủy sát ngày, toàn bộ tiền sẽ được chuyển cho người chăm sóc để bù đắp giữ lịch hẹn và phí sàn.
        </p>
      `;
    }
  }

  document.getElementById('cancel-booking-modal').classList.add('open');
}

function closeCancelModal() {
  document.getElementById('cancel-booking-modal').classList.remove('open');
}

async function payBooking(bookingId) {
  try {
    const res = await apiFetch(`/bookings/${bookingId}/pay`, { method: 'POST' });
    if (res && res.checkoutUrl) {
      window.location.href = res.checkoutUrl;
    } else {
      showToast('Đang kết nối cổng thanh toán...', 3000, '💳');
      loadBookings();
    }
  } catch (err) {
    showError(err.message || 'Không thể tạo link thanh toán.');
  }
}

function openRefundAccountModal(bookingId) {
  document.getElementById('refund-booking-id').value = bookingId;
  document.getElementById('refund-bank').value = '';
  document.getElementById('refund-account-num').value = '';
  document.getElementById('refund-account-name').value = '';
  document.getElementById('refund-modal').classList.add('open');
}

function closeRefundAccountModal() {
  document.getElementById('refund-modal').classList.remove('open');
}

