/* =====================================================
   ADMIN WITHDRAWALS MODULE (withdrawals.js)
   Phase: C2 - Round 4C-2
   ===================================================== */

let currentWithdrawalStatus = 'REQUESTED';

document.addEventListener('DOMContentLoaded', () => {
    const subtabsContainer = document.getElementById('subtabs-container');
    if (subtabsContainer) {
        subtabsContainer.addEventListener('click', (e) => {
            const btn = e.target.closest('[data-action="switch-withdrawal-status"]');
            if (btn) {
                e.preventDefault();
                const status = btn.getAttribute('data-status') || '';
                loadWithdrawals(status);
            }
        });
    }

    const listContainer = document.getElementById('list-container');
    if (listContainer) {
        listContainer.addEventListener('click', async (e) => {
            const reloadBtn = e.target.closest('[data-action="reload-withdrawals"]');
            if (reloadBtn) {
                e.preventDefault();
                await loadWithdrawals(currentWithdrawalStatus);
                return;
            }

            const approveBtn = e.target.closest('[data-action="approve-withdrawal"]');
            if (approveBtn) {
                e.preventDefault();
                const id = approveBtn.getAttribute('data-withdrawal-id');
                if (id) await approveWithdrawal(id, approveBtn);
                return;
            }

            const rejectBtn = e.target.closest('[data-action="reject-withdrawal"]');
            if (rejectBtn) {
                e.preventDefault();
                const id = rejectBtn.getAttribute('data-withdrawal-id');
                if (id) await rejectWithdrawal(id, rejectBtn);
                return;
            }

            const claimBtn = e.target.closest('[data-action="claim-withdrawal"]');
            if (claimBtn) {
                e.preventDefault();
                const id = claimBtn.getAttribute('data-withdrawal-id');
                if (id) await claimProcessing(id, claimBtn);
                return;
            }

            const unclaimBtn = e.target.closest('[data-action="unclaim-withdrawal"]');
            if (unclaimBtn) {
                e.preventDefault();
                const id = unclaimBtn.getAttribute('data-withdrawal-id');
                if (id) await unclaimWithdrawal(id, unclaimBtn);
                return;
            }

            const paidBtn = e.target.closest('[data-action="confirm-paid-withdrawal"]');
            if (paidBtn) {
                e.preventDefault();
                const id = paidBtn.getAttribute('data-withdrawal-id');
                if (id) await confirmPaidWithdrawal(id, paidBtn);
                return;
            }

            const decryptBtn = e.target.closest('[data-action="show-decrypted-bank"]');
            if (decryptBtn) {
                e.preventDefault();
                const id = decryptBtn.getAttribute('data-withdrawal-id');
                if (id) await showDecryptedBank(id, decryptBtn);
                return;
            }
        });
    }

    const modal = document.getElementById('bank-decrypt-modal');
    if (modal) {
        modal.addEventListener('click', (e) => {
            const closeBtn = e.target.closest('[data-action="close-bank-decrypt-modal"]');
            if (closeBtn) {
                e.preventDefault();
                closeBankDecryptModal();
            }
        });
    }
});

async function loadWithdrawals(status = currentWithdrawalStatus) {
    currentWithdrawalStatus = status;
    const subtabsContainer = document.getElementById('subtabs-container');
    if (subtabsContainer) {
        subtabsContainer.style.display = 'block';
        subtabsContainer.innerHTML = `
            <div class="sub-tabs">
                <button type="button" class="btn-subtab ${/* safe */ status === 'REQUESTED' ? 'active' : ''}" data-action="switch-withdrawal-status" data-status="REQUESTED">Chờ duyệt</button>
                <button type="button" class="btn-subtab ${/* safe */ status === 'APPROVED' ? 'active' : ''}" data-action="switch-withdrawal-status" data-status="APPROVED">Đã duyệt (Chờ chi)</button>
                <button type="button" class="btn-subtab ${/* safe */ status === 'PROCESSING' ? 'active' : ''}" data-action="switch-withdrawal-status" data-status="PROCESSING">Đang xử lý chi</button>
                <button type="button" class="btn-subtab ${/* safe */ status === 'PAID' ? 'active' : ''}" data-action="switch-withdrawal-status" data-status="PAID">Đã chuyển thành công</button>
                <button type="button" class="btn-subtab ${/* safe */ status === 'REJECTED' ? 'active' : ''}" data-action="switch-withdrawal-status" data-status="REJECTED">Đã từ chối</button>
                <button type="button" class="btn-subtab ${/* safe */ status === '' ? 'active' : ''}" data-action="switch-withdrawal-status" data-status="">Tất cả</button>
            </div>
        `;
    }

    const container = document.getElementById('list-container');
    if (!container) return;
    container.innerHTML = '<div class="withdrawals-empty">Đang tải danh sách yêu cầu rút tiền...</div>';

    try {
        const query = status ? `?status=${encodeURIComponent(status)}` : '';
        const items = await apiFetch(`/admin/withdrawals${query}`);

        if (!items || items.length === 0) {
            container.innerHTML = '<div class="withdrawals-card withdrawals-empty">Không có yêu cầu rút tiền nào trong mục này.</div>';
            return;
        }

        const statusBadges = {
            'REQUESTED': '<span class="status-badge status-requested">Chờ duyệt</span>',
            'APPROVED': '<span class="status-badge status-approved">Đã duyệt</span>',
            'PROCESSING': '<span class="status-badge status-processing">Đang xử lý</span>',
            'PAID': '<span class="status-badge status-paid">Thành công</span>',
            'REJECTED': '<span class="status-badge status-rejected">Từ chối</span>',
            'CANCELLED': '<span class="status-badge status-cancelled">Hủy bởi Sitter</span>',
            'FAILED': '<span class="status-badge status-failed">Thất bại</span>'
        };

        const rows = items.map(w => {
            const timeStr = new Date(w.createdAt).toLocaleString('vi-VN');
            let actionHtml = '';

            if (w.status === 'REQUESTED') {
                actionHtml = `
                    <div class="withdrawals-action-group">
                        <button type="button" class="btn btn-primary btn-sm" data-action="approve-withdrawal" data-withdrawal-id="${/* safe */ escapeHtml(w.withdrawalId)}">Duyệt</button>
                        <button type="button" class="btn btn-outline btn-sm withdrawals-btn-reject" data-action="reject-withdrawal" data-withdrawal-id="${/* safe */ escapeHtml(w.withdrawalId)}">Từ chối</button>
                    </div>
                `;
            } else if (w.status === 'APPROVED') {
                actionHtml = `
                    <button type="button" class="btn btn-primary btn-sm withdrawals-btn-claim" data-action="claim-withdrawal" data-withdrawal-id="${/* safe */ escapeHtml(w.withdrawalId)}">Nhận xử lý chi</button>
                `;
            } else if (w.status === 'PROCESSING') {
                const currentUser = typeof getCurrentUser === 'function' ? getCurrentUser() : JSON.parse(localStorage.getItem('pawstay_user') || '{}');
                const isMe = w.claimedByAdminUserId === (currentUser?.id || currentUser?.userId);
                const claimText = isMe ? 'Bạn đang xử lý' : `Đang xử lý bởi Admin: ${escapeHtml(w.claimedByAdminUserId || '')}`;

                actionHtml = `
                    <div>
                        <div class="withdrawals-claim-info">ℹ️ ${/* safe */ claimText}</div>
                        <div class="withdrawals-action-group align-center">
                            <input type="text" id="bank-ref-${/* safe */ escapeHtml(w.withdrawalId)}" placeholder="Mã GD ngân hàng..." class="withdrawals-ref-input" />
                            <button type="button" class="btn btn-primary btn-sm withdrawals-btn-paid" data-action="confirm-paid-withdrawal" data-withdrawal-id="${/* safe */ escapeHtml(w.withdrawalId)}">Xác nhận PAID</button>
                            <button type="button" class="btn btn-outline btn-sm withdrawals-btn-unclaim" data-action="unclaim-withdrawal" data-withdrawal-id="${/* safe */ escapeHtml(w.withdrawalId)}" title="Nhả quyền xử lý">Nhả</button>
                        </div>
                    </div>
                `;
            } else if (w.status === 'PAID') {
                actionHtml = `<span class="withdrawals-paid-text">✅ Ref: <strong>${escapeHtml(w.bankTransactionRef || '—')}</strong></span>`;
            } else if (w.status === 'REJECTED') {
                actionHtml = `<span class="withdrawals-rejected-text">Lý do: ${escapeHtml(w.adminNote || '—')}</span>`;
            }

            return `
                <tr class="withdrawals-tr">
                    <td class="withdrawals-td">
                        <code>#${/* safe */ escapeHtml((w.withdrawalId || '').substring(0, 8))}</code><br/>
                        <small class="text-muted">${/* safe */ escapeHtml(timeStr)}</small>
                    </td>
                    <td class="withdrawals-td">
                        <strong>${escapeHtml(w.sitterName || w.sitterUserId)}</strong><br/>
                        <small class="text-muted">ID: ${/* safe */ escapeHtml((w.sitterUserId || '').substring(0, 8))}...</small>
                    </td>
                    <td class="withdrawals-td-amount">
                        ${/* safe */ formatTien(w.amount)}
                    </td>
                    <td class="withdrawals-td-bank">
                        <div><strong>${escapeHtml(w.bankName)}</strong></div>
                        <div>${escapeHtml(w.accountNumberMasked)}</div>
                        <div class="text-muted">${escapeHtml(w.accountHolderName)}</div>
                        <button type="button" class="btn btn-outline btn-sm withdrawals-btn-decrypt" data-action="show-decrypted-bank" data-withdrawal-id="${/* safe */ escapeHtml(w.withdrawalId)}">👁️ Xem STK đầy đủ</button>
                    </td>
                    <td class="withdrawals-td">${/* safe */ statusBadges[w.status] || escapeHtml(w.status)}</td>
                    <td class="withdrawals-td">${/* safe */ actionHtml}</td>
                </tr>
            `;
        }).join('');

        container.innerHTML = `
            <div class="withdrawals-card">
                <div class="withdrawals-header">
                    <h3 class="withdrawals-title">Yêu cầu rút tiền của Người chăm sóc (${/* safe */ items.length})</h3>
                    <button type="button" class="btn btn-outline btn-sm" data-action="reload-withdrawals">Làm mới</button>
                </div>
                <div class="withdrawals-table-wrap">
                    <table class="withdrawals-table">
                        <thead>
                            <tr class="withdrawals-thead-row">
                                <th class="withdrawals-th">Mã yêu cầu</th>
                                <th class="withdrawals-th">Sitter</th>
                                <th class="withdrawals-th">Số tiền</th>
                                <th class="withdrawals-th">Tài khoản nhận</th>
                                <th class="withdrawals-th">Trạng thái</th>
                                <th class="withdrawals-th">Thao tác</th>
                            </tr>
                        </thead>
                        <tbody>${/* safe */ rows}</tbody>
                    </table>
                </div>
            </div>
        `;
    } catch (err) {
        showError(err.message || 'Lỗi khi tải danh sách rút tiền.');
        container.innerHTML = '';
    }
}

async function approveWithdrawal(id, btn) {
    if (!confirm('Xác nhận phê duyệt yêu cầu rút tiền này? Trạng thái sẽ chuyển sang APPROVED.')) return;
    const originalText = btn ? btn.textContent : '';
    if (btn) {
        btn.disabled = true;
        btn.textContent = 'Đang duyệt...';
    }
    try {
        await apiFetch(`/admin/withdrawals/${id}/approve`, {
            method: 'POST',
            body: JSON.stringify({})
        });
        showToast('Đã phê duyệt yêu cầu rút tiền!', 3000, '✅');
        await loadWithdrawals(currentWithdrawalStatus);
    } catch (err) {
        showError(err.message || 'Lỗi khi duyệt yêu cầu rút tiền.');
        if (btn) {
            btn.disabled = false;
            btn.textContent = originalText;
        }
    }
}

async function rejectWithdrawal(id, btn) {
    const reason = prompt('Nhập lý do từ chối yêu cầu rút tiền (số tiền sẽ được hoàn trả khả dụng cho Sitter):');
    if (reason === null) return;
    if (!reason.trim()) {
        showError('Vui lòng nhập lý do từ chối.');
        return;
    }
    const originalText = btn ? btn.textContent : '';
    if (btn) {
        btn.disabled = true;
        btn.textContent = 'Đang từ chối...';
    }
    try {
        await apiFetch(`/admin/withdrawals/${id}/reject`, {
            method: 'POST',
            body: JSON.stringify({ reason: reason.trim() })
        });
        showToast('Đã từ chối yêu cầu rút tiền!', 3000, '❌');
        await loadWithdrawals(currentWithdrawalStatus);
    } catch (err) {
        showError(err.message || 'Lỗi khi từ chối yêu cầu rút tiền.');
        if (btn) {
            btn.disabled = false;
            btn.textContent = originalText;
        }
    }
}

async function claimProcessing(id, btn) {
    if (!confirm('Nhận xử lý chi cho yêu cầu này? Bạn sẽ được ghi nhận là Admin đang thực hiện chuyển khoản.')) return;
    const originalText = btn ? btn.textContent : '';
    if (btn) {
        btn.disabled = true;
        btn.textContent = 'Đang nhận...';
    }
    try {
        await apiFetch(`/admin/withdrawals/${id}/claim-processing`, { method: 'POST' });
        showToast('Đã nhận xử lý chi!', 3000, '✅');
        await loadWithdrawals(currentWithdrawalStatus);
    } catch (err) {
        showError(err.message || 'Lỗi khi nhận xử lý.');
        if (btn) {
            btn.disabled = false;
            btn.textContent = originalText;
        }
    }
}

async function unclaimWithdrawal(id, btn) {
    if (!confirm('Nhả xử lý yêu cầu này? Yêu cầu sẽ quay lại trạng thái Đã duyệt (APPROVED) để Admin khác có thể tiếp nhận.')) return;
    const originalText = btn ? btn.textContent : '';
    if (btn) {
        btn.disabled = true;
        btn.textContent = 'Đang nhả...';
    }
    try {
        await apiFetch(`/admin/withdrawals/${id}/unclaim`, { method: 'POST' });
        showToast('Đã nhả xử lý yêu cầu rút tiền!', 3000, '↩️');
        await loadWithdrawals(currentWithdrawalStatus);
    } catch (err) {
        showError(err.message || 'Lỗi khi nhả xử lý.');
        if (btn) {
            btn.disabled = false;
            btn.textContent = originalText;
        }
    }
}

async function confirmPaidWithdrawal(id, btn) {
    const input = document.getElementById(`bank-ref-${id}`);
    const bankRef = input ? input.value.trim() : '';
    if (!bankRef) {
        showError('Vui lòng nhập mã giao dịch ngân hàng chuyển khoản thành công.');
        return;
    }
    if (!confirm(`Xác nhận đã chuyển khoản thành công với Mã GD: "${bankRef}"? Số tiền sẽ được trừ chính thức khỏi sổ cái.`)) return;

    const originalText = btn ? btn.textContent : '';
    if (btn) {
        btn.disabled = true;
        btn.textContent = 'Đang xử lý...';
    }

    try {
        await apiFetch(`/admin/withdrawals/${id}/confirm-paid`, {
            method: 'POST',
            body: JSON.stringify({ bankTransactionRef: bankRef })
        });
        showToast('Đã xác nhận chi tiền thành công!', 3000, '✅');
        await loadWithdrawals(currentWithdrawalStatus);
    } catch (err) {
        showError(err.message || 'Lỗi khi xác nhận chi tiền.');
        if (btn) {
            btn.disabled = false;
            btn.textContent = originalText;
        }
    }
}

async function showDecryptedBank(id, btn) {
    const originalText = btn ? btn.textContent : '';
    if (btn) {
        btn.disabled = true;
        btn.textContent = 'Đang giải mã...';
    }
    try {
        const data = await apiFetch(`/admin/withdrawals/${id}/decrypted-bank`);
        const elName = document.getElementById('decrypt-bank-name');
        const elNum = document.getElementById('decrypt-account-number');
        const elHolder = document.getElementById('decrypt-account-holder');
        if (elName) elName.textContent = data.bankName || '—';
        if (elNum) elNum.textContent = data.accountNumber || '—';
        if (elHolder) elHolder.textContent = data.accountHolderName || '—';

        const modal = document.getElementById('bank-decrypt-modal');
        if (modal) modal.style.display = 'flex';
    } catch (err) {
        showError(err.message || 'Lỗi giải mã tài khoản ngân hàng.');
    } finally {
        if (btn) {
            btn.disabled = false;
            btn.textContent = originalText;
        }
    }
}

function closeBankDecryptModal() {
    const modal = document.getElementById('bank-decrypt-modal');
    if (modal) modal.style.display = 'none';

    // BẢO MẬT: Xóa sạch dữ liệu STK nhạy cảm khỏi DOM ngay khi đóng modal
    const elName = document.getElementById('decrypt-bank-name');
    const elNum = document.getElementById('decrypt-account-number');
    const elHolder = document.getElementById('decrypt-account-holder');
    if (elName) elName.textContent = '—';
    if (elNum) elNum.textContent = '—';
    if (elHolder) elHolder.textContent = '—';
}

window.loadWithdrawals = loadWithdrawals;
window.closeBankDecryptModal = closeBankDecryptModal;

