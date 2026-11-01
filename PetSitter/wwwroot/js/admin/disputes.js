/**
 * Admin Disputes Management Module (disputes.js)
 * Phase C2 - Round 4D-3
 * CSP Compliant: No inline styles, no inline event handlers. Event delegation via closest('[data-action]').
 */

(function () {
  'use strict';

  async function loadDisputes() {
    const container = document.getElementById('list-container');
    const subtabs = document.getElementById('subtabs-container');
    if (subtabs) subtabs.style.display = 'none';
    if (!container) return;

    container.innerHTML = '<div class="admin-loading-state">Đang tải danh sách khiếu nại...</div>';

    try {
      const disputes = await apiFetch('/admin/disputed-bookings');
      if (!disputes || disputes.length === 0) {
        container.innerHTML = '<div class="admin-empty-state">Không có đơn đặt lịch nào đang khiếu nại.</div>';
        return;
      }

      container.innerHTML = disputes.map(d => {
        const bps = (typeof d.commissionBps === 'number' && d.commissionBps > 0) ? d.commissionBps : 1000;
        const totalAmt = d.tongTien || d.totalAmount || 0;

        return `
          <div class="dispute-card" id="dispute-${d.bookingId}">
            <div class="dispute-header-row">
              <div>
                <h3 class="dispute-card-title">Đơn #${escapeHtml(d.maDatLich || '')} - Sitter: ${escapeHtml(d.sitterName || '')}</h3>
                <span class="dispute-status-badge">Đang tranh chấp</span>
              </div>
              <div>
                <strong class="dispute-total-amount">Tổng tiền: ${formatTien(totalAmt)}</strong>
              </div>
            </div>
            <div class="dispute-reason-box">
              <strong class="dispute-reason-title">⚠️ Lý do khiếu nại từ chủ nuôi:</strong>
              <p class="dispute-reason-text">${escapeHtml(d.disputeReason || 'Không có lý do chi tiết')}</p>
            </div>
            <p class="dispute-meta-p"><strong>Thời gian gửi:</strong> ${new Date(d.ngayBatDau).toLocaleDateString('vi-VN')} - ${new Date(d.ngayKetThuc).toLocaleDateString('vi-VN')} (${d.soNgay || 0} ngày)</p>
            <p class="dispute-meta-p"><strong>Thú cưng:</strong> ${escapeHtml(d.pet?.tenPet || '')} (${escapeHtml(d.pet?.loaiPet || '')})</p>
            
            <div class="dispute-action-box">
              <label class="dispute-label">Ghi chú xử lý của Admin (10 đến 500 ký tự) <span class="dispute-req-star">*</span></label>
              <textarea id="note-${d.bookingId}" placeholder="Nhập kết luận và căn cứ giải quyết tranh chấp..." class="dispute-textarea"></textarea>
              <div class="dispute-violation-row">
                <input type="checkbox" id="violation-${d.bookingId}" />
                <label for="violation-${d.bookingId}" class="dispute-violation-label">⚠️ Ghi nhận vi phạm đối với Sitter (tăng số lần vi phạm)</label>
              </div>
              <div class="dispute-btn-group">
                <button class="btn btn-primary btn-sm" data-action="resolve-dispute" data-resolution="COMPLETED" data-booking-id="${d.bookingId}">Duyệt hoàn tất (COMPLETED)</button>
                <button class="btn btn-outline btn-sm btn-split-outline" data-action="open-split-modal" data-booking-id="${d.bookingId}" data-total-amount="${totalAmt}" data-commission-bps="${bps}">Hòa giải (SPLIT)</button>
                <button class="btn btn-outline btn-sm btn-cancel-outline" data-action="resolve-dispute" data-resolution="CANCELLED" data-booking-id="${d.bookingId}">Hủy đơn (CANCELLED)</button>
              </div>
            </div>
          </div>
        `;
      }).join('');

      const urlParams = new URLSearchParams(window.location.search);
      const targetBookingId = urlParams.get('bookingId');
      if (targetBookingId) {
        const targetCard = document.getElementById(`dispute-${targetBookingId}`);
        if (targetCard) {
          targetCard.scrollIntoView({ behavior: 'smooth', block: 'center' });
          targetCard.classList.add('booking-highlight');
        }
      }
    } catch (err) {
      showError(err.message || 'Lỗi tải danh sách khiếu nại.');
      container.innerHTML = '';
    }
  }

  async function resolveDispute(bookingId, resolution, actionBtn) {
    const noteInput = document.getElementById(`note-${bookingId}`);
    const note = noteInput ? noteInput.value.trim() : '';
    const isViolation = document.getElementById(`violation-${bookingId}`)?.checked || false;

    if (note.length < 10 || note.length > 500) {
      showError('Ghi chú xử lý bắt buộc từ 10 đến 500 ký tự.');
      return;
    }

    if (!confirm(`Xác nhận giải quyết tranh chấp với kết quả: ${resolution}?`)) return;

    if (actionBtn) {
      actionBtn.disabled = true;
      actionBtn.dataset.origText = actionBtn.textContent;
      actionBtn.textContent = 'Đang xử lý...';
    }

    try {
      await apiFetch(`/admin/bookings/${bookingId}/resolve`, {
        method: 'PATCH',
        body: JSON.stringify({
          resolution,
          note,
          recordSitterViolation: isViolation,
          sitterViolationReason: isViolation ? note : null
        })
      });
      showToast(`Đã giải quyết tranh chấp thành công (${resolution})!`, 3000, '✅');
      const card = document.getElementById(`dispute-${bookingId}`);
      if (card) card.remove();
      if (!document.querySelector('#list-container .dispute-card')) {
        loadDisputes();
      }
    } catch (err) {
      showError(err.message || 'Lỗi khi giải quyết tranh chấp.');
    } finally {
      if (actionBtn) {
        actionBtn.disabled = false;
        actionBtn.textContent = actionBtn.dataset.origText || resolution;
      }
    }
  }

  function openSplitModal(bookingId, totalAmount, commissionBps) {
    const elBookingId = document.getElementById('split-booking-id');
    const elTotalAmount = document.getElementById('split-total-amount');
    const elCommissionBps = document.getElementById('split-commission-bps');
    const elTotalDisplay = document.getElementById('split-total-display');
    const elRefundInput = document.getElementById('split-refund-amount');
    const elNote = document.getElementById('split-note');
    const elViolCheck = document.getElementById('split-record-violation');
    const elViolReason = document.getElementById('split-violation-reason');
    const modal = document.getElementById('split-modal');

    const total = parseInt(totalAmount, 10) || 0;
    const bps = (typeof commissionBps === 'number' && commissionBps > 0) ? commissionBps : (parseInt(commissionBps, 10) || 1000);

    if (elBookingId) elBookingId.value = bookingId || '';
    if (elTotalAmount) elTotalAmount.value = total;
    if (elCommissionBps) elCommissionBps.value = bps;
    if (elTotalDisplay) elTotalDisplay.textContent = formatTien(total);

    const defaultRefund = Math.floor(total / 2);
    if (elRefundInput) {
      elRefundInput.value = defaultRefund;
      elRefundInput.max = total > 0 ? (total - 1) : 0;
    }
    if (elNote) elNote.value = '';
    if (elViolCheck) elViolCheck.checked = false;
    if (elViolReason) elViolReason.value = '';

    updateSplitPreview();

    if (modal) {
      modal.classList.add('is-active');
      modal.classList.add('open');
      modal.style.display = 'flex';
    }
  }

  function closeSplitModal() {
    const modal = document.getElementById('split-modal');
    if (modal) {
      modal.classList.remove('is-active');
      modal.classList.remove('open');
      modal.style.display = 'none';
    }
    const form = document.getElementById('split-form');
    if (form) form.reset();
  }

  function updateSplitPreview() {
    const elTotal = document.getElementById('split-total-amount');
    const elRefund = document.getElementById('split-refund-amount');
    const elBps = document.getElementById('split-commission-bps');

    const total = parseInt(elTotal?.value || '0', 10) || 0;
    const refund = parseInt(elRefund?.value || '0', 10) || 0;
    const bps = parseInt(elBps?.value || '1000', 10) || 1000;
    const remainderForSitter = Math.max(0, total - refund);

    // Tính phí sàn động theo CommissionBps
    const commission = Math.floor((remainderForSitter * bps) / 10000);
    const netSitter = Math.max(0, remainderForSitter - commission);
    const pctDisplay = (bps / 100).toFixed(bps % 100 === 0 ? 0 : 1);

    const feeLabel = document.getElementById('split-fee-label');
    if (feeLabel) feeLabel.textContent = `Phí dịch vụ nền tảng (${pctDisplay}% phần Sitter):`;
    const sitterLabel = document.getElementById('split-sitter-label');
    if (sitterLabel) sitterLabel.textContent = `Người chăm sóc thực nhận (${(100 - (bps / 100)).toFixed(bps % 100 === 0 ? 0 : 1)}%):`;

    const ownerPreview = document.getElementById('split-owner-preview');
    if (ownerPreview) ownerPreview.textContent = formatTien(refund);
    const feePreview = document.getElementById('split-fee-preview');
    if (feePreview) feePreview.textContent = formatTien(commission);
    const sitterPreview = document.getElementById('split-sitter-preview');
    if (sitterPreview) sitterPreview.textContent = formatTien(netSitter);

    const hintEl = document.getElementById('split-amount-hint');
    if (hintEl) {
      if (refund <= 0 || refund >= total) {
        hintEl.classList.add('hint-error');
        hintEl.textContent = `⚠️ Số tiền hoàn phải lớn hơn 0 ₫ và nhỏ hơn ${formatTien(total)}.`;
      } else {
        hintEl.classList.remove('hint-error');
        hintEl.textContent = `Chủ nuôi: ${formatTien(refund)} • Sàn (${pctDisplay}%): ${formatTien(commission)} • Sitter thực nhận: ${formatTien(netSitter)} (Tổng: ${formatTien(refund + commission + netSitter)})`;
      }
    }
  }

  async function submitSplitForm(form) {
    const bookingId = document.getElementById('split-booking-id')?.value;
    const totalAmount = parseInt(document.getElementById('split-total-amount')?.value || '0', 10) || 0;
    const refundAmount = parseInt(document.getElementById('split-refund-amount')?.value || '0', 10);
    const note = document.getElementById('split-note')?.value.trim();
    const isViolation = document.getElementById('split-record-violation')?.checked || false;
    const violationReason = document.getElementById('split-violation-reason')?.value.trim() || note;
    const submitBtn = form.querySelector('button[type="submit"]');

    if (isNaN(refundAmount) || refundAmount <= 0 || refundAmount >= totalAmount) {
      showError(`Số tiền hoàn cho chủ nuôi phải lớn hơn 0 và nhỏ hơn tổng giá trị đơn (${formatTien(totalAmount)}).`);
      return;
    }

    if (note.length < 10 || note.length > 500) {
      showError('Ghi chú xử lý bắt buộc từ 10 đến 500 ký tự.');
      return;
    }

    if (!confirm(`Xác nhận hòa giải tranh chấp với phương án SPLIT:\n• Hoàn Chủ nuôi: ${formatTien(refundAmount)}\n• Kết luận: ${note}`)) {
      return;
    }

    if (submitBtn) {
      submitBtn.disabled = true;
      submitBtn.dataset.origText = submitBtn.textContent;
      submitBtn.textContent = 'Đang xử lý...';
    }

    try {
      await apiFetch(`/admin/bookings/${bookingId}/resolve`, {
        method: 'PATCH',
        body: JSON.stringify({
          resolution: 'SPLIT',
          refundToOwnerAmount: refundAmount,
          note: note,
          recordSitterViolation: isViolation,
          sitterViolationReason: isViolation ? violationReason : null
        })
      });
      showToast('Đã giải quyết tranh chấp với phương án SPLIT thành công!', 3000, '✅');
      closeSplitModal();
      const card = document.getElementById(`dispute-${bookingId}`);
      if (card) card.remove();
      if (!document.querySelector('#list-container .dispute-card')) {
        loadDisputes();
      }
    } catch (err) {
      showError(err.message || 'Lỗi khi hòa giải tranh chấp.');
    } finally {
      if (submitBtn) {
        submitBtn.disabled = false;
        submitBtn.textContent = submitBtn.dataset.origText || 'Xác nhận hòa giải (SPLIT)';
      }
    }
  }

  // Event Delegation
  document.addEventListener('click', function (e) {
    const actionEl = e.target.closest('[data-action]');
    if (!actionEl) return;

    const action = actionEl.getAttribute('data-action');
    if (action === 'resolve-dispute') {
      const bookingId = actionEl.getAttribute('data-booking-id');
      const resolution = actionEl.getAttribute('data-resolution');
      resolveDispute(bookingId, resolution, actionEl);
    } else if (action === 'open-split-modal') {
      const bookingId = actionEl.getAttribute('data-booking-id');
      const total = actionEl.getAttribute('data-total-amount');
      const bps = actionEl.getAttribute('data-commission-bps');
      openSplitModal(bookingId, total, bps);
    } else if (action === 'close-split-modal') {
      closeSplitModal();
    }
  });

  // Input listener cập nhật preview thời gian thực
  document.addEventListener('input', function (e) {
    if (e.target && e.target.id === 'split-refund-amount') {
      updateSplitPreview();
    }
  });

  // Submit listener
  document.addEventListener('submit', function (e) {
    if (e.target && e.target.id === 'split-form') {
      e.preventDefault();
      submitSplitForm(e.target);
    }
  });

  // Public exports
  window.loadDisputes = loadDisputes;
  window.resolveDispute = resolveDispute;
  window.openSplitModal = openSplitModal;
  window.closeSplitModal = closeSplitModal;
  window.updateSplitPreview = updateSplitPreview;
})();

