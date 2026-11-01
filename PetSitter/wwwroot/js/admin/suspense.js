/**
 * Admin SUSPENSE Management Module (suspense.js)
 * Phase C2 - Round 4D-2
 * CSP Compliant: No inline styles, no inline event handlers. Event delegation via closest('[data-action]').
 */

(function () {
  'use strict';

  let currentSuspensePage = 1;
  let currentSuspenseStatus = 'ABNORMAL';

  async function loadSuspensePayments(page = 1, status = currentSuspenseStatus) {
    currentSuspensePage = page;
    currentSuspenseStatus = status;

    const container = document.getElementById('list-container');
    const subtabs = document.getElementById('subtabs-container');
    if (subtabs) subtabs.style.display = 'none';
    if (!container) return;

    container.innerHTML = '<div class="admin-loading-state">Đang tải danh sách giao dịch SUSPENSE...</div>';

    try {
      let url = `/admin/payments/suspense?page=${page}&pageSize=15`;
      if (status && status !== 'ALL') {
        url += `&status=${encodeURIComponent(status)}`;
      }
      const res = await apiFetch(url);
      const items = res.data || [];
      const total = res.total || 0;
      const totalPages = res.totalPages || 1;

      const filterBar = `
        <div class="suspense-filter-bar">
          <div class="suspense-subtabs-group">
            <button class="btn-subtab ${status === 'ABNORMAL' ? 'active' : ''}" data-action="switch-suspense-status" data-status="ABNORMAL">Chờ xử lý (ABNORMAL)</button>
            <button class="btn-subtab ${status === 'REFUND_PENDING' ? 'active' : ''}" data-action="switch-suspense-status" data-status="REFUND_PENDING">Chờ hoàn tiền (REFUND_PENDING)</button>
            <button class="btn-subtab ${status === 'PAID' ? 'active' : ''}" data-action="switch-suspense-status" data-status="PAID">Đã gán đơn (PAID)</button>
            <button class="btn-subtab ${status === 'ALL' ? 'active' : ''}" data-action="switch-suspense-status" data-status="ALL">Tất cả (${total})</button>
          </div>
          <button class="btn btn-outline btn-sm" data-action="reload-suspense">🔄 Làm mới</button>
        </div>
      `;

      if (!items || items.length === 0) {
        container.innerHTML = `
          <div class="suspense-card">
            <h3 class="suspense-title">Giao dịch bất thường (Tài khoản SUSPENSE)</h3>
            <p class="suspense-desc">Các khoản thanh toán chuyển sai số tiền hoặc gửi thừa tiền, tạm giữ an toàn trong tài khoản trung gian SUSPENSE chờ Admin xử lý gán hoặc hoàn trả.</p>
            ${filterBar}
            <div class="admin-empty-state">Không có giao dịch bất thường nào phù hợp với bộ lọc hiện tại.</div>
          </div>
        `;
        return;
      }

      const rows = items.map(p => {
        const receivedAmt = p.receivedAmount != null ? p.receivedAmount : p.amount;
        let statusBadge = '<span class="suspense-badge badge-paid">ĐÃ GÁN ĐƠN</span>';
        if (p.status === 'ABNORMAL') {
          statusBadge = '<span class="suspense-badge badge-abnormal">CHỜ XỬ LÝ</span>';
        } else if (p.status === 'REFUND_PENDING') {
          statusBadge = '<span class="suspense-badge badge-refund-pending">CHỜ HOÀN TIỀN</span>';
        }

        const actionBtns = (p.status === 'ABNORMAL') ? `
          <button class="btn btn-primary btn-sm" id="btn-assign-${p.orderCode}" data-action="open-assign-modal" data-order-code="${p.orderCode}">Gán vào đơn</button>
          <button class="btn btn-outline btn-sm btn-danger-outline" id="btn-refund-${p.orderCode}" data-action="open-refund-modal" data-order-code="${p.orderCode}">Hoàn trả</button>
          <button class="btn btn-outline btn-sm" data-action="open-decrypt-modal" data-order-code="${p.orderCode}">👁️ STK người gửi</button>
        ` : `
          <button class="btn btn-outline btn-sm" data-action="open-decrypt-modal" data-order-code="${p.orderCode}">👁️ STK người gửi</button>
        `;

        return `
          <tr class="suspense-tr">
            <td class="suspense-td"><strong>${escapeHtml(p.orderCode ? p.orderCode.toString() : '')}</strong></td>
            <td class="suspense-td text-sm"><code>${escapeHtml(p.bookingId || '—')}</code></td>
            <td class="suspense-td text-sm text-muted">${new Date(p.createdAt).toLocaleString('vi-VN')}</td>
            <td class="suspense-td-amount">${formatTien(receivedAmt)}</td>
            <td class="suspense-td">${statusBadge}</td>
            <td class="suspense-td-reason">${escapeHtml(p.abnormalReason || 'Sai số tiền thanh toán')}</td>
            <td class="suspense-td">
              <div class="suspense-actions-flex">
                ${actionBtns}
              </div>
            </td>
          </tr>
        `;
      }).join('');

      const isPrevDisabled = page <= 1;
      const isNextDisabled = page >= totalPages;

      const pagination = `
        <div class="suspense-pagination">
          <div>Trang <strong>${page}</strong> / <strong>${totalPages}</strong> (Tổng <strong>${total}</strong> giao dịch)</div>
          <div class="suspense-pagination-btns">
            <button class="btn btn-outline btn-sm" ${isPrevDisabled ? 'disabled' : ''} data-action="prev-suspense-page">◀ Trang trước</button>
            <button class="btn btn-outline btn-sm" ${isNextDisabled ? 'disabled' : ''} data-action="next-suspense-page">Trang sau ▶</button>
          </div>
        </div>
      `;

      container.innerHTML = `
        <div class="suspense-card">
          <h3 class="suspense-title">Giao dịch bất thường (Tài khoản SUSPENSE)</h3>
          <p class="suspense-desc">Các khoản thanh toán chuyển sai số tiền hoặc gửi thừa tiền, tạm giữ an toàn trong tài khoản trung gian SUSPENSE chờ Admin xử lý gán hoặc hoàn trả.</p>
          ${filterBar}
          <div class="suspense-table-wrap">
            <table class="suspense-table">
              <thead>
                <tr>
                  <th class="suspense-th">Mã OrderCode</th>
                  <th class="suspense-th">BookingId gốc</th>
                  <th class="suspense-th">Thời gian tạo</th>
                  <th class="suspense-th">Số tiền thực nhận</th>
                  <th class="suspense-th">Trạng thái</th>
                  <th class="suspense-th">Lý do bất thường</th>
                  <th class="suspense-th">Thao tác xử lý</th>
                </tr>
              </thead>
              <tbody>${rows}</tbody>
            </table>
          </div>
          ${pagination}
        </div>
      `;
    } catch (err) {
      showError(err.message || 'Lỗi khi tải danh sách giao dịch SUSPENSE.');
      container.innerHTML = '';
    }
  }

  function openSuspenseAssignModal(orderCode) {
    const codeEl = document.getElementById('assign-order-code');
    const bookEl = document.getElementById('assign-booking-id');
    const noteEl = document.getElementById('assign-note');
    const modal = document.getElementById('suspense-assign-modal');

    if (codeEl) codeEl.value = orderCode || '';
    if (bookEl) bookEl.value = '';
    if (noteEl) noteEl.value = '';
    if (modal) {
      modal.classList.add('is-active');
      modal.style.display = 'flex';
    }
  }

  function closeSuspenseAssignModal() {
    const modal = document.getElementById('suspense-assign-modal');
    if (modal) {
      modal.classList.remove('is-active');
      modal.style.display = 'none';
    }
    const form = document.getElementById('suspense-assign-form');
    if (form) form.reset();
  }

  async function submitSuspenseAssign() {
    const btn = document.getElementById('btn-submit-suspense-assign');
    const orderCode = document.getElementById('assign-order-code')?.value;
    const bookingId = document.getElementById('assign-booking-id')?.value.trim();
    const note = document.getElementById('assign-note')?.value.trim();

    if (!bookingId) {
      showError('Vui lòng nhập mã đơn đặt lịch (BookingId) cần gán.');
      return;
    }

    if (!confirm(`Xác nhận gán giao dịch ${orderCode} vào đơn ${bookingId}?\nThao tác này sẽ chuyển đơn sang HELD (Đã thanh toán) và ghi nhận bút toán sổ cái kép.`)) {
      return;
    }

    if (btn) {
      btn.disabled = true;
      btn.dataset.origText = btn.textContent;
      btn.textContent = 'Đang xử lý...';
    }

    try {
      await apiFetch(`/admin/payments/suspense/${orderCode}/assign`, {
        method: 'POST',
        body: JSON.stringify({ bookingId, note: note || 'Admin gán từ giao dịch SUSPENSE' })
      });
      showToast('Đã gán giao dịch bất thường vào đơn thành công!', 3000, '✅');
      closeSuspenseAssignModal();
      loadSuspensePayments(currentSuspensePage, currentSuspenseStatus);
    } catch (err) {
      showError(err.message || 'Lỗi khi gán giao dịch bất thường.');
    } finally {
      if (btn) {
        btn.disabled = false;
        btn.textContent = btn.dataset.origText || 'Xác nhận gán (SUSPENSE -> ESCROW)';
      }
    }
  }

  function openSuspenseRefundModal(orderCode) {
    const codeEl = document.getElementById('refund-order-code');
    const reasonEl = document.getElementById('refund-reason');
    const modal = document.getElementById('suspense-refund-modal');

    if (codeEl) codeEl.value = orderCode || '';
    if (reasonEl) reasonEl.value = '';
    if (modal) {
      modal.classList.add('is-active');
      modal.style.display = 'flex';
    }
  }

  function closeSuspenseRefundModal() {
    const modal = document.getElementById('suspense-refund-modal');
    if (modal) {
      modal.classList.remove('is-active');
      modal.style.display = 'none';
    }
    const form = document.getElementById('suspense-refund-form');
    if (form) form.reset();
  }

  async function submitSuspenseRefund() {
    const btn = document.getElementById('btn-submit-suspense-refund');
    const orderCode = document.getElementById('refund-order-code')?.value;
    const reason = document.getElementById('refund-reason')?.value.trim();

    if (!reason) {
      showError('Vui lòng nhập lý do hoàn tiền.');
      return;
    }

    if (!confirm(`Xác nhận tạo yêu cầu hoàn trả tiền cho giao dịch ${orderCode}?`)) {
      return;
    }

    if (btn) {
      btn.disabled = true;
      btn.dataset.origText = btn.textContent;
      btn.textContent = 'Đang xử lý...';
    }

    try {
      await apiFetch(`/admin/payments/suspense/${orderCode}/refund`, {
        method: 'POST',
        body: JSON.stringify({ reason })
      });
      showToast('Đã tạo yêu cầu hoàn tiền cho người trả!', 3000, '✅');
      closeSuspenseRefundModal();
      loadSuspensePayments(currentSuspensePage, currentSuspenseStatus);
    } catch (err) {
      showError(err.message || 'Lỗi khi tạo yêu cầu hoàn tiền.');
    } finally {
      if (btn) {
        btn.disabled = false;
        btn.textContent = btn.dataset.origText || 'Tạo yêu cầu hoàn tiền (SUSPENSE -> REFUND_PENDING)';
      }
    }
  }

  async function showDecryptedSuspensePayer(orderCode) {
    try {
      const data = await apiFetch(`/payments/${orderCode}/decrypted-account`);
      const bankNameEl = document.getElementById('decrypt-bank-name');
      const accountNumEl = document.getElementById('decrypt-account-number');
      const accountHolderEl = document.getElementById('decrypt-account-holder');
      const modal = document.getElementById('bank-decrypt-modal');

      if (bankNameEl) bankNameEl.textContent = data.bankName || '—';
      if (accountNumEl) accountNumEl.textContent = data.accountNumber || '—';
      if (accountHolderEl) accountHolderEl.textContent = data.accountName || '—';
      if (modal) {
        modal.classList.add('is-active');
        modal.style.display = 'flex';
      }
    } catch (err) {
      showError(err.message || 'Không tìm thấy thông tin tài khoản người trả.');
    }
  }

  function closeDecryptedModal() {
    // Bảo mật: Xóa thông tin giải mã khỏi DOM ngay khi đóng
    const bankNameEl = document.getElementById('decrypt-bank-name');
    const accountNumEl = document.getElementById('decrypt-account-number');
    const accountHolderEl = document.getElementById('decrypt-account-holder');
    const modal = document.getElementById('bank-decrypt-modal');

    if (bankNameEl) bankNameEl.textContent = '—';
    if (accountNumEl) accountNumEl.textContent = '—';
    if (accountHolderEl) accountHolderEl.textContent = '—';
    if (modal) {
      modal.classList.remove('is-active');
      modal.style.display = 'none';
    }
  }

  // Event Delegation
  document.addEventListener('click', function (e) {
    const actionEl = e.target.closest('[data-action]');
    if (!actionEl) return;

    const action = actionEl.getAttribute('data-action');
    if (action === 'switch-suspense-status') {
      const status = actionEl.getAttribute('data-status') || 'ABNORMAL';
      loadSuspensePayments(1, status);
    } else if (action === 'reload-suspense') {
      loadSuspensePayments(currentSuspensePage, currentSuspenseStatus);
    } else if (action === 'prev-suspense-page') {
      if (currentSuspensePage > 1) {
        loadSuspensePayments(currentSuspensePage - 1, currentSuspenseStatus);
      }
    } else if (action === 'next-suspense-page') {
      loadSuspensePayments(currentSuspensePage + 1, currentSuspenseStatus);
    } else if (action === 'open-assign-modal') {
      const orderCode = actionEl.getAttribute('data-order-code');
      openSuspenseAssignModal(orderCode);
    } else if (action === 'close-suspense-assign-modal') {
      closeSuspenseAssignModal();
    } else if (action === 'open-refund-modal') {
      const orderCode = actionEl.getAttribute('data-order-code');
      openSuspenseRefundModal(orderCode);
    } else if (action === 'close-suspense-refund-modal') {
      closeSuspenseRefundModal();
    } else if (action === 'open-decrypt-modal') {
      const orderCode = actionEl.getAttribute('data-order-code');
      showDecryptedSuspensePayer(orderCode);
    } else if (action === 'close-bank-decrypt-modal') {
      closeDecryptedModal();
    }
  });

  // Submit listeners cho các form modal
  document.addEventListener('submit', function (e) {
    if (e.target && e.target.id === 'suspense-assign-form') {
      e.preventDefault();
      submitSuspenseAssign();
    } else if (e.target && e.target.id === 'suspense-refund-form') {
      e.preventDefault();
      submitSuspenseRefund();
    }
  });

  // Public exports
  window.loadSuspensePayments = loadSuspensePayments;
  window.openSuspenseAssignModal = openSuspenseAssignModal;
  window.closeSuspenseAssignModal = closeSuspenseAssignModal;
  window.submitSuspenseAssign = submitSuspenseAssign;
  window.openSuspenseRefundModal = openSuspenseRefundModal;
  window.closeSuspenseRefundModal = closeSuspenseRefundModal;
  window.submitSuspenseRefund = submitSuspenseRefund;
  window.showDecryptedSuspensePayer = showDecryptedSuspensePayer;
  window.closeDecryptedPayerModal = closeDecryptedModal;
})();

