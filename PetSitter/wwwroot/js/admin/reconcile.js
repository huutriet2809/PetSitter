/**
 * Admin Ledger & Bank Statement Reconciliation Module (reconcile.js)
 * Phase C2 - Round 4D-1
 * CSP Compliant: No inline styles, no inline event handlers. Event delegation via closest('[data-action]').
 */

(function () {
  'use strict';

  let currentBankReconcileResult = null;
  let currentBankFilter = 'ALL';

  async function loadReconciliation() {
    const container = document.getElementById('list-container');
    const subtabs = document.getElementById('subtabs-container');
    if (subtabs) subtabs.style.display = 'none';
    if (!container) return;

    container.innerHTML = '<div class="admin-loading-state">Đang tải lịch sử kiểm toán đối soát sổ cái...</div>';

    try {
      const runs = await apiFetch('/admin/reconciliation/history');
      const latestRun = (runs && runs.length > 0) ? runs[0] : null;

      let latestSummaryHtml = '<p class="text-muted reconcile-empty-hint">Chưa có lượt chạy đối soát nào được ghi nhận. Bấm nút bên dưới để thực hiện kiểm toán ngay.</p>';
      if (latestRun) {
        const isMatch = latestRun.isBalanced ?? (latestRun.status === 'SUCCESS' && (latestRun.isBalancedTotalDebitCredit !== false));
        const statusBoxClass = isMatch ? 'box-success' : 'box-warning';
        const statusTextClass = isMatch ? 'text-success' : 'text-danger';
        const statusText = isMatch ? '✅ SỔ CÁI HOÀN TOÀN CÂN BẰNG' : '❌ PHÁT HIỆN LỆCH SỔ CÁI HOẶC BẤT BÌNH THƯỜNG';
        const runDate = latestRun.runAt || latestRun.executedAt;
        const dateStr = runDate ? new Date(runDate).toLocaleString('vi-VN') : '—';

        const crit1 = latestRun.criteria1_BankBalanceMatch ?? latestRun.isBankBalanceMatched ?? true;
        const crit2 = latestRun.criteria2_InternalLedgerBalanced ?? latestRun.isBalancedTotalDebitCredit ?? true;
        const crit3 = latestRun.criteria3_PerBookingInvariantsMatch ?? true;
        const crit4 = latestRun.criteria4_EscrowValidityMatch ?? latestRun.isEscrowHeldMatchesHeldBookings ?? true;
        const crit5 = latestRun.criteria5_ReservedVsOpenMatch ?? latestRun.isReservedAmountMatchesOpenWithdrawals ?? true;
        const crit6 = latestRun.criteria6_SweptBalanceMatch ?? true;
        const crit7 = latestRun.criteria7_SuspenseBalanceMatch ?? true;
        const crit8 = latestRun.criteria8_ZeroSumNetMatch ?? true;

        const issues = latestRun.details || latestRun.issues || [];
        const issuesHtml = issues.length > 0 ? `
          <div class="reconcile-issues-alert">
            <strong>Danh sách vấn đề phát hiện:</strong>
            <ul class="reconcile-issues-list">
              ${issues.map(i => `<li>${escapeHtml(i)}</li>`).join('')}
            </ul>
          </div>
        ` : '';

        latestSummaryHtml = `
          <div class="reconcile-summary-box ${statusBoxClass}">
            <div class="reconcile-summary-status-row">
              <strong class="reconcile-status-title ${statusTextClass}">${statusText}</strong>
              <span class="text-sm text-muted">Lượt chạy gần nhất: ${dateStr}</span>
            </div>
            <div class="reconcile-summary-criteria">
              <div>• Cân bằng tiền mặt ngân hàng (Gateway Clearing): <strong>${crit1 ? '✅ Khớp' : '❌ Lệch'}</strong> ${latestRun.expectedBankBalance ? `(Expected: ${formatTien(latestRun.expectedBankBalance)})` : ''}</div>
              <div>• Đẳng thức tổng thể sổ cái (Ledger Identity): <strong>${crit2 ? '✅ Cân' : '❌ Lệch'}</strong></div>
              <div>• Bất biến theo đơn (Per-Booking Invariants): <strong>${crit3 ? '✅ Khớp 100%' : '❌ Vi phạm'}</strong></div>
              <div>• Tính hợp lệ Escrow đang giữ: <strong>${crit4 ? '✅ Đúng' : '❌ Lệch'}</strong></div>
              <div>• Tính toàn vẹn số dư ví Sitter (Reserved vs Open): <strong>${crit5 ? '✅ Khớp' : '❌ Lệch'}</strong></div>
              <div>• Đẳng thức doanh thu & rút sàn (Sweeps): <strong>${crit6 ? '✅ Khớp' : '❌ Lệch'}</strong></div>
              <div>• Giao dịch bất thường (Suspense Match): <strong>${crit7 ? '✅ Khớp' : '❌ Lệch'}</strong></div>
              <div>• Tính lũy đẳng & bù trừ sổ cái: <strong>${crit8 ? '✅ Khớp' : '❌ Lệch'}</strong></div>
            </div>
            ${issuesHtml}
          </div>
        `;
      }

      const runRows = (runs && runs.length > 0) ? runs.map(r => {
        const isBal = r.isBalanced ?? (r.status === 'SUCCESS' && r.isBalancedTotalDebitCredit !== false);
        const rDate = r.runAt || r.executedAt;
        const rDateStr = rDate ? new Date(rDate).toLocaleString('vi-VN') : '—';
        const issuesCount = (r.details || r.issues || []).length;
        const bookingsCount = r.reconciledBookingsCount ?? (r.completedBookingsWithHeldStatusCount ? `${r.completedBookingsWithHeldStatusCount} đơn bất thường` : 'Đã quét');
        const walletsCount = r.reconciledEarningsAccountsCount ?? (r.autoFixedAccountsCount !== undefined ? `${r.autoFixedAccountsCount} ví sửa` : 'Đã quét');

        return `
          <tr>
            <td class="reconcile-td"><code>#${escapeHtml((r.runId || '').substring(0, 8))}</code></td>
            <td class="reconcile-td text-sm text-muted">${rDateStr}</td>
            <td class="reconcile-td">${isBal ? '<span class="text-success font-semibold">✅ Khớp</span>' : '<span class="text-danger font-semibold">❌ Lệch</span>'}</td>
            <td class="reconcile-td text-sm">${escapeHtml(bookingsCount.toString())}</td>
            <td class="reconcile-td text-sm">${escapeHtml(walletsCount.toString())}</td>
            <td class="reconcile-td text-sm font-semibold ${issuesCount > 0 ? 'text-danger' : 'text-success'}">${issuesCount} cảnh báo</td>
          </tr>
        `;
      }).join('') : '<tr><td colspan="6" class="reconcile-td text-center text-muted">Chưa có lịch sử đối soát.</td></tr>';

      container.innerHTML = `
        <!-- Card 1: Đối soát 3 bên với Sao kê Ngân hàng (CSV) -->
        <div class="reconcile-card">
          <div class="reconcile-header-flex">
            <div>
              <h3 class="reconcile-title">🏦 Đối soát 3 bên với Sao kê Ngân hàng (CSV)</h3>
              <p class="reconcile-desc">
                Tải lên file sao kê CSV từ ngân hàng để tự động so khớp với tài khoản GATEWAY_CLEARING. Hệ thống chỉ đọc và phân loại kết quả.
              </p>
            </div>
          </div>

          <!-- Form upload -->
          <div class="reconcile-upload-box">
            <input type="file" id="bank-statement-file" accept=".csv" class="reconcile-file-input" />
            <button id="btn-upload-statement" class="btn btn-primary reconcile-upload-btn" data-action="upload-bank-statement">
              <span>📤 Tải lên & Đối soát 3 bên</span>
            </button>
          </div>

          <div id="bank-reconcile-result-area" class="mt-4 ${currentBankReconcileResult ? '' : 'hidden'}">
            <!-- Kết quả đối soát hiển thị ở đây -->
          </div>
        </div>

        <!-- Card 2: Kiểm toán & Đối soát Sổ cái kép -->
        <div class="reconcile-card">
          <div class="reconcile-header-flex">
            <h3 class="reconcile-title">Kiểm toán & Đối soát Sổ cái kép (8 Tiêu chí)</h3>
            <button class="btn btn-primary" data-action="run-reconciliation">🔍 Chạy kiểm toán đối soát ngay</button>
          </div>
          ${latestSummaryHtml}
          <h4 class="reconcile-subheading">Lịch sử các lượt kiểm toán</h4>
          <div class="reconcile-table-wrap">
            <table class="reconcile-table">
              <thead>
                <tr>
                  <th class="reconcile-th">Mã lượt</th>
                  <th class="reconcile-th">Thời gian</th>
                  <th class="reconcile-th">Kết quả</th>
                  <th class="reconcile-th">Số đơn quét</th>
                  <th class="reconcile-th">Số ví quét</th>
                  <th class="reconcile-th">Cảnh báo</th>
                </tr>
              </thead>
              <tbody>${runRows}</tbody>
            </table>
          </div>
        </div>
      `;

      if (currentBankReconcileResult) {
        renderBankReconciliationResult(currentBankReconcileResult);
      }
    } catch (err) {
      showError(err.message || 'Lỗi tải lịch sử đối soát.');
      container.innerHTML = '';
    }
  }

  async function runReconciliation() {
    if (!confirm('Khởi chạy kiểm toán đối soát toàn bộ sổ cái kép và số dư hệ thống?')) return;
    const btn = document.querySelector('[data-action="run-reconciliation"]');
    if (btn) {
      btn.disabled = true;
      btn.dataset.origText = btn.textContent;
      btn.textContent = '⏳ Đang kiểm toán...';
    }

    try {
      await apiFetch('/admin/reconciliation/run', { method: 'POST', body: JSON.stringify({}) });
      showToast('Đã hoàn tất lượt đối soát sổ cái!', 3000, '✅');
      loadReconciliation();
    } catch (err) {
      showError(err.message || 'Lỗi khi chạy đối soát.');
    } finally {
      if (btn) {
        btn.disabled = false;
        btn.textContent = btn.dataset.origText || '🔍 Chạy kiểm toán đối soát ngay';
      }
    }
  }

  async function uploadBankStatement() {
    const fileInput = document.getElementById('bank-statement-file');
    const btn = document.getElementById('btn-upload-statement');
    if (!fileInput || !fileInput.files || fileInput.files.length === 0) {
      showError('Vui lòng chọn một tệp sao kê CSV (.csv) từ máy tính.');
      return;
    }

    const file = fileInput.files[0];
    if (!file.name.toLowerCase().endsWith('.csv')) {
      showError('Chỉ chấp nhận tệp định dạng .csv.');
      return;
    }

    if (file.size > 5 * 1024 * 1024) {
      showError('Kích thước tệp vượt quá giới hạn cho phép (tối đa 5MB).');
      return;
    }

    if (btn) {
      btn.disabled = true;
      btn.innerHTML = '<span>⏳ Đang xử lý đối soát...</span>';
    }

    try {
      const formData = new FormData();
      formData.append('file', file);

      const token = localStorage.getItem('pawstay_token');
      const res = await fetch('/api/admin/reconciliation/upload-statement', {
        method: 'POST',
        headers: {
          'Authorization': `Bearer ${token}`
        },
        body: formData
      });

      if (!res.ok) {
        const errData = await res.json().catch(() => ({}));
        throw new Error(errData.message || 'Lỗi đối soát sao kê ngân hàng.');
      }

      const json = await res.json();
      currentBankReconcileResult = json.data;
      renderBankReconciliationResult(json.data);
      showToast(json.message || 'Đối soát thành công!', 4000, '🏦');
    } catch (err) {
      showError(err.message || 'Lỗi khi tải lên và đối soát sao kê.');
    } finally {
      if (btn) {
        btn.disabled = false;
        btn.innerHTML = '<span>📤 Tải lên & Đối soát 3 bên</span>';
      }
    }
  }

  function renderBankReconciliationResult(data) {
    const area = document.getElementById('bank-reconcile-result-area');
    if (!area || !data) return;

    area.style.display = 'block';

    const countAll = data.items?.length || 0;
    const countMatched = data.matchedCount || 0;
    const countMismatch = data.amountMismatchCount || 0;
    const countSuspicious = data.suspiciousCount || 0;
    const countMissingLedger = data.missingInLedgerCount || 0;
    const countMissingBank = data.missingInBankCount || 0;

    area.innerHTML = `
      <div class="reconcile-result-box">
        <!-- 5 KPI Cards -->
        <div class="reconcile-kpi-grid">
          <div class="reconcile-kpi-box kpi-matched">
            <div class="reconcile-kpi-title">✅ Khớp hoàn toàn</div>
            <div class="reconcile-kpi-val">${countMatched}</div>
          </div>
          <div class="reconcile-kpi-box kpi-mismatch">
            <div class="reconcile-kpi-title">⚠️ Lệch số tiền</div>
            <div class="reconcile-kpi-val">${countMismatch}</div>
          </div>
          <div class="reconcile-kpi-box kpi-suspicious">
            <div class="reconcile-kpi-title">🟣 Nghi ngờ lệch ngày</div>
            <div class="reconcile-kpi-val">${countSuspicious}</div>
          </div>
          <div class="reconcile-kpi-box kpi-missing-ledger">
            <div class="reconcile-kpi-title">❌ Thiếu trên Sổ cái</div>
            <div class="reconcile-kpi-val">${countMissingLedger}</div>
          </div>
          <div class="reconcile-kpi-box kpi-missing-bank">
            <div class="reconcile-kpi-title">🟡 Thiếu tại Ngân hàng</div>
            <div class="reconcile-kpi-val">${countMissingBank}</div>
          </div>
        </div>

        <!-- Filter Toolbar -->
        <div class="reconcile-filter-bar">
          <div class="reconcile-filter-group">
            <button class="btn btn-sm ${currentBankFilter === 'ALL' ? 'btn-primary' : 'btn-outline'}" data-action="filter-bank-items" data-status="ALL">Tất cả (${countAll})</button>
            <button class="btn btn-sm ${currentBankFilter === 'MATCHED' ? 'btn-primary' : 'btn-outline'}" data-action="filter-bank-items" data-status="MATCHED">Khớp (${countMatched})</button>
            <button class="btn btn-sm ${currentBankFilter === 'AMOUNT_MISMATCH' ? 'btn-primary' : 'btn-outline'}" data-action="filter-bank-items" data-status="AMOUNT_MISMATCH">Lệch tiền (${countMismatch})</button>
            <button class="btn btn-sm ${currentBankFilter === 'SUSPICIOUS' ? 'btn-primary' : 'btn-outline'}" data-action="filter-bank-items" data-status="SUSPICIOUS">Nghi ngờ (${countSuspicious})</button>
            <button class="btn btn-sm ${currentBankFilter === 'MISSING_IN_LEDGER' ? 'btn-primary' : 'btn-outline'}" data-action="filter-bank-items" data-status="MISSING_IN_LEDGER">Thiếu sổ cái (${countMissingLedger})</button>
            <button class="btn btn-sm ${currentBankFilter === 'MISSING_IN_BANK' ? 'btn-primary' : 'btn-outline'}" data-action="filter-bank-items" data-status="MISSING_IN_BANK">Thiếu ngân hàng (${countMissingBank})</button>
          </div>
          <div class="reconcile-meta-info">
            Tệp: <strong>${escapeHtml(data.fileName || 'statement.csv')}</strong> • Tổng tiền sao kê: <strong>${formatTien(data.totalBankAmount || 0)}</strong>
          </div>
        </div>

        <!-- Table Items -->
        <div class="reconcile-table-wrap">
          <table class="reconcile-table">
            <thead>
              <tr>
                <th class="reconcile-th">Dòng</th>
                <th class="reconcile-th">Ngày GD</th>
                <th class="reconcile-th">Mã Đơn / Ref</th>
                <th class="reconcile-th">Mô tả</th>
                <th class="reconcile-th text-right">Tiền Sao kê</th>
                <th class="reconcile-th text-right">Tiền Sổ cái</th>
                <th class="reconcile-th text-center">Phân loại</th>
                <th class="reconcile-th">Ghi chú đối soát</th>
              </tr>
            </thead>
            <tbody id="bank-reconcile-items-tbody">
              ${renderBankTableRows(data.items, currentBankFilter)}
            </tbody>
          </table>
        </div>
      </div>
    `;
  }

  function filterBankItems(status) {
    currentBankFilter = status;
    if (currentBankReconcileResult) {
      renderBankReconciliationResult(currentBankReconcileResult);
    }
  }

  function renderBankTableRows(items, filterStatus) {
    if (!items || items.length === 0) {
      return '<tr><td colspan="8" class="reconcile-td text-center text-muted">Không có dữ liệu giao dịch nào.</td></tr>';
    }

    const filtered = filterStatus === 'ALL' ? items : items.filter(it => it.matchStatus === filterStatus);

    if (filtered.length === 0) {
      return '<tr><td colspan="8" class="reconcile-td text-center text-muted">Không có giao dịch nào thuộc nhóm này.</td></tr>';
    }

    return filtered.map(it => {
      let badgeHtml = '';
      if (it.matchStatus === 'MATCHED') {
        badgeHtml = '<span class="reconcile-badge badge-matched">KHỚP</span>';
      } else if (it.matchStatus === 'AMOUNT_MISMATCH') {
        badgeHtml = '<span class="reconcile-badge badge-mismatch">LỆCH TIỀN</span>';
      } else if (it.matchStatus === 'SUSPICIOUS') {
        badgeHtml = '<span class="reconcile-badge badge-suspicious">NGHI NGỜ</span>';
      } else if (it.matchStatus === 'MISSING_IN_LEDGER') {
        badgeHtml = '<span class="reconcile-badge badge-missing-ledger">THIẾU SỔ CÁI</span>';
      } else if (it.matchStatus === 'MISSING_IN_BANK') {
        badgeHtml = '<span class="reconcile-badge badge-missing-bank">THIẾU NH</span>';
      }

      const dateStr = it.transactionDate ? new Date(it.transactionDate).toLocaleDateString('vi-VN') : '—';
      const orderCodeStr = it.orderCode ? `#${it.orderCode}` : (it.referenceCode || '—');

      return `
        <tr>
          <td class="reconcile-td text-muted">${it.lineNumber > 0 ? it.lineNumber : '—'}</td>
          <td class="reconcile-td">${dateStr}</td>
          <td class="reconcile-td font-semibold">${escapeHtml(orderCodeStr.toString())}</td>
          <td class="reconcile-td-desc" title="${escapeHtml(it.description || '')}">${escapeHtml(it.description || '')}</td>
          <td class="reconcile-td text-right">${it.statementAmount > 0 ? formatTien(it.statementAmount) : '—'}</td>
          <td class="reconcile-td text-right">${it.ledgerAmount ? formatTien(it.ledgerAmount) : '—'}</td>
          <td class="reconcile-td text-center">${badgeHtml}</td>
          <td class="reconcile-td-note">${escapeHtml(it.discrepancyNote || '')}</td>
        </tr>
      `;
    }).join('');
  }

  // Event Delegation cho #list-container
  document.addEventListener('click', function (e) {
    const actionEl = e.target.closest('[data-action]');
    if (!actionEl) return;

    const action = actionEl.getAttribute('data-action');
    if (action === 'run-reconciliation') {
      runReconciliation();
    } else if (action === 'upload-bank-statement') {
      uploadBankStatement();
    } else if (action === 'filter-bank-items') {
      const status = actionEl.getAttribute('data-status') || 'ALL';
      filterBankItems(status);
    }
  });

  // Public exports
  window.loadReconciliation = loadReconciliation;
  window.runReconciliation = runReconciliation;
  window.uploadBankStatement = uploadBankStatement;
  window.filterBankItems = filterBankItems;
})();

