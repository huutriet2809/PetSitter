/* PawStay Admin Core & Routing */
let currentTab = 'applications';

window.addEventListener('pageshow', () => {
    if (!localStorage.getItem('pawstay_token')) {
        window.location.replace('login.html');
    }
});

function getActiveTabFromUrl() {
    if (window.location.hash) {
        const hash = window.location.hash.replace('#', '').trim().toLowerCase();
        if (hash) return hash;
    }
    const urlParams = new URLSearchParams(window.location.search);
    if (urlParams.get('bookingId')) return 'disputes';
    return urlParams.get('tab') || 'applications';
}

document.addEventListener('DOMContentLoaded', async () => {
    if (!localStorage.getItem('pawstay_token')) {
        window.location.href = 'login.html';
        return;
    }

    const user = typeof getCurrentUser === 'function' 
        ? getCurrentUser() 
        : JSON.parse(localStorage.getItem('pawstay_user') || 'null');
    const role = (user && user.role ? user.role : '').toUpperCase();
    if (role !== 'ADMIN') {
        window.location.href = role === 'SITTER' ? 'sitter-dashboard.html' : 'owner-dashboard.html';
        return;
    }

    // Sidebar menu navigation via event delegation
    const adminMenu = document.getElementById('admin-menu');
    if (adminMenu) {
        adminMenu.addEventListener('click', (e) => {
            const link = e.target.closest('[data-tab]');
            if (link) {
                e.preventDefault();
                const tab = link.getAttribute('data-tab');
                if (tab) switchTab(tab, true);
            }
        });
    }

    const initialTab = getActiveTabFromUrl();
    switchTab(initialTab, false);
});

window.addEventListener('hashchange', () => {
    const tab = getActiveTabFromUrl();
    switchTab(tab, false);
});

function switchTab(tab, updateHash = true) {
    currentTab = tab;
    if (updateHash) {
        if (window.location.hash !== `#${tab}`) {
            window.location.hash = `#${tab}`;
        }
    }

    // Highlight sidebar menu
    document.querySelectorAll('.sidebar-menu a').forEach(a => {
        if (a.getAttribute('data-tab') === tab) {
            a.classList.add('active');
        } else {
            a.classList.remove('active');
        }
    });

    // Update page title
    const titleEl = document.getElementById('page-title');
    if (titleEl) {
        const titles = {
            'applications': 'Quản trị hồ sơ Chăm sóc viên (Sitter)',
            'users': 'Quản lý Người dùng & Bảo mật',
            'payments': 'Thanh toán & Tạm giữ (Escrow)',
            'refunds': 'Yêu cầu Hoàn tiền (Refunds)',
            'withdrawals': 'Quản lý Rút tiền Sitter (Payouts)',
            'revenue': 'Tổng quan Doanh thu Nền tảng',
            'reconcile': 'Đối soát & Kiểm toán Sổ cái kép',
            'suspense': 'Xử lý Giao dịch bất thường (Tài khoản SUSPENSE)',
            'disputes': 'Xử lý Đơn khiếu nại & Tranh chấp'
        };
        titleEl.textContent = titles[tab] || 'Quản trị hệ thống PawStay';
    }

    const subtabsContainer = document.getElementById('subtabs-container');
    if (tab === 'applications') {
        if (subtabsContainer) subtabsContainer.style.display = 'block';
        if (typeof loadApplications === 'function') {
            loadApplications(window.currentAppStatus || 'Pending');
        }
    } else {
        if (subtabsContainer) subtabsContainer.style.display = 'none';
        if (tab === 'users') {
            if (typeof loadUsers === 'function') loadUsers();
        } else if (tab === 'disputes') {
            if (typeof loadDisputes === 'function') loadDisputes();
        } else if (tab === 'payments') {
            if (typeof loadPaymentsOverview === 'function') loadPaymentsOverview();
        } else if (tab === 'refunds') {
            if (typeof loadPaymentsOverview === 'function') loadPaymentsOverview();
        } else if (tab === 'withdrawals') {
            if (typeof loadWithdrawals === 'function') loadWithdrawals('REQUESTED');
        } else if (tab === 'revenue') {
            if (typeof loadRevenue === 'function') loadRevenue();
        } else if (tab === 'reconcile') {
            if (typeof loadReconciliation === 'function') loadReconciliation();
        } else if (tab === 'suspense') {
            if (typeof loadSuspensePayments === 'function') loadSuspensePayments(1, 'ABNORMAL');
        }
    }
}

function showError(msg) {
    const el = document.getElementById('error-msg');
    if (!el) return;
    el.textContent = msg;
    el.style.display = 'block';
    setTimeout(() => {
        el.style.display = 'none';
    }, 5000);
}

function escapeHtml(unsafe) {
    return (unsafe || '').toString()
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;")
        .replace(/'/g, "&#039;");
}

