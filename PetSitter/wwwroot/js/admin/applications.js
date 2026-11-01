/* PawStay Admin Sitter Applications Module */
window.currentAppStatus = 'Pending';

document.addEventListener('DOMContentLoaded', () => {
    const subtabsContainer = document.getElementById('subtabs-container');
    if (subtabsContainer) {
        subtabsContainer.addEventListener('click', (e) => {
            const btn = e.target.closest('[data-action="switch-app-status"]');
            if (btn) {
                const status = btn.getAttribute('data-status');
                if (status) switchAppStatus(status);
            }
        });
    }

    const listContainer = document.getElementById('list-container');
    if (listContainer) {
        listContainer.addEventListener('click', async (e) => {
            if (currentTab !== 'applications') return;

            const approveBtn = e.target.closest('[data-action="approve-sitter"]');
            if (approveBtn) {
                const id = approveBtn.getAttribute('data-id');
                if (id) await approve(id, approveBtn);
                return;
            }

            const showRejectBtn = e.target.closest('[data-action="show-reject"]');
            if (showRejectBtn) {
                const id = showRejectBtn.getAttribute('data-id');
                if (id) showReject(id);
                return;
            }

            const cancelRejectBtn = e.target.closest('[data-action="cancel-reject"]');
            if (cancelRejectBtn) {
                const id = cancelRejectBtn.getAttribute('data-id');
                if (id) hideReject(id);
                return;
            }

            const confirmRejectBtn = e.target.closest('[data-action="confirm-reject"]');
            if (confirmRejectBtn) {
                const id = confirmRejectBtn.getAttribute('data-id');
                if (id) await reject(id, confirmRejectBtn);
            }
        });
    }
});

function switchAppStatus(status) {
    window.currentAppStatus = status;
    loadApplications(status);
}

async function loadApplications(status = window.currentAppStatus) {
    const subtabsContainer = document.getElementById('subtabs-container');
    if (subtabsContainer) {
        subtabsContainer.style.display = 'block';
        subtabsContainer.innerHTML = `
            <div class="sub-tabs">
                <button type="button" class="btn-subtab ${status === 'Pending' ? 'active' : ''}" data-action="switch-app-status" data-status="Pending">Chờ duyệt</button>
                <button type="button" class="btn-subtab ${status === 'Approved' ? 'active' : ''}" data-action="switch-app-status" data-status="Approved">Đã duyệt</button>
                <button type="button" class="btn-subtab ${status === 'Rejected' ? 'active' : ''}" data-action="switch-app-status" data-status="Rejected">Đã từ chối</button>
            </div>
        `;
    }

    const container = document.getElementById('list-container');
    if (!container) return;

    container.innerHTML = '<div class="loading-text">Đang tải danh sách hồ sơ...</div>';

    try {
        const apps = await apiFetch(`/admin/sitter-applications?status=${encodeURIComponent(status)}`);
        if (!apps || apps.length === 0) {
            const statusLabels = { 'Pending': 'đang chờ duyệt', 'Approved': 'đã được duyệt', 'Rejected': 'bị từ chối' };
            container.innerHTML = `<p class="empty-text">Không có hồ sơ nào ${statusLabels[status] || status}.</p>`;
            return;
        }

        container.innerHTML = apps.map(app => {
            let actionBtns = '';
            let statusBadge = '';

            if (status === 'Pending') {
                actionBtns = `
                    <div class="action-btns">
                        <button type="button" class="btn btn-primary btn-sm" data-action="approve-sitter" data-id="${escapeHtml(app.id)}">Duyệt</button>
                        <button type="button" class="btn btn-outline btn-sm" data-action="show-reject" data-id="${escapeHtml(app.id)}">Từ chối</button>
                    </div>
                `;
            } else if (status === 'Approved') {
                statusBadge = '<span class="status-badge badge-app-approved">Đã duyệt</span>';
            } else if (status === 'Rejected') {
                statusBadge = '<span class="status-badge badge-app-rejected">Đã từ chối</span>';
            }

            const rejectReasonHtml = (app.status === 'Rejected' && app.lyDoTuChoi) 
                ? `<p class="reject-reason-text"><strong>Lý do từ chối:</strong> ${escapeHtml(app.lyDoTuChoi)}</p>` 
                : '';

            return `
                <div class="card" id="app-${escapeHtml(app.id)}">
                    <div class="header-row">
                        <div>
                            <h3 class="app-title">${escapeHtml(app.fullName)} (Khu vực: ${escapeHtml(app.khuVuc)})</h3>
                            ${statusBadge}
                        </div>
                        ${actionBtns}
                    </div>
                    <p><strong>Kinh nghiệm:</strong> ${escapeHtml(String(app.kinhNghiem || 0))} năm</p>
                    <p><strong>Giá cơ bản:</strong> ${formatTien(app.giaMoiNgay)} / ngày</p>
                    <p><strong>Loại Pet nhận:</strong> ${(app.loaiPetNhan || []).map(escapeHtml).join(', ')}</p>
                    <p><strong>Tags:</strong> ${(app.tags || []).map(escapeHtml).join(', ')}</p>
                    <p><strong>Bio:</strong> ${escapeHtml(app.bio)}</p>
                    ${rejectReasonHtml}
                    
                    <div class="reject-container" id="reject-${escapeHtml(app.id)}">
                        <input type="text" id="lydo-${escapeHtml(app.id)}" placeholder="Lý do từ chối (tối đa 500 ký tự)..." maxlength="500" />
                        <div class="reject-actions-row">
                            <button type="button" class="btn btn-outline btn-sm" data-action="confirm-reject" data-id="${escapeHtml(app.id)}">Xác nhận từ chối</button>
                            <button type="button" class="btn btn-outline btn-sm" data-action="cancel-reject" data-id="${escapeHtml(app.id)}">Hủy</button>
                        </div>
                    </div>
                </div>
            `;
        }).join('');
    } catch (err) {
        showError(err.message || 'Lỗi tải danh sách hồ sơ. Bạn có phải Admin không?');
        container.innerHTML = '<div class="empty-text error-text">Không thể tải danh sách hồ sơ.</div>';
    }
}

function showReject(id) {
    const el = document.getElementById(`reject-${id}`);
    if (el) el.style.display = 'block';
}

function hideReject(id) {
    const el = document.getElementById(`reject-${id}`);
    if (el) el.style.display = 'none';
}

async function approve(id, btn) {
    if (!confirm('Xác nhận phê duyệt hồ sơ người chăm sóc này?')) {
        return;
    }

    if (btn) {
        if (btn.disabled) return;
        btn.disabled = true;
        btn.textContent = 'Đang duyệt...';
    }

    try {
        await apiFetch(`/admin/sitter-applications/${encodeURIComponent(id)}/approve`, { method: 'PATCH' });
        if (typeof showToast === 'function') {
            showToast('Đã duyệt hồ sơ người chăm sóc thành công!', 3000, '✅');
        } else {
            alert('Đã duyệt hồ sơ người chăm sóc thành công!');
        }
        const card = document.getElementById(`app-${id}`);
        if (card) card.remove();
        if (!document.querySelector('#list-container .card')) {
            loadApplications(window.currentAppStatus);
        }
    } catch (err) {
        showError(err.message || 'Lỗi khi duyệt hồ sơ');
        if (btn) {
            btn.disabled = false;
            btn.textContent = 'Duyệt';
        }
    }
}

async function reject(id, btn) {
    const reasonInput = document.getElementById(`lydo-${id}`);
    const reason = reasonInput ? reasonInput.value.trim() : '';
    if (!reason) {
        showError('Vui lòng nhập lý do từ chối.');
        return;
    }

    if (!confirm('Xác nhận từ chối hồ sơ người chăm sóc này?')) {
        return;
    }

    if (btn) {
        if (btn.disabled) return;
        btn.disabled = true;
        btn.textContent = 'Đang từ chối...';
    }

    try {
        await apiFetch(`/admin/sitter-applications/${encodeURIComponent(id)}/reject`, { 
            method: 'PATCH',
            body: JSON.stringify({ lyDo: reason })
        });
        if (typeof showToast === 'function') {
            showToast('Đã từ chối hồ sơ người chăm sóc!', 3000, '❌');
        } else {
            alert('Đã từ chối hồ sơ người chăm sóc!');
        }
        const card = document.getElementById(`app-${id}`);
        if (card) card.remove();
        if (!document.querySelector('#list-container .card')) {
            loadApplications(window.currentAppStatus);
        }
    } catch (err) {
        showError(err.message || 'Lỗi khi từ chối hồ sơ');
        if (btn) {
            btn.disabled = false;
            btn.textContent = 'Xác nhận từ chối';
        }
    }
}
