/* =====================================================
   ADMIN PAYMENTS & REFUNDS MODULE (payments.js)
   Phase: C2 - Round 4C-1
   ===================================================== */

document.addEventListener('DOMContentLoaded', () => {
    const listContainer = document.getElementById('list-container');
    if (listContainer) {
        listContainer.addEventListener('click', async (e) => {
            const reloadBtn = e.target.closest('[data-action="reload-payments"]');
            if (reloadBtn) {
                e.preventDefault();
                await loadPaymentsOverview();
                return;
            }

            const refundBtn = e.target.closest('[data-action="complete-refund"]');
            if (refundBtn) {
                e.preventDefault();
                const refundId = refundBtn.getAttribute('data-refund-id');
                if (refundId) {
                    await completeRefund(refundId, refundBtn);
                }
                return;
            }
        });
    }
});

async function loadPaymentsOverview() {
    const container = document.getElementById('list-container');
    if (!container) return;
    container.innerHTML = 'Đang tải dữ liệu tài chính...';
    try {
        const [overview, refunds] = await Promise.all([
            apiFetch('/admin/payments/overview'),
            apiFetch('/admin/refunds?status=REFUND_PENDING')
        ]);

        let refundRows = '<p class="refund-empty-text">Không có yêu cầu hoàn tiền nào đang chờ xử lý.</p>';
        if (refunds && refunds.length > 0) {
            refundRows = `
                <div class="refund-table-wrap">
                    <table class="refund-table">
                        <thead>
                            <tr class="refund-thead-row">
                                <th class="refund-th">Mã đơn</th>
                                <th class="refund-th">Số tiền</th>
                                <th class="refund-th">Lý do</th>
                                <th class="refund-th">Tài khoản nhận</th>
                                <th class="refund-th">Thao tác</th>
                            </tr>
                        </thead>
                        <tbody>
                            ${refunds.map(r => `
                                <tr class="refund-tr">
                                    <td class="refund-td"><strong>${escapeHtml(r.bookingCode || r.bookingId)}</strong></td>
                                    <td class="refund-td-amount">${/* safe */ formatTien(r.amount)}</td>
                                    <td class="refund-td">${escapeHtml(r.reason)}</td>
                                    <td class="refund-td">
                                        ${/* safe */ r.payerInfoMasked ? `<strong>${escapeHtml(r.payerInfoMasked)}</strong>` : '<span class="refund-waiting-text">Chờ khách nhập STK</span>'}
                                    </td>
                                    <td class="refund-td">
                                        <div class="refund-action-wrap">
                                            <input type="text" id="ref-code-${/* safe */ escapeHtml(r.refundId)}" placeholder="Mã GD ngân hàng..." class="refund-ref-input" />
                                            <button type="button" class="btn btn-primary btn-sm" data-action="complete-refund" data-refund-id="${/* safe */ escapeHtml(r.refundId)}">Xác nhận đã chuyển</button>
                                        </div>
                                    </td>
                                </tr>
                            `).join('')}
                        </tbody>
                    </table>
                </div>
            `;
        }

        container.innerHTML = `
            <div class="payments-kpi-grid">
                <div class="payments-kpi-card kpi-escrow">
                    <div class="payments-kpi-label">Tổng tiền giữ đảm bảo (Escrow)</div>
                    <div class="payments-kpi-value val-escrow">${/* safe */ formatTien(overview ? overview.escrowCurrentlyHeld : 0)}</div>
                </div>
                <div class="payments-kpi-card kpi-revenue">
                    <div class="payments-kpi-label">Doanh thu hoa hồng (10%)</div>
                    <div class="payments-kpi-value val-revenue">${/* safe */ formatTien(overview ? overview.platformRevenue : 0)}</div>
                </div>
                <div class="payments-kpi-card kpi-refund">
                    <div class="payments-kpi-label">Tiền chờ hoàn cho khách</div>
                    <div class="payments-kpi-value val-refund">${/* safe */ formatTien(overview ? overview.refundPendingAmount : 0)}</div>
                </div>
                <div class="payments-kpi-card kpi-refund-count">
                    <div class="payments-kpi-label">Số yêu cầu chờ hoàn</div>
                    <div class="payments-kpi-value val-refund-count">${/* safe */ overview && overview.refundPendingCount != null ? overview.refundPendingCount : 0} đơn</div>
                </div>
            </div>

            <div class="refund-queue-card">
                <div class="refund-queue-header">
                    <h3 class="refund-queue-title">Hàng đợi hoàn tiền (Refund Queue)</h3>
                    <button type="button" class="btn btn-outline btn-sm" data-action="reload-payments">Làm mới</button>
                </div>
                ${/* safe */ refundRows}
            </div>
        `;
    } catch (err) {
        showError(err.message || 'Lỗi khi tải dữ liệu thanh toán & hoàn tiền.');
        container.innerHTML = '';
    }
}

async function completeRefund(refundId, btn) {
    const refInput = document.getElementById(`ref-code-${refundId}`);
    const bankRef = refInput ? refInput.value.trim() : '';
    if (!bankRef) {
        showError('Vui lòng nhập mã giao dịch ngân hàng để đối soát.');
        return;
    }
    if (!confirm('Xác nhận bạn đã chuyển khoản hoàn tiền thành công cho khách hàng?')) return;

    const originalText = btn ? btn.textContent : '';
    if (btn) {
        btn.disabled = true;
        btn.textContent = 'Đang xử lý...';
    }

    try {
        await apiFetch(`/admin/refunds/${refundId}/complete`, {
            method: 'POST',
            body: JSON.stringify({ bankTransactionRef: bankRef })
        });
        showToast('Đã xác nhận hoàn tất hoàn tiền!', 3000, '✅');
        await loadPaymentsOverview();
    } catch (err) {
        showError(err.message || 'Lỗi khi xác nhận hoàn tiền.');
        if (btn) {
            btn.disabled = false;
            btn.textContent = originalText;
        }
    }
}

window.loadPaymentsOverview = loadPaymentsOverview;
window.completeRefund = completeRefund;

