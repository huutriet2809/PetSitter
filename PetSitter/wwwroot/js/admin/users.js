/* PawStay Admin Users Management Module */
let currentUserSearch = '';
let currentUserRole = '';
let currentUserActive = '';
let currentUserPage = 1;

document.addEventListener('DOMContentLoaded', () => {
    const listContainer = document.getElementById('list-container');
    if (listContainer) {
        listContainer.addEventListener('click', async (e) => {
            if (currentTab !== 'users') return;

            const applyBtn = e.target.closest('[data-action="apply-user-filters"]');
            if (applyBtn) {
                applyUserFilters();
                return;
            }

            const resetBtn = e.target.closest('[data-action="reset-user-filters"]');
            if (resetBtn) {
                resetUserFilters();
                return;
            }

            const toggleBtn = e.target.closest('[data-action="toggle-user-status"]');
            if (toggleBtn) {
                const userId = toggleBtn.getAttribute('data-user-id');
                const newActive = toggleBtn.getAttribute('data-active') === 'true';
                const userName = toggleBtn.getAttribute('data-user-name') || '';
                if (userId) {
                    await toggleUserStatus(userId, newActive, userName, toggleBtn);
                }
                return;
            }

            const pageBtn = e.target.closest('[data-action="user-page"]');
            if (pageBtn) {
                const targetPage = parseInt(pageBtn.getAttribute('data-page'), 10);
                if (targetPage > 0) {
                    loadUsers(targetPage);
                }
            }
        });

        listContainer.addEventListener('keydown', (e) => {
            if (currentTab !== 'users') return;
            if (e.target && e.target.id === 'user-search-input' && e.key === 'Enter') {
                applyUserFilters();
            }
        });
    }
});

async function loadUsers(page = currentUserPage) {
    currentUserPage = page;
    const container = document.getElementById('list-container');
    if (!container) return;

    container.innerHTML = '<div class="loading-text">Đang tải danh sách người dùng...</div>';

    try {
        let url = `/admin/users?page=${page}&pageSize=15`;
        if (currentUserSearch) url += `&search=${encodeURIComponent(currentUserSearch)}`;
        if (currentUserRole) url += `&role=${encodeURIComponent(currentUserRole)}`;
        if (currentUserActive !== '') url += `&isActive=${encodeURIComponent(currentUserActive)}`;

        const res = await apiFetch(url);
        const users = res.data || [];
        const total = res.total || 0;
        const totalPages = res.totalPages || 1;

        let currentUser = null;
        try {
            currentUser = JSON.parse(localStorage.getItem('pawstay_user') || '{}');
        } catch (e) {
            currentUser = null;
        }

        const filterBarHtml = `
            <div class="card user-filter-card">
                <div class="user-filter-row">
                    <input type="text" id="user-search-input" class="user-search-input" value="${escapeHtml(currentUserSearch)}" placeholder="Tìm kiếm theo tên, email, SĐT..." />
                    <select id="user-role-select" class="user-select">
                        <option value="" ${currentUserRole === '' ? 'selected' : ''}>Tất cả vai trò</option>
                        <option value="USER" ${currentUserRole === 'USER' ? 'selected' : ''}>Chủ nuôi (USER)</option>
                        <option value="SITTER" ${currentUserRole === 'SITTER' ? 'selected' : ''}>Người chăm sóc (SITTER)</option>
                        <option value="ADMIN" ${currentUserRole === 'ADMIN' ? 'selected' : ''}>Quản trị viên (ADMIN)</option>
                    </select>
                    <select id="user-active-select" class="user-select">
                        <option value="" ${currentUserActive === '' ? 'selected' : ''}>Tất cả trạng thái</option>
                        <option value="true" ${currentUserActive === 'true' ? 'selected' : ''}>Đang hoạt động</option>
                        <option value="false" ${currentUserActive === 'false' ? 'selected' : ''}>Đã bị khóa</option>
                    </select>
                    <button type="button" class="btn btn-primary btn-sm" data-action="apply-user-filters">Tìm kiếm</button>
                    <button type="button" class="btn btn-outline btn-sm" data-action="reset-user-filters">Đặt lại</button>
                </div>
            </div>
        `;

        if (users.length === 0) {
            container.innerHTML = filterBarHtml + '<div class="card"><p class="empty-text">Không tìm thấy người dùng nào phù hợp.</p></div>';
            return;
        }

        const tableRows = users.map(u => {
            const isMe = currentUser && (currentUser.userId === u.userId);
            const isLockedOut = u.lockoutEndUtc && new Date(u.lockoutEndUtc) > new Date();
            
            let statusBadge = '';
            if (!u.isActive) {
                statusBadge = '<span class="status-badge badge-user-locked">Đã khóa</span>';
            } else if (isLockedOut) {
                const minRem = Math.ceil((new Date(u.lockoutEndUtc) - new Date()) / 60000);
                statusBadge = `<span class="status-badge badge-user-temp-locked" title="Khóa tạm ${minRem}p do sai mật khẩu">Tạm khóa (${minRem}p)</span>`;
            } else {
                statusBadge = '<span class="status-badge badge-user-active">Hoạt động</span>';
            }

            let actionBtn = '';
            if (isMe) {
                actionBtn = '<span class="user-self-label">(Tài khoản của bạn)</span>';
            } else if (u.isActive) {
                actionBtn = `<button type="button" class="btn btn-outline btn-sm btn-lock-user" data-action="toggle-user-status" data-user-id="${escapeHtml(u.userId)}" data-active="false" data-user-name="${escapeHtml(u.fullName)}">Khóa tài khoản</button>`;
            } else {
                actionBtn = `<button type="button" class="btn btn-primary btn-sm btn-unlock-user" data-action="toggle-user-status" data-user-id="${escapeHtml(u.userId)}" data-active="true" data-user-name="${escapeHtml(u.fullName)}">Mở khóa</button>`;
            }

            const createdDate = u.createdAt ? new Date(u.createdAt).toLocaleDateString('vi-VN') : '—';

            return `
                <tr class="users-table-row">
                    <td>
                        <div class="user-name-bold">${escapeHtml(u.fullName)}</div>
                        <div class="user-meta-sub">${escapeHtml(u.email)}</div>
                    </td>
                    <td class="user-phone-cell">${escapeHtml(u.phone || '—')}</td>
                    <td>
                        <span class="user-role-badge">${escapeHtml(u.role)}</span>
                    </td>
                    <td>${statusBadge}</td>
                    <td class="user-meta-sub">${escapeHtml(createdDate)}</td>
                    <td class="user-action-cell">${actionBtn}</td>
                </tr>
            `;
        }).join('');

        const paginationHtml = totalPages > 1 ? `
            <div class="user-pagination-wrap">
                <button type="button" class="btn btn-outline btn-sm ${page <= 1 ? 'btn-page-disabled' : ''}" data-action="user-page" data-page="${page - 1}">« Trang trước</button>
                <span class="user-page-info">Trang ${page} / ${totalPages} (${total} người dùng)</span>
                <button type="button" class="btn btn-outline btn-sm ${page >= totalPages ? 'btn-page-disabled' : ''}" data-action="user-page" data-page="${page + 1}">Trang sau »</button>
            </div>
        ` : '';

        container.innerHTML = `
            ${filterBarHtml}
            <div class="card users-table-wrap">
                <table class="users-table">
                    <thead>
                        <tr class="users-table-head-row">
                            <th>Họ tên & Email</th>
                            <th>Số điện thoại</th>
                            <th>Vai trò</th>
                            <th>Trạng thái</th>
                            <th>Ngày tạo</th>
                            <th class="user-action-cell">Hành động</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${tableRows}
                    </tbody>
                </table>
                ${paginationHtml}
            </div>
        `;
    } catch (err) {
        showError(err.message || 'Lỗi khi tải danh sách người dùng.');
        container.innerHTML = '<div class="card"><p class="empty-text error-text">Không thể tải danh sách người dùng.</p></div>';
    }
}

function applyUserFilters() {
    const searchEl = document.getElementById('user-search-input');
    const roleEl = document.getElementById('user-role-select');
    const activeEl = document.getElementById('user-active-select');
    currentUserSearch = searchEl ? searchEl.value.trim() : '';
    currentUserRole = roleEl ? roleEl.value : '';
    currentUserActive = activeEl ? activeEl.value : '';
    loadUsers(1);
}

function resetUserFilters() {
    currentUserSearch = '';
    currentUserRole = '';
    currentUserActive = '';
    loadUsers(1);
}

async function toggleUserStatus(userId, newActive, userName, btn) {
    const confirmMsg = newActive
        ? `Bạn có chắc chắn muốn mở khóa tài khoản cho "${userName}"?`
        : `CẢNH BÁO: Bạn có chắc chắn muốn khóa tài khoản "${userName}"?\n\nTài khoản sẽ bị vô hiệu hóa và ngay lập tức bị đăng xuất khỏi tất cả thiết bị.`;

    if (!confirm(confirmMsg)) return;

    if (btn) {
        if (btn.disabled) return;
        btn.disabled = true;
        btn.textContent = 'Đang xử lý...';
    }

    try {
        await apiFetch(`/admin/users/${encodeURIComponent(userId)}/status`, {
            method: 'PATCH',
            body: JSON.stringify({ isActive: newActive })
        });

        if (typeof showToast === 'function') {
            showToast(newActive ? 'Đã mở khóa tài khoản thành công!' : 'Đã khóa tài khoản và thu hồi toàn bộ phiên đăng nhập!', 3000, '✅');
        } else {
            alert(newActive ? 'Đã mở khóa tài khoản thành công!' : 'Đã khóa tài khoản thành công!');
        }
        loadUsers(currentUserPage);
    } catch (err) {
        showError(err.message || 'Không thể cập nhật trạng thái người dùng.');
        if (btn) {
            btn.disabled = false;
            btn.textContent = newActive ? 'Mở khóa' : 'Khóa tài khoản';
        }
    }
}

