function showError(msg) {
  const el = document.getElementById('error-msg');
  if (el) {
    el.textContent = msg;
    el.style.display = 'block';
    setTimeout(() => { el.style.display = 'none'; }, 5000);
  }
}

function formatTien(amount) {
  return (amount || 0).toLocaleString('vi-VN') + ' đ';
}

function formatNgayDep(isoStr) {
  if (!isoStr) return '';
  const d = new Date(isoStr);
  return d.toLocaleDateString('vi-VN');
}

function getStatusLabel(status) {
  switch (status) {
    case 'PENDING': return 'Chờ xác nhận';
    case 'ACCEPTED': return 'Đã xác nhận';
    case 'COMPLETION_PENDING': return 'Chờ chủ xác nhận';
    case 'COMPLETED': return 'Đã hoàn thành';
    case 'DISPUTED': return 'Đang tranh chấp';
    case 'CANCELLED': return 'Đã hủy';
    case 'REJECTED': return 'Từ chối';
    case 'EXPIRED': return 'Đã hết hạn';
    default: return status || '';
  }
}

function formatTimeRemaining(isoDate) {
  if (!isoDate) return '';
  const diffMs = new Date(isoDate).getTime() - Date.now();
  if (diffMs <= 0) return 'Đã hết hạn xác nhận';
  const days = Math.floor(diffMs / (1000 * 60 * 60 * 24));
  const hours = Math.floor((diffMs % (1000 * 60 * 60 * 24)) / (1000 * 60 * 60));
  if (days > 0) return `còn ${days} ngày ${hours} giờ`;
  const minutes = Math.floor((diffMs % (1000 * 60 * 60)) / (1000 * 60));
  return `còn ${hours} giờ ${minutes} phút`;
}

window.addEventListener('pageshow', () => {
  if (!localStorage.getItem('pawstay_token')) {
    window.location.replace('login.html');
  }
});

document.addEventListener('DOMContentLoaded', () => {
  if (!localStorage.getItem('pawstay_token')) {
    window.location.href = 'login.html';
    return;
  }
  const user = typeof getCurrentUser === 'function' ? getCurrentUser() : JSON.parse(localStorage.getItem('pawstay_user') || 'null');
  const role = (user && user.role ? user.role : '').toUpperCase();
  if (role !== 'SITTER') {
    window.location.href = role === 'ADMIN' ? 'admin.html' : 'owner-dashboard.html';
    return;
  }

  // Delegation cho tabs
  const tabsContainer = document.querySelector('.tabs');
  if (tabsContainer) {
    tabsContainer.addEventListener('click', (e) => {
      const tab = e.target.closest('.tab');
      if (!tab) return;
      const tabType = tab.dataset.tab;
      if (tabType === 'earnings') {
        loadEarnings(tab);
      } else {
        loadBookings(tabType, tab);
      }
    });
  }

  // Delegation cho tất cả hành động bên trong #booking-list
  const bookingListContainer = document.getElementById('booking-list');
  if (bookingListContainer) {
    bookingListContainer.addEventListener('click', (e) => {
      // 1. Chấp nhận booking
      const acceptBtn = e.target.closest('[data-action="accept-booking"]');
      if (acceptBtn) {
        acceptBooking(acceptBtn.dataset.bookingId);
        return;
      }

      // 2. Mở form từ chối
      const showRejectBtn = e.target.closest('[data-action="show-reject"]');
      if (showRejectBtn) {
        const wrap = document.getElementById(`reject-${showRejectBtn.dataset.bookingId}`);
        if (wrap) wrap.classList.remove('hidden');
        return;
      }

      // 3. Xác nhận từ chối
      const confirmRejectBtn = e.target.closest('[data-action="confirm-reject"]');
      if (confirmRejectBtn) {
        rejectBooking(confirmRejectBtn.dataset.bookingId);
        return;
      }

      // 4. Mở form hủy đơn của sitter
      const showCancelBtn = e.target.closest('[data-action="show-sitter-cancel"]');
      if (showCancelBtn) {
        const wrap = document.getElementById(`cancel-${showCancelBtn.dataset.bookingId}`);
        if (wrap) wrap.classList.remove('hidden');
        return;
      }

      // 5. Xác nhận hủy đơn của sitter
      const confirmCancelBtn = e.target.closest('[data-action="confirm-sitter-cancel"]');
      if (confirmCancelBtn) {
        sitterCancelBooking(confirmCancelBtn.dataset.bookingId);
        return;
      }

      // 6. Đánh dấu hoàn tất
      const completeBtn = e.target.closest('[data-action="complete-booking"]');
      if (completeBtn) {
        completeBooking(completeBtn.dataset.bookingId);
        return;
      }

      // 7. Hủy yêu cầu rút tiền
      const cancelWthBtn = e.target.closest('[data-action="cancel-withdrawal"]');
      if (cancelWthBtn) {
        cancelWithdrawal(cancelWthBtn.dataset.withdrawalId);
        return;
      }

      // 8. Ẩn/hiện form đổi tài khoản ngân hàng
      const toggleBankBtn = e.target.closest('[data-action="toggle-bank-edit"]');
      if (toggleBankBtn) {
        toggleBankEdit(toggleBankBtn.dataset.show === 'true');
        return;
      }

      // 9. Lưu tài khoản ngân hàng
      const saveBankBtn = e.target.closest('[data-action="save-bank-account"]');
      if (saveBankBtn) {
        saveBankAccount();
        return;
      }

      // 10. Chọn nhanh số tiền rút
      const setAmtBtn = e.target.closest('[data-action="set-withdraw-amount"]');
      if (setAmtBtn) {
        setWithdrawAmount(Number(setAmtBtn.dataset.amount));
        return;
      }

      // 11. Gửi yêu cầu rút tiền
      const submitWthBtn = e.target.closest('[data-action="submit-withdrawal"]');
      if (submitWthBtn) {
        submitWithdrawal();
        return;
      }
    });
  }

  const urlParams = new URLSearchParams(window.location.search);
  const targetBookingId = urlParams.get('bookingId');
  if (targetBookingId) {
    const tabs = document.querySelectorAll('.tab');
    const allTab = tabs[tabs.length - 2]; // Tab 'Tất cả'
    loadBookings('', allTab);
  } else {
    loadBookings('PENDING');
  }
});

async function loadBookings(status, tabElement) {
  if (tabElement) {
    document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
    tabElement.classList.add('active');
  }

  const container = document.getElementById('booking-list');
  if (!container) return;
  container.innerHTML = '<div class="loading-box">Đang tải danh sách yêu cầu...</div>';

  try {
    const url = status ? `/sitter/bookings?status=${status}` : '/sitter/bookings';
    const bookings = await apiFetch(url);
    
    if (bookings.length === 0) {
      container.innerHTML = '<p class="empty-table-msg">Không có yêu cầu nào.</p>';
      return;
    }

    container.innerHTML = bookings.map(b => {
      let actionHtml = '';
      const vnTodayStr = new Date(Date.now() + 7 * 3600000).toISOString().split('T')[0];
      const endVnStr = new Date(new Date(b.ngayKetThuc).getTime() + 7 * 3600000).toISOString().split('T')[0];
      const isPastEnd = endVnStr < vnTodayStr;

      if (b.trangThai === 'PENDING') {
        actionHtml = `
          <div class="action-group">
            <button class="btn btn-primary btn-sm" data-action="accept-booking" data-booking-id="${/* safe */ b.bookingId}">Chấp nhận</button>
            <button class="btn btn-outline btn-sm" data-action="show-reject" data-booking-id="${/* safe */ b.bookingId}">Từ chối</button>
          </div>
          <div class="reject-wrap hidden" id="reject-${/* safe */ b.bookingId}">
            <input type="text" class="reject-input" id="lydo-${/* safe */ b.bookingId}" placeholder="Nhập lý do từ chối (tối đa 500 ký tự)..." maxlength="500" />
            <button class="btn btn-outline btn-sm" data-action="confirm-reject" data-booking-id="${/* safe */ b.bookingId}">Xác nhận từ chối</button>
          </div>
        `;
      } else if (b.trangThai === 'ACCEPTED') {
        const canCancel = new Date(b.ngayBatDau).getTime() > Date.now();
        actionHtml = `
          <div class="action-group">
            <a href="stay-updates.html?bookingId=${/* safe */ b.bookingId}" class="btn btn-outline btn-sm">Nhật ký chăm sóc</a>
            ${isPastEnd 
              ? `<button class="btn btn-primary btn-sm" data-action="complete-booking" data-booking-id="${/* safe */ b.bookingId}">Đánh dấu hoàn tất</button>`
              : `<button class="btn btn-primary btn-sm btn-disabled-placeholder" disabled title="Chỉ có thể đánh dấu sau ngày kết thúc lưu trú">Đánh dấu hoàn tất (chưa qua ngày kết thúc)</button>`
            }
            ${canCancel ? `<button class="btn btn-outline btn-sm btn-danger-outline" data-action="show-sitter-cancel" data-booking-id="${/* safe */ b.bookingId}">Hủy nhận đơn</button>` : ''}
          </div>
          <div class="reject-wrap hidden" id="cancel-${/* safe */ b.bookingId}">
            <input type="text" class="reject-input" id="cancel-reason-${/* safe */ b.bookingId}" placeholder="Nhập lý do hủy (bắt buộc, tối đa 500 ký tự)..." maxlength="500" />
            <button class="btn btn-outline btn-sm" data-action="confirm-sitter-cancel" data-booking-id="${/* safe */ b.bookingId}">Xác nhận hủy</button>
          </div>
        `;
      } else if (b.trangThai === 'COMPLETION_PENDING') {
        const timeRem = formatTimeRemaining(b.completionDeadline);
        actionHtml = `
          <div class="notice-purple">
            <strong>⏳ Chờ chủ xác nhận hoàn tất:</strong> ${escapeHtml(timeRem)} (hạn chót: ${formatNgayDep(b.completionDeadline)})
          </div>
        `;
      } else if (b.trangThai === 'DISPUTED') {
        actionHtml = `
          <div class="notice-orange">
            <strong>⚠️ Đơn đang tranh chấp:</strong> Đang chờ Quản trị viên can thiệp xử lý.
            ${/* safe */ b.disputeReason ? `<br/><em>Lý do chủ nuôi khiếu nại: ${escapeHtml(b.disputeReason)}</em>` : ''}
          </div>
        `;
      }

      let statusClass = 'status-pending';
      if (b.trangThai === 'ACCEPTED') statusClass = 'status-accepted';
      if (b.trangThai === 'COMPLETION_PENDING') statusClass = 'status-completion-pending';
      if (b.trangThai === 'COMPLETED') statusClass = 'status-completed';
      if (b.trangThai === 'DISPUTED') statusClass = 'status-disputed';
      if (b.trangThai === 'CANCELLED' || b.trangThai === 'REJECTED') statusClass = 'status-rejected';
      if (b.trangThai === 'EXPIRED') statusClass = 'status-expired';

      let paymentBadge = '';
      if (b.paymentStatus === 'PENDING_PAYMENT') {
        paymentBadge = `<span class="status-badge badge-pay-pending">Chờ thanh toán</span>`;
      } else if (b.paymentStatus === 'HELD') {
        paymentBadge = `<span class="status-badge badge-pay-held">Đã thanh toán (Escrow giữ)</span>`;
      } else if (b.paymentStatus === 'RELEASED') {
        paymentBadge = `<span class="status-badge badge-pay-released">Đã giải ngân</span>`;
      } else if (b.paymentStatus === 'REFUND_PENDING') {
        paymentBadge = `<span class="status-badge badge-pay-refund-pending">Đang hoàn tiền</span>`;
      } else if (b.paymentStatus === 'REFUNDED') {
        paymentBadge = `<span class="status-badge badge-pay-refunded">Đã hoàn tiền</span>`;
      } else if (b.paymentStatus === 'PARTIALLY_REFUNDED') {
        paymentBadge = `<span class="status-badge badge-pay-partial">Hoàn một phần</span>`;
      } else if (b.paymentStatus === 'CANCELLED') {
        paymentBadge = `<span class="status-badge badge-pay-cancelled">Hủy thanh toán</span>`;
      }

      return `
        <div class="booking-card" id="card-${/* safe */ b.bookingId}">
          <div class="booking-header">
            <div>
              <strong>Mã: ${escapeHtml(b.maDatLich)}</strong>
            </div>
            <div class="booking-header-right">
              <span class="status-badge ${/* safe */ statusClass}">${escapeHtml(getStatusLabel(b.trangThai))}</span>
              ${/* safe */ paymentBadge}
            </div>
          </div>
          <div class="booking-grid-info">
            <div>
              <p><strong>Ngày:</strong> ${formatNgayDep(b.ngayBatDau)} - ${formatNgayDep(b.ngayKetThuc)} (${/* safe */ b.soNgay} ngày)</p>
              <p><strong>Thú cưng:</strong> ${escapeHtml(b.pet?.tenPet || '')} (${escapeHtml(b.pet?.loaiPet || '')}, ${escapeHtml(b.pet?.kichCo || '')})</p>
              ${/* safe */ b.ghiChu ? `<p><strong>Ghi chú:</strong> <em>${escapeHtml(b.ghiChu)}</em></p>` : ''}
              ${/* safe */ (b.trangThai === 'PENDING' && b.expiresAt) ? `<p class="expiry-text"><strong>Hạn phản hồi:</strong> ${formatNgayDep(b.expiresAt)}</p>` : ''}
            </div>
            <div>
              <p><strong>Chi phí ước tính:</strong> ${formatTien(b.tongTien)}</p>
              <p class="subtext-muted">(Bao gồm ${formatTien(b.giaSitter)} phí trông + ${formatTien(b.tongAddons)} phụ phí)</p>
              ${/* safe */ b.lyDoTuChoi ? `<p class="error-text-red"><strong>Lý do:</strong> ${escapeHtml(b.lyDoTuChoi)}</p>` : ''}
            </div>
          </div>
          ${/* safe */ actionHtml}
        </div>
      `;
    }).join('');

    const targetBookingId = new URLSearchParams(window.location.search).get('bookingId');
    if (targetBookingId) {
      const targetCard = document.getElementById(`card-${targetBookingId}`);
      if (targetCard) {
        targetCard.scrollIntoView({ behavior: 'smooth', block: 'center' });
        targetCard.classList.add('booking-highlight');
      }
    }
  } catch (err) {
    showError(err.message || 'Lỗi tải danh sách. Bạn có quyền Sitter không?');
    container.innerHTML = '';
  }
}

async function acceptBooking(id) {
  try {
    const res = await apiFetch(`/sitter/bookings/${id}/accept`, { method: 'PATCH' });
    if (res && res.warning) {
      alert(res.warning);
    }
    loadBookings('PENDING', document.querySelector('.tab.active'));
  } catch (e) {
    showError(e.message);
  }
}

async function rejectBooking(id) {
  const input = document.getElementById(`lydo-${id}`);
  const reason = input ? input.value : '';
  if (!reason || !reason.trim()) {
    showError('Vui lòng nhập lý do từ chối.');
    return;
  }
  try {
    await apiFetch(`/sitter/bookings/${id}/reject`, {
      method: 'PATCH',
      body: JSON.stringify({ lyDo: reason.trim() })
    });
    loadBookings('PENDING', document.querySelector('.tab.active'));
  } catch (e) {
    showError(e.message);
  }
}

async function sitterCancelBooking(id) {
  const reasonInput = document.getElementById(`cancel-reason-${id}`);
  const reason = reasonInput ? reasonInput.value.trim() : '';
  if (!reason) {
    showError('Vui lòng nhập lý do hủy đơn.');
    return;
  }
  try {
    await apiFetch(`/sitter/bookings/${id}/cancel`, {
      method: 'PATCH',
      body: JSON.stringify({ lyDo: reason })
    });
    loadBookings('ACCEPTED', document.querySelector('.tab.active'));
  } catch (e) {
    showError(e.message);
  }
}

async function completeBooking(id) {
  try {
    await apiFetch(`/sitter/bookings/${id}/complete`, { method: 'PATCH' });
    loadBookings('ACCEPTED', document.querySelector('.tab.active'));
  } catch (e) {
    showError(e.message);
  }
}

async function loadEarnings(tabElement) {
  if (tabElement) {
    document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
    tabElement.classList.add('active');
  }
  const container = document.getElementById('booking-list');
  if (!container) return;
  container.innerHTML = '<div class="loading-box">⏳ Đang tải thông tin thu nhập, ví và tài khoản ngân hàng...</div>';

  try {
    const [wallet, withdrawalsRes, historyRes] = await Promise.all([
      apiFetch('/wallet/summary'),
      apiFetch('/wallet/withdrawals'),
      apiFetch('/wallet/history')
    ]);

    const historyItems = historyRes || [];
    const withdrawals = withdrawalsRes || [];

    const typeLabels = {
      'BOOKING_PAYOUT': 'Thù lao trông giữ',
      'COMMISSION': 'Phí dịch vụ sàn',
      'DISPUTE_SPLIT': 'Hòa giải tranh chấp',
      'CANCEL_COMPENSATION': 'Bồi thường hủy đơn',
      'WITHDRAWAL': 'Rút tiền về tài khoản'
    };

    const statusLabels = {
      'REQUESTED': '<span class="status-badge badge-wth-requested">Chờ duyệt</span>',
      'APPROVED': '<span class="status-badge badge-wth-approved">Đã duyệt</span>',
      'PROCESSING': '<span class="status-badge badge-wth-processing">Đang xử lý</span>',
      'PAID': '<span class="status-badge badge-wth-paid">Đã thanh toán</span>',
      'REJECTED': '<span class="status-badge badge-wth-rejected">Bị từ chối</span>',
      'CANCELLED': '<span class="status-badge badge-wth-cancelled">Đã hủy</span>',
      'FAILED': '<span class="status-badge badge-wth-failed">Thất bại</span>'
    };

    const historyHtml = historyItems.length === 0 
      ? '<p class="empty-table-msg">Chưa có giao dịch biến động nào được ghi nhận.</p>'
      : `
        <div class="table-responsive">
          <table class="data-table">
            <thead>
              <tr>
                <th>Thời gian</th>
                <th>Mã đơn</th>
                <th>Loại giao dịch</th>
                <th>Số tiền</th>
                <th>Trạng thái</th>
              </tr>
            </thead>
            <tbody>
              ${historyItems.map(item => {
                const sign = item.direction === 'OUT' ? '-' : '+';
                const colorCls = item.direction === 'OUT' ? 'text-red' : 'text-green';
                const timeStr = new Date(item.createdAt).toLocaleString('vi-VN');
                const availableStr = item.isAvailable 
                  ? '<span class="text-green">✅ Sẵn sàng rút</span>' 
                  : `<span class="subtext-muted">⏳ Tạm giữ (mở: ${/* safe */ item.availableAt ? new Date(item.availableAt).toLocaleString('vi-VN') : 'chờ'})</span>`;

                return `
                  <tr>
                    <td class="subtext-muted">${/* safe */ timeStr}</td>
                    <td><strong>${escapeHtml(item.bookingId ? '#' + item.bookingId.substring(0, 8) : '—')}</strong></td>
                    <td>${escapeHtml(typeLabels[item.type] || item.type)}</td>
                    <td class="${/* safe */ colorCls}"><strong>${/* safe */ sign}${formatTien(Math.abs(item.amount))}</strong></td>
                    <td>${/* safe */ availableStr}</td>
                  </tr>
                `;
              }).join('')}
            </tbody>
          </table>
        </div>
      `;

    const withdrawalsHtml = withdrawals.length === 0
      ? '<p class="empty-table-msg">Bạn chưa có yêu cầu rút tiền nào.</p>'
      : `
        <div class="table-responsive">
          <table class="data-table">
            <thead>
              <tr>
                <th>Mã yêu cầu</th>
                <th>Thời gian</th>
                <th>Số tiền rút</th>
                <th>Tài khoản nhận</th>
                <th>Trạng thái</th>
                <th>Thao tác</th>
              </tr>
            </thead>
            <tbody>
              ${withdrawals.map(w => {
                const timeStr = new Date(w.createdAt).toLocaleString('vi-VN');
                const cancelBtn = w.status === 'REQUESTED' 
                  ? `<button class="btn btn-outline btn-sm btn-danger-outline" data-action="cancel-withdrawal" data-withdrawal-id="${/* safe */ w.withdrawalId}">Hủy</button>` 
                  : (w.bankTransactionRef ? `<span class="subtext-muted">Mã GD: ${escapeHtml(w.bankTransactionRef)}</span>` : '—');
                return `
                  <tr>
                    <td><code>#${/* safe */ w.withdrawalId.substring(0, 8)}</code></td>
                    <td class="subtext-muted">${/* safe */ timeStr}</td>
                    <td class="text-green"><strong>${formatTien(w.amount)}</strong></td>
                    <td>${escapeHtml(w.bankName)} - <strong>${escapeHtml(w.accountNumberMasked)}</strong><br/><small class="subtext-muted">${escapeHtml(w.accountHolderName)}</small></td>
                    <td>${/* safe */ statusLabels[w.status] || w.status}</td>
                    <td>${/* safe */ cancelBtn}</td>
                  </tr>
                `;
              }).join('')}
            </tbody>
          </table>
        </div>
      `;

    const bankInfo = wallet.bankAccount;
    const bankDisplayHtml = bankInfo && bankInfo.accountNumberMasked
      ? `
        <div class="bank-info-box">
          <div class="bank-info-head">
            <div>
              <div class="bank-label-sub">Ngân hàng thụ hưởng</div>
              <strong class="bank-name-text">${escapeHtml(bankInfo.bankName || '')}</strong>
              <div class="bank-number-text">${escapeHtml(bankInfo.accountNumberMasked)}</div>
              <div class="bank-holder-text">${escapeHtml(bankInfo.accountHolderName || '')}</div>
            </div>
            <button class="btn btn-outline btn-sm" data-action="toggle-bank-edit" data-show="true">Thay đổi STK</button>
          </div>
          ${/* safe */ wallet.freezeUntil ? `<div class="freeze-warning">🔒 Tài khoản đang trong thời gian đóng băng an toàn 24h sau khi cập nhật (mở lúc: ${new Date(wallet.freezeUntil).toLocaleString('vi-VN')}).</div>` : ''}
        </div>
      `
      : `
        <div class="bank-empty-box">
          <div class="bank-info-head">
            <div>
              <strong>⚠️ Chưa liên kết tài khoản ngân hàng</strong>
              <div class="bank-label-sub">Vui lòng cập nhật tài khoản ngân hàng chính chủ để có thể thực hiện rút tiền.</div>
            </div>
            <button class="btn btn-primary btn-sm" data-action="toggle-bank-edit" data-show="true">Cập nhật ngay</button>
          </div>
        </div>
      `;

    container.innerHTML = `
      <!-- Metric Cards -->
      <div class="wallet-metrics-grid">
        <div class="wallet-metric-card wallet-card-green">
          <span class="wallet-metric-label">Số dư khả dụng</span>
          <h2 class="wallet-metric-val text-green">${formatTien(wallet.availableBalance || 0)}</h2>
          <span class="wallet-subtext">Sẵn sàng rút về tài khoản</span>
        </div>
        <div class="wallet-metric-card wallet-card-amber">
          <span class="wallet-metric-label">Đang tạm giữ (48h)</span>
          <h2 class="wallet-metric-val text-amber">${formatTien(wallet.pendingHoldBalance || 0)}</h2>
          <span class="wallet-subtext">Tạm giữ đối soát sau chuyến đi</span>
        </div>
        <div class="wallet-metric-card wallet-card-indigo">
          <span class="wallet-metric-label">Đang xử lý rút</span>
          <h2 class="wallet-metric-val text-indigo">${formatTien(wallet.reservedAmount || 0)}</h2>
          <span class="wallet-subtext">Đang chờ Admin duyệt/chuyển khoản</span>
        </div>
        <div class="wallet-metric-card wallet-card-slate">
          <span class="wallet-metric-label">Đã rút thành công</span>
          <h2 class="wallet-metric-val text-slate">${formatTien(wallet.withdrawnTotal || 0)}</h2>
          <span class="wallet-subtext">Tổng số tiền đã nhận</span>
        </div>
      </div>

      <!-- Bank & Withdrawal Panel -->
      <div class="wallet-panel-grid">
        <!-- Bank Account Card -->
        <div class="wallet-section-card">
          <h3 class="wallet-card-title">🏦 Tài khoản ngân hàng thụ hưởng</h3>
          ${/* safe */ bankDisplayHtml}
          
          <!-- Update Bank Form (hidden by default) -->
          <div id="bank-edit-form" class="bank-edit-form hidden">
            <h4 class="wallet-card-title">Cập nhật thông tin ngân hàng</h4>
            <div class="bank-form-group">
              <label class="bank-form-label">Tên ngân hàng (Vietcombank, MB, Techcombank,...)</label>
              <input type="text" class="bank-form-input" id="bank-input-name" placeholder="VD: MB Bank" />
            </div>
            <div class="bank-form-group">
              <label class="bank-form-label">Số tài khoản</label>
              <input type="text" class="bank-form-input" id="bank-input-number" placeholder="VD: 0987654321" />
            </div>
            <div class="bank-form-group">
              <label class="bank-form-label">Tên chủ tài khoản (viết hoa không dấu)</label>
              <input type="text" class="bank-form-input" id="bank-input-holder" placeholder="VD: NGUYEN VAN A" />
            </div>
            <div class="bank-form-group-last">
              <label class="bank-form-label">Mật khẩu tài khoản (để xác thực bảo mật)</label>
              <input type="password" class="bank-form-input" id="bank-input-password" placeholder="Nhập mật khẩu PawStay của bạn" />
            </div>
            <div class="bank-freeze-hint">⚠️ Lưu ý: Sau khi cập nhật, tài khoản sẽ bị đóng băng an toàn trong 24 giờ trước khi có thể rút tiền.</div>
            <div class="actions-right-gap8">
              <button class="btn btn-outline btn-sm" data-action="toggle-bank-edit" data-show="false">Hủy</button>
              <button class="btn btn-primary btn-sm" data-action="save-bank-account">Lưu thông tin</button>
            </div>
          </div>
        </div>

        <!-- Withdraw Request Form Card -->
        <div class="wallet-section-card">
          <h3 class="wallet-card-title">💸 Yêu cầu rút tiền</h3>
          <div class="withdraw-rules">
            <div>• Khả dụng rút ngay: <strong class="text-green">${formatTien(wallet.availableBalance || 0)}</strong></div>
            <div>• Hạn mức ngày còn lại: <strong>${formatTien(wallet.dailyLimitRemaining || 0)}</strong> / 20.000.000 ₫</div>
            <div>• Mức rút tối thiểu: <strong>50.000 ₫/lần</strong></div>
          </div>
          <div class="bank-form-group-last">
            <label class="bank-form-label">Số tiền muốn rút (₫)</label>
            <input type="number" class="withdraw-input-lg" id="withdraw-amount-input" min="50000" max="${/* safe */ wallet.availableBalance || 0}" step="10000" placeholder="VD: 500000" />
          </div>
          <div class="action-group">
            <button type="button" class="btn btn-outline btn-sm" data-action="set-withdraw-amount" data-amount="${/* safe */ wallet.availableBalance || 0}">Rút tất cả (${formatTien(wallet.availableBalance || 0)})</button>
            <button type="button" class="btn btn-outline btn-sm" data-action="set-withdraw-amount" data-amount="500000">500.000 ₫</button>
            <button type="button" class="btn btn-outline btn-sm" data-action="set-withdraw-amount" data-amount="1000000">1.000.000 ₫</button>
          </div>
          <button class="btn btn-primary btn-full-p10" data-action="submit-withdrawal" ${/* safe */ (!wallet.canWithdraw || (wallet.availableBalance || 0) < 50000) ? 'disabled' : ''}>
            Gửi yêu cầu rút tiền
          </button>
          ${/* safe */ !wallet.canWithdraw ? `<div class="withdraw-disabled-hint">${/* safe */ wallet.freezeUntil ? 'Tài khoản đang đóng băng 24h.' : 'Chưa đủ điều kiện rút tiền (cần thêm STK hoặc hết hạn mức ngày).'}</div>` : ''}
        </div>
      </div>

      <!-- Withdrawal Requests History -->
      <div class="wallet-section-card">
        <div class="table-header-flex">
          <h3 class="wallet-card-title">Danh sách yêu cầu rút tiền</h3>
          <span class="subtext-muted">${/* safe */ withdrawals.length} yêu cầu</span>
        </div>
        ${/* safe */ withdrawalsHtml}
      </div>

      <!-- Ledger History -->
      <div class="wallet-section-card">
        <div class="table-header-flex">
          <h3 class="wallet-card-title">Lịch sử biến động số dư ví</h3>
          <span class="subtext-muted">${/* safe */ historyItems.length} giao dịch</span>
        </div>
        ${/* safe */ historyHtml}
      </div>
    `;
  } catch (err) {
    showError(err.message || 'Lỗi tải thông tin thu nhập.');
    container.innerHTML = '<p class="empty-table-msg">Không thể tải dữ liệu thu nhập & ví.</p>';
  }
}

function toggleBankEdit(show) {
  const el = document.getElementById('bank-edit-form');
  if (el) {
    if (show) {
      el.classList.remove('hidden');
    } else {
      el.classList.add('hidden');
    }
  }
}

function setWithdrawAmount(amt) {
  const el = document.getElementById('withdraw-amount-input');
  if (el) el.value = amt;
}

async function saveBankAccount() {
  const bankName = (document.getElementById('bank-input-name')?.value || '').trim();
  const accountNumber = (document.getElementById('bank-input-number')?.value || '').trim();
  const accountHolderName = (document.getElementById('bank-input-holder')?.value || '').trim();
  const password = (document.getElementById('bank-input-password')?.value || '').trim();

  if (!bankName || !accountNumber || !accountHolderName) {
    showError('Vui lòng điền đầy đủ tên ngân hàng, số tài khoản và tên chủ tài khoản.');
    return;
  }
  if (!password) {
    showError('Vui lòng nhập mật khẩu đăng nhập để xác nhận đổi tài khoản ngân hàng.');
    return;
  }

  try {
    await apiFetch('/wallet/bank-account', {
      method: 'PUT',
      body: JSON.stringify({ bankName, accountNumber, accountHolderName, password })
    });
    if (typeof showToast === 'function') {
      showToast('Đã lưu thông tin tài khoản ngân hàng thành công!', 3000, '✅');
    }
    loadEarnings();
  } catch (err) {
    showError(err.message || 'Lỗi khi cập nhật tài khoản ngân hàng.');
  }
}

async function submitWithdrawal() {
  const input = document.getElementById('withdraw-amount-input');
  const amount = parseInt(input?.value || '0', 10);
  if (!amount || amount < 50000) {
    showError('Số tiền rút tối thiểu là 50.000 ₫.');
    return;
  }

  if (!confirm(`Xác nhận yêu cầu rút ${formatTien(amount)} về tài khoản ngân hàng của bạn?`)) return;

  const idempotencyKey = 'WTH_' + Date.now() + '_' + Math.random().toString(36).substring(2, 8);
  try {
    await apiFetch('/wallet/withdraw', {
      method: 'POST',
      headers: { 'Idempotency-Key': idempotencyKey },
      body: JSON.stringify({ amount })
    });
    if (typeof showToast === 'function') {
      showToast('Đã gửi yêu cầu rút tiền thành công!', 3000, '✅');
    }
    loadEarnings();
  } catch (err) {
    showError(err.message || 'Lỗi khi gửi yêu cầu rút tiền.');
  }
}

async function cancelWithdrawal(withdrawalId) {
  if (!confirm('Bạn có chắc chắn muốn hủy yêu cầu rút tiền này? Số tiền bị giữ sẽ được hoàn lại số dư khả dụng ngay lập tức.')) return;
  try {
    await apiFetch(`/wallet/withdrawals/${withdrawalId}/cancel`, { method: 'POST' });
    if (typeof showToast === 'function') {
      showToast('Đã hủy yêu cầu rút tiền thành công!', 3000, '✅');
    }
    loadEarnings();
  } catch (err) {
    showError(err.message || 'Lỗi khi hủy yêu cầu rút tiền.');
  }
}
