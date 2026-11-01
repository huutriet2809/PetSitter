/**
 * Admin Revenue Management Module (revenue.js)
 * Phase C2 - Round 4C-3
 * CSP Compliant: No inline styles, no inline event handlers. Event delegation via closest('[data-action]').
 */

(function () {
  'use strict';

  let currentTransferable = 0;
  let currentRevenue = 0;
  let currentBuffer = 0;

  async function loadRevenue() {
    const container = document.getElementById('list-container');
    const subtabs = document.getElementById('subtabs-container');
    if (subtabs) subtabs.style.display = 'none';
    if (!container) return;

    container.innerHTML = '<div class="admin-loading-state">Đang tải dữ liệu tổng quan doanh thu...</div>';

    try {
      const rev = await apiFetch('/admin/revenue/overview');
      if (!rev) {
        container.innerHTML = '<div class="admin-empty-state">Không có dữ liệu doanh thu.</div>';
        return;
      }

      currentTransferable = rev.transferableAmount || 0;
      currentRevenue = rev.platformRevenue || 0;
      currentBuffer = rev.revenueReserveBuffer || 0;

      const quotaBanner = rev.payosWarning ? `
        <div class="payos-warning-banner">
          <div>
            <div class="payos-warning-title">⚠️ Cảnh báo hạn mức PayOS (FREE-100)</div>
            <div class="payos-warning-desc">Đã sử dụng <strong>${rev.payosLinksCount ?? 0}</strong> / <strong>${rev.payosMaxQuota ?? 100}</strong> link thanh toán. Vui lòng liên hệ PayOS nâng cấp gói trả phí.</div>
          </div>
        </div>
      ` : '';

      const sweepsRows = (rev.recentSweeps && rev.recentSweeps.length > 0) ? rev.recentSweeps.map(s => `
        <tr class="sweeps-tr">
          <td class="sweeps-td"><code>#${escapeHtml((s.sweepId || '').substring(0, 8))}</code></td>
          <td class="sweeps-td text-muted text-sm">${new Date(s.createdAt).toLocaleString('vi-VN')}</td>
          <td class="sweeps-td-amount">${formatTien(s.amount)}</td>
          <td class="sweeps-td"><code>${escapeHtml(s.bankTransactionRef || '')}</code></td>
          <td class="sweeps-td text-sm">${escapeHtml(s.note || '—')}</td>
          <td class="sweeps-td text-sm text-muted">${escapeHtml(s.adminUserId || '')}</td>
        </tr>
      `).join('') : '<tr><td colspan="6" class="sweeps-empty">Chưa có lượt rút doanh thu nào.</td></tr>';

      const isTransferableDisabled = currentTransferable < 10000;

      container.innerHTML = `
        ${quotaBanner}
        <!-- Metric Cards -->
        <div class="revenue-kpi-grid">
          <div class="revenue-kpi-card kpi-revenue">
            <div class="revenue-kpi-label">Doanh thu hoa hồng (10%)</div>
            <div class="revenue-kpi-value val-revenue">${formatTien(rev.platformRevenue)}</div>
            <div class="revenue-kpi-desc">Tổng doanh thu hoa hồng tích lũy</div>
          </div>
          <div class="revenue-kpi-card kpi-transferable">
            <div class="revenue-kpi-label">Khả dụng rút về tài khoản sàn</div>
            <div class="revenue-kpi-value val-transferable">${formatTien(rev.transferableAmount)}</div>
            <div class="revenue-kpi-desc">Đã trừ khoản đệm an toàn & phần đã rút</div>
          </div>
          <div class="revenue-kpi-card kpi-buffer">
            <div class="revenue-kpi-label">Khoản đệm an toàn (Buffer)</div>
            <div class="revenue-kpi-value val-buffer">${formatTien(rev.revenueReserveBuffer)}</div>
            <div class="revenue-kpi-desc">Đệm 48h đề phòng hoàn tiền/sai số phí</div>
          </div>
          <div class="revenue-kpi-card kpi-swept">
            <div class="revenue-kpi-label">Đã rút lũy kế (Swept Total)</div>
            <div class="revenue-kpi-value val-swept">${formatTien(rev.sweptTotal)}</div>
            <div class="revenue-kpi-desc">Tổng số tiền nền tảng đã rút</div>
          </div>
        </div>

        <!-- Action Toolbar -->
        <div class="revenue-toolbar">
          <button class="btn btn-primary" data-action="open-sweep-modal" ${isTransferableDisabled ? 'disabled' : ''}>
            🏦 Rút doanh thu nền tảng (Sweep)
          </button>
          <button class="btn btn-outline" data-action="export-revenue-csv">
            📊 Tải báo cáo doanh thu tháng (CSV)
          </button>
        </div>

        <!-- Sweeps History Table -->
        <div class="sweeps-card">
          <div class="sweeps-header">
            <h3 class="sweeps-title">Lịch sử rút doanh thu nền tảng (Sweeps)</h3>
            <span class="sweeps-count">${rev.recentSweeps?.length || 0} lượt</span>
          </div>
          <div class="sweeps-table-wrap">
            <table class="sweeps-table">
              <thead>
                <tr class="sweeps-thead-row">
                  <th class="sweeps-th">Mã lượt rút</th>
                  <th class="sweeps-th">Thời gian</th>
                  <th class="sweeps-th">Số tiền</th>
                  <th class="sweeps-th">Mã GD ngân hàng</th>
                  <th class="sweeps-th">Ghi chú</th>
                  <th class="sweeps-th">Admin thực hiện</th>
                </tr>
              </thead>
              <tbody>${sweepsRows}</tbody>
            </table>
          </div>
        </div>
      `;
    } catch (err) {
      showError(err.message || 'Lỗi tải tổng quan doanh thu.');
      container.innerHTML = '';
    }
  }

  function openSweepModal(transferable, revenue, buffer) {
    const t = (transferable !== undefined && transferable !== null) ? transferable : currentTransferable;
    const r = (revenue !== undefined && revenue !== null) ? revenue : currentRevenue;
    const b = (buffer !== undefined && buffer !== null) ? buffer : currentBuffer;

    const elRev = document.getElementById('sweep-platform-revenue');
    const elBuf = document.getElementById('sweep-buffer-amount');
    const elMax = document.getElementById('sweep-max-transferable');
    const elInput = document.getElementById('sweep-input-amount');
    const modal = document.getElementById('sweep-modal');

    if (elRev) elRev.textContent = formatTien(r);
    if (elBuf) elBuf.textContent = formatTien(b);
    if (elMax) elMax.textContent = formatTien(t);
    if (elInput) {
      elInput.value = t > 0 ? t : '';
      elInput.max = t;
    }
    if (modal) {
      modal.classList.add('is-active');
      modal.style.display = 'flex';
    }
  }

  function closeSweepModal() {
    const modal = document.getElementById('sweep-modal');
    if (modal) {
      modal.classList.remove('is-active');
      modal.style.display = 'none';
    }
    const form = document.getElementById('sweep-form');
    if (form) form.reset();
  }

  async function submitSweep() {
    const amountInput = document.getElementById('sweep-input-amount');
    const refInput = document.getElementById('sweep-input-ref');
    const noteInput = document.getElementById('sweep-input-note');
    const submitBtn = document.querySelector('#sweep-form button[type="submit"]');

    const amount = parseInt(amountInput ? amountInput.value : '0', 10);
    const bankRef = refInput ? refInput.value.trim() : '';
    const note = noteInput ? noteInput.value.trim() : '';

    if (!amount || amount < 10000) {
      showError('Số tiền rút tối thiểu là 10.000 ₫.');
      return;
    }
    if (!bankRef || !note) {
      showError('Vui lòng nhập đầy đủ mã giao dịch ngân hàng và ghi chú.');
      return;
    }

    // Chống bấm đúp
    if (submitBtn) {
      submitBtn.disabled = true;
      submitBtn.dataset.originalText = submitBtn.textContent;
      submitBtn.textContent = 'Đang xử lý...';
    }

    try {
      await apiFetch('/admin/revenue/sweeps', {
        method: 'POST',
        body: JSON.stringify({ amount, bankTransactionRef: bankRef, note })
      });
      showToast('Đã rút doanh thu nền tảng thành công!', 3000, '✅');
      closeSweepModal();
      loadRevenue();
    } catch (err) {
      showError(err.message || 'Lỗi khi rút doanh thu.');
    } finally {
      if (submitBtn) {
        submitBtn.disabled = false;
        submitBtn.textContent = submitBtn.dataset.originalText || 'Xác nhận rút doanh thu';
      }
    }
  }

  function exportMonthlyCsv() {
    const now = new Date();
    const defaultVal = `${now.getMonth() + 1}/${now.getFullYear()}`;
    const inputStr = prompt('Nhập tháng và năm cần xuất báo cáo (VD: 10/2026):', defaultVal);
    if (!inputStr) return;

    const trimmed = inputStr.trim();
    // Chống CSV injection & input validation: chỉ cho phép định dạng MM/YYYY
    if (!/^(0?[1-9]|1[0-2])\/\d{4}$/.test(trimmed)) {
      showError('Định dạng không hợp lệ. Vui lòng nhập định dạng MM/YYYY (VD: 10/2026).');
      return;
    }

    const parts = trimmed.split('/');
    const month = parseInt(parts[0], 10);
    const year = parseInt(parts[1], 10);
    if (isNaN(month) || isNaN(year) || month < 1 || month > 12 || year < 2020 || year > 2100) {
      showError('Tháng hoặc năm không hợp lệ.');
      return;
    }

    const token = localStorage.getItem('pawstay_token');
    const url = `/api/admin/revenue/monthly-report/export-csv?year=${year}&month=${month}`;

    fetch(url, {
      headers: { 'Authorization': `Bearer ${token}` }
    })
    .then(res => {
      if (!res.ok) throw new Error('Không thể tải file CSV.');
      return res.blob();
    })
    .then(blob => {
      const downloadUrl = window.URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = downloadUrl;
      a.download = `BaoCao_DoanhThu_${year}_${month.toString().padStart(2, '0')}.csv`;
      document.body.appendChild(a);
      a.click();
      a.remove();
      window.URL.revokeObjectURL(downloadUrl);
      showToast('Đã tải xuống báo cáo doanh thu CSV!', 3000, '📊');
    })
    .catch(err => showError(err.message));
  }

  // Event Delegation cho #list-container và #sweep-modal
  document.addEventListener('click', function (e) {
    const actionEl = e.target.closest('[data-action]');
    if (!actionEl) return;

    const action = actionEl.getAttribute('data-action');
    if (action === 'open-sweep-modal') {
      openSweepModal();
    } else if (action === 'export-revenue-csv') {
      exportMonthlyCsv();
    } else if (action === 'close-sweep-modal') {
      closeSweepModal();
    }
  });

  // Submit listener cho form rút doanh thu
  document.addEventListener('submit', function (e) {
    if (e.target && e.target.id === 'sweep-form') {
      e.preventDefault();
      submitSweep();
    }
  });

  // Public exports
  window.loadRevenue = loadRevenue;
  window.openSweepModal = openSweepModal;
  window.closeSweepModal = closeSweepModal;
  window.submitSweep = submitSweep;
  window.exportMonthlyCsv = exportMonthlyCsv;
})();

