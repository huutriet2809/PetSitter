// ================================================
// PAWSTAY - Shared Components (components.js)
// Inject navbar và footer vào tất cả các trang
// Để chỉnh menu/footer, chỉ cần sửa file này
// ================================================

// ------------------------------------------------
// Xác định trang hiện tại để highlight nav link
// ------------------------------------------------
function getCurrentPage() {
  const path = window.location.pathname;
  const file = path.split('/').pop() || 'index.html';
  return file;
}

function safeEscape(str) {
  if (str == null) return '';
  return String(str)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}
if (typeof window !== 'undefined' && typeof window.escapeHtml !== 'function') {
  window.escapeHtml = safeEscape;
}

function isCurrentPage(targetHref, currentPage) {
  if (!targetHref) return false;
  const linkFile = targetHref.split('?')[0].split('#')[0];
  const curFile = (currentPage || '').split('?')[0].split('#')[0];
  if (linkFile === curFile) return true;
  if ((curFile === '' || curFile === 'index.html' || curFile === '/') && linkFile === 'index.html') return true;
  return false;
}

// ------------------------------------------------
// DỮ LIỆU ĐIỀU HƯỚNG THEO ROLE (GIỮA & DROPDOWN)
// ------------------------------------------------
function getNavLinksForRole(role, isLoggedIn) {
  if (!isLoggedIn) {
    return [
      { href: 'index.html', label: 'Trang chủ' },
      { href: 'sitters.html', label: 'Tìm Sitter' },
      { href: 'how-it-works.html', label: 'Cách hoạt động' },
      { href: 'contact.html', label: 'Liên hệ' }
    ];
  }

  const normalized = (role || '').toUpperCase();
  if (normalized === 'ADMIN') {
    return [
      { href: 'admin.html', label: 'Quản trị' },
      { href: 'admin-messages.html', label: 'Tin nhắn' }
    ];
  }

  if (normalized === 'SITTER') {
    return [
      { href: 'sitter-dashboard.html', label: 'Bảng điều khiển' },
      { href: 'sitter-calendar.html', label: 'Lịch rảnh' },
      { href: 'sitter-profile.html', label: 'Hồ sơ Sitter' }
    ];
  }

  // Mặc định: OWNER / USER
  return [
    { href: 'index.html', label: 'Trang chủ' },
    { href: 'sitters.html', label: 'Tìm Sitter' },
    { href: 'my-bookings.html', label: 'Lịch của tôi' },
    { href: 'how-it-works.html', label: 'Cách hoạt động' }
  ];
}

function getDropdownLinksForRole(role) {
  const normalized = (role || '').toUpperCase();
  if (normalized === 'ADMIN') {
    return [
      { href: 'profile.html', label: 'Thông tin tài khoản', icon: '👤' }
    ];
  }

  if (normalized === 'SITTER') {
    return [
      { href: 'profile.html', label: 'Thông tin tài khoản', icon: '👤' }
    ];
  }

  // Mặc định: OWNER / USER
  return [
    { href: 'owner-dashboard.html', label: 'Bảng điều khiển', icon: '📊' },
    { href: 'my-pets.html', label: 'Thú cưng của tôi', icon: '🐾' },
    { href: 'become-sitter.html', label: 'Trở thành Sitter', icon: '✨' },
    { href: 'profile.html', label: 'Thông tin tài khoản', icon: '👤' }
  ];
}

function getRoleBadgeText(role) {
  const normalized = (role || '').toUpperCase();
  if (normalized === 'ADMIN') return 'Quản trị viên';
  if (normalized === 'SITTER') return 'Pet Sitter';
  return 'Chủ nuôi';
}

// ------------------------------------------------
// RENDER NAVBAR
// ------------------------------------------------
function renderNavbar() {
  // Xóa navbar cũ nếu đã tồn tại để tránh nhân đôi khi gọi nhiều lần
  const oldNavbar = document.getElementById('navbar');
  if (oldNavbar) oldNavbar.remove();
  const oldMobileNav = document.getElementById('mobile-nav');
  if (oldMobileNav) oldMobileNav.remove();

  const currentPage = getCurrentPage();
  const user        = typeof getCurrentUser === 'function' ? getCurrentUser() : null;
  const userRole    = user ? (user.role || '').toUpperCase() : '';

  // Lấy danh sách link giữa và dropdown theo role
  const centerLinks   = getNavLinksForRole(userRole, !!user);
  const dropdownLinks = user ? getDropdownLinksForRole(userRole) : [];
  const roleBadgeText = getRoleBadgeText(userRole);

  // Tạo center nav links HTML (Desktop)
  const navLinksHtml = centerLinks.map(link => {
    const isActive = isCurrentPage(link.href, currentPage) ? 'active' : '';
    return `<li><a href="${safeEscape(link.href)}" class="${safeEscape(isActive)}">${safeEscape(link.label)}</a></li>`;
  }).join('');

  // Mobile links (Link giữa)
  const mobileCenterLinksHtml = centerLinks.map(link => {
    const isActive = isCurrentPage(link.href, currentPage) ? 'active' : '';
    return `<a href="${safeEscape(link.href)}" class="${safeEscape(isActive)}">${safeEscape(link.label)}</a>`;
  }).join('');

  // Chuẩn bị thông tin người dùng cho Avatar & Dropdown
  let avatarLetter = 'U';
  let shortName = 'Bạn';
  let rawName = 'Bạn';

  if (user) {
    rawName = (user.fullName || user.ten || user.email || 'Bạn').trim();
    if (rawName.includes('@')) {
      shortName = rawName.split('@')[0];
      avatarLetter = (shortName.charAt(0) || 'U').toUpperCase();
    } else {
      const parts = rawName.split(/\s+/).filter(Boolean);
      shortName = parts.length > 1 ? parts.slice(-2).join(' ') : rawName;
      const lastWord = parts[parts.length - 1];
      avatarLetter = (lastWord ? lastWord.charAt(0) : rawName.charAt(0)).toUpperCase();
    }
  }

  const safeShortName     = safeEscape(shortName);
  const safeFullName      = safeEscape(rawName);
  const safeEmail         = safeEscape(user && user.email ? user.email : '');
  const safeAvatarLetter  = safeEscape(avatarLetter);
  const safeRoleBadgeText = safeEscape(roleBadgeText);

  // Chuông thông báo (In-app)
  const notifBellHtml = user
    ? `<div class="notif-wrapper" id="notif-wrapper">
         <button id="notif-bell-btn" class="notif-bell" aria-label="Thông báo" aria-expanded="false" type="button">
           🔔<span id="notif-badge" class="notif-badge hidden"></span>
         </button>
         <div id="notif-dropdown" class="notif-dropdown hidden">
           <div class="notif-header">
             <span>Thông báo</span>
             <button id="notif-mark-all" class="btn btn-ghost btn-sm notif-mark-all-btn" type="button">Đã đọc tất cả</button>
           </div>
           <div id="notif-list" class="notif-list"></div>
           <div id="notif-empty" class="notif-empty hidden">
             Không có thông báo nào
           </div>
         </div>
       </div>`
    : '';

  // Dropdown người dùng (Desktop)
  const accountDropdownHtml = user
    ? `<div class="account-menu-wrapper" id="account-menu-wrapper">
         <button id="account-menu-btn" class="account-menu-btn" type="button" aria-expanded="false" aria-haspopup="true" aria-label="Menu tài khoản của ${safeShortName}">
           <span class="account-avatar" aria-hidden="true">${safeAvatarLetter}</span>
           <span class="account-shortname">${safeShortName}</span>
           <span class="account-caret" aria-hidden="true">▾</span>
         </button>
         <div id="account-menu-dropdown" class="account-menu-dropdown hidden" role="menu" aria-label="Menu người dùng">
           <div class="account-menu-header">
             <div class="account-menu-user">${safeFullName}</div>
             ${safeEmail ? `<div class="account-menu-email">${safeEmail}</div>` : ''}
             <span class="account-menu-badge">${safeRoleBadgeText}</span>
           </div>
           <div class="account-menu-items">
             ${dropdownLinks.map(l => {
               const activeClass = isCurrentPage(l.href, currentPage) ? 'active' : '';
               return `<a href="${safeEscape(l.href)}" class="account-menu-item ${activeClass}" role="menuitem">${l.icon ? l.icon + ' ' : ''}${safeEscape(l.label)}</a>`;
             }).join('')}
           </div>
           <div class="account-menu-divider"></div>
           <button type="button" data-action="logout" class="account-menu-item account-menu-logout" role="menuitem">
             <span>🚪 Đăng xuất</span>
           </button>
         </div>
       </div>`
    : '';

  // Khối bên phải (Desktop)
  const authHtml = user
    ? `${notifBellHtml}
       ${accountDropdownHtml}`
    : `<a href="login.html" class="btn btn-ghost btn-sm" id="nav-login">Đăng nhập</a>
       <a href="signup.html" class="btn btn-primary btn-sm" id="nav-signup">Đăng ký</a>`;

  // Khối bên trong Mobile Drawer
  const mobileDrawerHtml = user
    ? `<div class="mobile-nav-links">
         ${mobileCenterLinksHtml}
       </div>
       <div class="mobile-nav-divider"></div>
       <div class="mobile-nav-auth">
         <div class="mobile-user-card">
           <div class="account-avatar">${safeAvatarLetter}</div>
           <div class="mobile-user-details">
             <div class="mobile-user-name">${safeFullName}</div>
             ${safeEmail ? `<div class="mobile-user-email">${safeEmail}</div>` : ''}
             <span class="account-menu-badge">${safeRoleBadgeText}</span>
           </div>
         </div>
         <div class="mobile-user-menu">
           ${dropdownLinks.map(l => {
             const activeClass = isCurrentPage(l.href, currentPage) ? 'active' : '';
             return `<a href="${safeEscape(l.href)}" class="mobile-nav-item ${activeClass}">${l.icon ? l.icon + ' ' : ''}${safeEscape(l.label)}</a>`;
           }).join('')}
         </div>
         <div class="mobile-nav-divider"></div>
         <button type="button" data-action="logout" class="btn btn-outline mobile-logout-btn">
           🚪 Đăng xuất
         </button>
       </div>`
    : `<div class="mobile-nav-links">
         ${mobileCenterLinksHtml}
       </div>
       <div class="mobile-nav-divider"></div>
       <div class="mobile-nav-auth">
         <div class="mobile-guest-auth">
           <a href="login.html" class="btn btn-ghost btn-sm">Đăng nhập</a>
           <a href="signup.html" class="btn btn-primary btn-sm">Đăng ký</a>
         </div>
       </div>`;

  const navbarHtml = `
    <nav class="navbar" id="navbar" role="navigation" aria-label="Điều hướng chính">
      <div class="navbar-inner">
        <!-- Logo (trái) -->
        <a href="index.html" class="logo" aria-label="PawStay - Trang chủ">
          <div class="logo-icon" aria-hidden="true">🐾</div>
          <span class="logo-text">Paw<span>Stay</span></span>
        </a>

        <!-- Link điều hướng chính (giữa) -->
        <ul class="nav-center" role="list">
          ${navLinksHtml}
        </ul>

        <!-- Chuông thông báo + Dropdown người dùng / Nút đăng nhập-đăng ký (phải) -->
        <div class="nav-right">
          ${authHtml}
        </div>

        <!-- Mobile toggle -->
        <button class="nav-toggle" id="nav-toggle" aria-label="Mở menu" aria-expanded="false">
          <span id="nav-toggle-icon">☰</span>
        </button>
      </div>
    </nav>

    <!-- Mobile drawer -->
    <div class="mobile-nav" id="mobile-nav" role="dialog" aria-label="Menu di động">
      ${mobileDrawerHtml}
    </div>
  `;

  // Chèn navbar vào đầu body
  document.body.insertAdjacentHTML('afterbegin', navbarHtml);

  // Khởi tạo tính năng thông báo và menu tài khoản nếu người dùng đã đăng nhập
  if (user) {
    initNotificationSystem(user);
    initAccountDropdown();
  }

  // Gắn sự kiện mobile toggle
  const toggleBtn = document.getElementById('nav-toggle');
  const mobileNav = document.getElementById('mobile-nav');
  if (toggleBtn && mobileNav) {
    toggleBtn.addEventListener('click', function() {
      const toggleIcon = document.getElementById('nav-toggle-icon');
      if (!toggleIcon) return;
      const isOpen = mobileNav.classList.toggle('open');
      this.setAttribute('aria-expanded', isOpen);
      toggleIcon.textContent = isOpen ? '✕' : '☰';
    });

    // Đóng drawer khi bấm vào bất kỳ liên kết nào bên trong
    mobileNav.querySelectorAll('a').forEach(link => {
      link.addEventListener('click', function() {
        mobileNav.classList.remove('open');
        toggleBtn.setAttribute('aria-expanded', 'false');
        const toggleIcon = document.getElementById('nav-toggle-icon');
        if (toggleIcon) toggleIcon.textContent = '☰';
      });
    });
  }

  // Đóng mobile nav khi click ra ngoài
  document.addEventListener('click', function(e) {
    const mNav   = document.getElementById('mobile-nav');
    const toggle = document.getElementById('nav-toggle');
    if (mNav && mNav.classList.contains('open') &&
        !mNav.contains(e.target) && !toggle.contains(e.target)) {
      mNav.classList.remove('open');
      const toggleIcon = document.getElementById('nav-toggle-icon');
      if (toggleIcon) toggleIcon.textContent = '☰';
      if (toggle) toggle.setAttribute('aria-expanded', 'false');
    }
  });
}

// ------------------------------------------------
// QUẢN LÝ THÔNG BÁO IN-APP (BELL & DROPDOWN)
// ------------------------------------------------
let notifPollTimer = null;

function navigateForNotification(item) {
  if (!item || !item.bookingId) return;
  const bid = encodeURIComponent(item.bookingId);

  // 1. Điều hướng trực tiếp theo TargetRole (OWNER / SITTER / ADMIN) lúc tạo
  const targetRole = (item.targetRole || '').toUpperCase();
  if (targetRole === 'OWNER') {
    window.location.href = `my-bookings.html?bookingId=${bid}`;
    return;
  }
  if (targetRole === 'SITTER') {
    window.location.href = `sitter-dashboard.html?bookingId=${bid}`;
    return;
  }
  if (targetRole === 'ADMIN') {
    window.location.href = `admin.html?bookingId=${bid}`;
    return;
  }

  // 2. Ghi chú migration: Fallback cho các bản ghi thông báo cũ chưa có trường targetRole theo Type
  const type = item.type;
  const user = typeof getCurrentUser === 'function' ? getCurrentUser() : null;
  const currentRole = (user && user.role ? user.role : '').toUpperCase();

  // Nhóm 1: Các loại thông báo chỉ gửi riêng cho Sitter
  if (type === 'BOOKING_NEW' || type === 'REMINDER_PENDING_EXPIRES' || type === 'REMINDER_OVERDUE_ACCEPTED') {
    window.location.href = `sitter-dashboard.html?bookingId=${bid}`;
    return;
  }

  // Nhóm 2: Các loại thông báo chỉ gửi riêng cho Owner
  if (type === 'BOOKING_COMPLETION_PENDING' || type === 'REMINDER_COMPLETION_EXPIRES' || type === 'BOOKING_ACCEPTED' || type === 'BOOKING_REJECTED') {
    window.location.href = `my-bookings.html?bookingId=${bid}`;
    return;
  }

  // Nhóm 3: Khiếu nại (BOOKING_DISPUTED) - có thể gửi cho Sitter hoặc Admin
  if (type === 'BOOKING_DISPUTED') {
    if (currentRole === 'ADMIN') {
      window.location.href = `admin.html?bookingId=${bid}`;
    } else if (currentRole === 'SITTER') {
      window.location.href = `sitter-dashboard.html?bookingId=${bid}`;
    } else {
      window.location.href = `my-bookings.html?bookingId=${bid}`;
    }
    return;
  }

  // Nhóm 4: Các loại sự kiện chung cho cả 2 bên (COMPLETED, CANCELLED, EXPIRED, RESOLVED)
  if (type === 'BOOKING_COMPLETED' || type === 'BOOKING_CANCELLED' || type === 'BOOKING_EXPIRED' || type === 'BOOKING_RESOLVED') {
    if (currentRole === 'ADMIN') {
      window.location.href = `admin.html?bookingId=${bid}`;
    } else if (currentRole === 'SITTER') {
      window.location.href = `sitter-dashboard.html?bookingId=${bid}`;
    } else {
      window.location.href = `my-bookings.html?bookingId=${bid}`;
    }
    return;
  }

  // Mặc định an toàn fallback
  window.location.href = `my-bookings.html?bookingId=${bid}`;
}

function formatNotificationTime(isoStr) {
  if (!isoStr) return '';
  try {
    const d = new Date(isoStr);
    const now = new Date();
    const diffMs = now - d;
    const diffMins = Math.floor(diffMs / 60000);
    if (diffMins < 1) return 'Vừa xong';
    if (diffMins < 60) return `${diffMins} phút trước`;
    const diffHours = Math.floor(diffMins / 60);
    if (diffHours < 24) return `${diffHours} giờ trước`;
    const diffDays = Math.floor(diffHours / 24);
    if (diffDays < 7) return `${diffDays} ngày trước`;
    return d.toLocaleDateString('vi-VN');
  } catch (e) {
    return '';
  }
}

async function notifApiFetch(endpoint, options = {}) {
  if (typeof apiFetch === 'function') {
    return await apiFetch(endpoint, options);
  }
  const token = localStorage.getItem('pawstay_token');
  const headers = { 'Content-Type': 'application/json', ...(options.headers || {}) };
  if (token) headers['Authorization'] = 'Bearer ' + token;
  const res = await fetch('/api' + endpoint, { ...options, headers });
  if (res.status === 401) {
    if (typeof window.stopNotifPolling === 'function') {
      window.stopNotifPolling();
    }
    throw new Error('Unauthorized');
  }
  if (!res.ok) throw new Error('Request failed: ' + res.status);
  return await res.json();
}

function initNotificationSystem(user) {
  if (!user) return;

  // Không chạy polling thông báo trên trang login và signup
  const currentPath = window.location.pathname.toLowerCase();
  if (currentPath.endsWith('login.html') || currentPath.endsWith('signup.html')) {
    return;
  }

  const bellBtn = document.getElementById('notif-bell-btn');
  const badge = document.getElementById('notif-badge');
  const dropdown = document.getElementById('notif-dropdown');
  const list = document.getElementById('notif-list');
  const empty = document.getElementById('notif-empty');
  const markAllBtn = document.getElementById('notif-mark-all');
  const wrapper = document.getElementById('notif-wrapper');

  if (!bellBtn || !dropdown || !list) return;

  function setBadgeCount(count) {
    if (!badge) return;
    if (count > 0) {
      badge.textContent = count > 9 ? '9+' : String(count);
      badge.classList.remove('hidden');
      badge.classList.add('visible');
    } else {
      badge.textContent = '0';
      badge.classList.remove('visible');
      badge.classList.add('hidden');
    }
  }

  async function refreshUnreadCount() {
    try {
      const data = await notifApiFetch('/notifications/unread-count');
      if (data && typeof data.unreadCount === 'number') {
        setBadgeCount(data.unreadCount);
      }
    } catch (e) {
      // Bỏ qua lỗi kết nối khi poll
    }
  }

  async function loadNotificationsList() {
    try {
      const items = await notifApiFetch('/notifications?limit=20');
      while (list.firstChild) list.removeChild(list.firstChild);

      if (!items || items.length === 0) {
        if (empty) {
          empty.classList.remove('hidden');
          empty.classList.add('visible');
        }
        return;
      }

      if (empty) {
        empty.classList.remove('visible');
        empty.classList.add('hidden');
      }

      items.forEach(item => {
        const itemEl = document.createElement('div');
        itemEl.className = 'notif-item' + (!item.readAt ? ' unread' : '');

        const headerEl = document.createElement('div');
        headerEl.className = 'notif-item-header';

        const titleEl = document.createElement('span');
        titleEl.className = 'notif-item-title';
        titleEl.textContent = item.title || 'Thông báo';
        headerEl.appendChild(titleEl);

        if (!item.readAt) {
          const dotEl = document.createElement('span');
          dotEl.className = 'notif-unread-dot';
          headerEl.appendChild(dotEl);
        }

        const msgEl = document.createElement('div');
        msgEl.className = 'notif-item-msg';
        msgEl.textContent = item.message || '';

        const timeEl = document.createElement('div');
        timeEl.className = 'notif-item-time';
        timeEl.textContent = formatNotificationTime(item.createdAt);

        itemEl.appendChild(headerEl);
        itemEl.appendChild(msgEl);
        itemEl.appendChild(timeEl);

        itemEl.addEventListener('click', async () => {
          try {
            if (!item.readAt) {
              await notifApiFetch(`/notifications/${encodeURIComponent(item.notificationId)}/read`, { method: 'PATCH' });
              item.readAt = new Date().toISOString();
              itemEl.classList.remove('unread');
              const dot = headerEl.querySelector('.notif-unread-dot');
              if (dot) dot.remove();
              refreshUnreadCount();
            }
          } catch (err) {
            console.warn('Lỗi đánh dấu đã đọc:', err);
          }
          navigateForNotification(item);
        });

        list.appendChild(itemEl);
      });
    } catch (e) {
      console.warn('Lỗi tải danh sách thông báo:', e);
    }
  }

  bellBtn.addEventListener('click', function(e) {
    e.stopPropagation();
    // Đóng menu tài khoản nếu đang mở
    const accDropdown = document.getElementById('account-menu-dropdown');
    const accBtn = document.getElementById('account-menu-btn');
    if (accDropdown && accDropdown.classList.contains('open')) {
      accDropdown.classList.remove('open');
      accDropdown.classList.add('hidden');
      if (accBtn) accBtn.setAttribute('aria-expanded', 'false');
    }

    const isOpen = dropdown.classList.contains('open');
    if (isOpen) {
      dropdown.classList.remove('open');
      dropdown.classList.add('hidden');
      bellBtn.setAttribute('aria-expanded', 'false');
    } else {
      dropdown.classList.remove('hidden');
      dropdown.classList.add('open');
      bellBtn.setAttribute('aria-expanded', 'true');
      loadNotificationsList();
      refreshUnreadCount();
    }
  });

  if (markAllBtn) {
    markAllBtn.addEventListener('click', async function(e) {
      e.stopPropagation();
      try {
        await notifApiFetch('/notifications/read-all', { method: 'PATCH' });
        setBadgeCount(0);
        await loadNotificationsList();
      } catch (err) {
        console.warn('Lỗi đánh dấu đọc tất cả:', err);
      }
    });
  }

  // Đóng dropdown khi click bên ngoài
  document.addEventListener('click', function(e) {
    if (wrapper && !wrapper.contains(e.target)) {
      dropdown.classList.remove('open');
      dropdown.classList.add('hidden');
      bellBtn.setAttribute('aria-expanded', 'false');
    }
  });

  // Polling chu kỳ 60 giây và dừng khi ẩn tab
  function startPolling() {
    if (notifPollTimer) clearInterval(notifPollTimer);
    notifPollTimer = setInterval(refreshUnreadCount, 60000);
  }

  function stopPolling() {
    if (notifPollTimer) {
      clearInterval(notifPollTimer);
      notifPollTimer = null;
    }
  }

  window.stopNotifPolling = stopPolling;

  document.addEventListener('visibilitychange', function() {
    if (document.visibilityState === 'visible') {
      refreshUnreadCount();
      startPolling();
    } else {
      stopPolling();
    }
  });

  // Khởi động lượt kiểm tra đầu tiên
  refreshUnreadCount();
  startPolling();
}

// Global helper mở popover thông báo từ bất kỳ trang nào
window.openAllNotifications = function() {
  const bellBtn = document.getElementById('notif-bell-btn');
  const dropdown = document.getElementById('notif-dropdown');
  if (bellBtn) {
    if (dropdown && !dropdown.classList.contains('open')) {
      bellBtn.click();
    } else if (!dropdown) {
      bellBtn.click();
    }
    window.scrollTo({ top: 0, behavior: 'smooth' });
  } else {
    if (typeof showToast === 'function') {
      showToast('Thông báo đang hiển thị trên thanh menu chuông.', 2000, '🔔');
    }
  }
};

// ------------------------------------------------
// QUẢN LÝ DROPDOWN TÀI KHOẢN TRÊN HEADER
// ------------------------------------------------
function initAccountDropdown() {
  const btn = document.getElementById('account-menu-btn');
  const dropdown = document.getElementById('account-menu-dropdown');
  const wrapper = document.getElementById('account-menu-wrapper');
  if (!btn || !dropdown || !wrapper) return;

  btn.addEventListener('click', function(e) {
    e.stopPropagation();
    // Đóng dropdown thông báo nếu đang mở
    const notifDropdown = document.getElementById('notif-dropdown');
    const bellBtn = document.getElementById('notif-bell-btn');
    if (notifDropdown && notifDropdown.classList.contains('open')) {
      notifDropdown.classList.remove('open');
      notifDropdown.classList.add('hidden');
      if (bellBtn) bellBtn.setAttribute('aria-expanded', 'false');
    }

    const isOpen = dropdown.classList.contains('open');
    if (isOpen) {
      dropdown.classList.remove('open');
      dropdown.classList.add('hidden');
      btn.setAttribute('aria-expanded', 'false');
    } else {
      dropdown.classList.remove('hidden');
      dropdown.classList.add('open');
      btn.setAttribute('aria-expanded', 'true');
    }
  });

  // Đóng dropdown khi click ra ngoài
  document.addEventListener('click', function(e) {
    if (wrapper && !wrapper.contains(e.target)) {
      dropdown.classList.remove('open');
      dropdown.classList.add('hidden');
      btn.setAttribute('aria-expanded', 'false');
    }
  });

  // Đóng dropdown khi nhấn phím Escape
  document.addEventListener('keydown', function(e) {
    if (e.key === 'Escape') {
      dropdown.classList.remove('open');
      dropdown.classList.add('hidden');
      btn.setAttribute('aria-expanded', 'false');
    }
  });
}

// ------------------------------------------------
// QUẢN LÝ ĐĂNG XUẤT (LOGOUT) TOÀN CỤC
// ------------------------------------------------
async function logout(skipConfirm = false) {
  if (!skipConfirm) {
    const ok = confirm('Bạn có chắc chắn muốn đăng xuất tài khoản PawStay không?');
    if (!ok) return;
  }

  // Dừng polling thông báo
  if (typeof window.stopNotifPolling === 'function') {
    window.stopNotifPolling();
  }

  // Thu hồi refresh token qua API backend
  try {
    const token = localStorage.getItem('pawstay_token');
    const headers = {};
    if (token) headers['Authorization'] = 'Bearer ' + token;

    if (typeof apiFetch === 'function') {
      await apiFetch('/auth/logout', { method: 'POST', headers }).catch(() => {});
    } else {
      await fetch('/api/auth/logout', {
        method: 'POST',
        headers,
        credentials: 'include'
      }).catch(() => {});
    }
  } catch (e) {
    // Bỏ qua lỗi kết nối
  } finally {
    // Luôn luôn dọn sạch storage và chuyển hướng về login.html
    localStorage.removeItem('pawstay_token');
    localStorage.removeItem('pawstay_user');
    sessionStorage.clear();
    window.location.href = 'login.html';
  }
}

window.logout = logout;

// ------------------------------------------------
// BẢO VỆ TRANG RIÊNG TƯ KHI DÙNG NÚT BACK (BFCACHE)
// ------------------------------------------------
window.addEventListener('pageshow', function(event) {
  const privatePages = [
    'owner-dashboard.html',
    'sitter-dashboard.html',
    'admin.html',
    'admin-messages.html',
    'my-bookings.html',
    'my-pets.html',
    'profile.html',
    'stay-updates.html',
    'sitter-edit.html',
    'sitter-calendar.html'
  ];
  const currentPage = (window.location.pathname.split('/').pop() || '').toLowerCase();
  if (privatePages.includes(currentPage)) {
    if (!localStorage.getItem('pawstay_token')) {
      window.location.replace('login.html');
    }
  }
});

// ------------------------------------------------
// RENDER FOOTER
// ------------------------------------------------
function renderFooter() {
  // Xóa footer cũ nếu đã tồn tại
  const oldFooter = document.querySelector('.site-footer');
  if (oldFooter) oldFooter.remove();

  const footerHtml = `
    <footer class="site-footer">
      <div class="footer-grid">

        <!-- Cột 1: Thương hiệu -->
        <div class="footer-brand">
          <a href="index.html" class="logo">
            <div class="logo-icon">🐾</div>
            <span class="logo-text">Paw<span>Stay</span></span>
          </a>
          <p>
            Nền tảng kết nối chủ thú cưng với pet-sitter uy tín tại Đà Nẵng.
            Gửi thú cưng yên tâm, mỗi lần đi xa.
          </p>
          <!-- Social -->
          <div class="footer-social">
            <a href="https://facebook.com" class="social-btn" aria-label="Facebook">📘</a>
            <a href="https://zalo.me" class="social-btn" aria-label="Zalo">💬</a>
            <a href="https://instagram.com" class="social-btn" aria-label="Instagram">📷</a>
            <a href="https://tiktok.com" class="social-btn" aria-label="TikTok">🎵</a>
          </div>
        </div>

        <!-- Cột 2: Dịch vụ -->
        <div class="footer-col">
          <h4>Dịch vụ</h4>
          <ul>
            <li><a href="sitters.html">Tìm pet-sitter</a></li>
            <li><a href="become-sitter.html">Trở thành sitter</a></li>
            <li><a href="how-it-works.html">Cách hoạt động</a></li>
            <li><a href="booking.html">Đặt lịch</a></li>
          </ul>
        </div>

        <!-- Cột 3: Công ty -->
        <div class="footer-col">
          <h4>Công ty</h4>
          <ul>
            <li><a href="about.html">Về chúng tôi</a></li>
            <li><a href="contact.html">Liên hệ</a></li>
            <li><a href="about.html">Tuyển dụng</a></li>
            <li><a href="how-it-works.html">Blog</a></li>
          </ul>
        </div>

        <!-- Cột 4: Hỗ trợ -->
        <div class="footer-col">
          <h4>Hỗ trợ</h4>
          <ul>
            <li><a href="faq.html">Câu hỏi thường gặp</a></li>
            <li><a href="refund-policy.html">Chính sách hủy & thanh toán</a></li>
            <li><a href="terms.html">Điều khoản dịch vụ</a></li>
            <li><a href="privacy.html">Chính sách bảo mật</a></li>
          </ul>
          <!-- Hotline -->
          <div class="footer-hotline-wrap">
            <p class="footer-hotline-label">Hotline hỗ trợ (7 ngày/tuần)</p>
            <a href="tel:02363123456" class="footer-hotline-link">0236 312 3456</a>
          </div>
        </div>

      </div>

      <!-- Footer bottom -->
      <div class="footer-bottom">
        <p>© 2024 PawStay. Nền tảng kết nối chủ thú cưng & pet-sitter tại Đà Nẵng, Việt Nam.</p>
        <ul class="footer-bottom-links">
          <li><a href="terms.html">Điều khoản</a></li>
          <li><a href="privacy.html">Bảo mật</a></li>
          <li><a href="privacy.html">Cookie</a></li>
        </ul>
      </div>
    </footer>
  `;

  // Chèn footer vào cuối body
  document.body.insertAdjacentHTML('beforeend', footerHtml);
}

// ------------------------------------------------
// GLOBAL TOAST (dùng ở mọi trang)
// ------------------------------------------------
function renderToast() {
  const oldToast = document.getElementById('global-toast');
  if (oldToast) oldToast.remove();
  const toastHtml = `<div class="toast" id="global-toast" role="alert" aria-live="polite"></div>`;
  document.body.insertAdjacentHTML('beforeend', toastHtml);
}

// Override showToast để dùng toast vừa tạo
window.showToast = function(message, duration, icon) {
  const toast = document.getElementById('global-toast');
  if (!toast) return;
  const safeIcon = icon ? `<span class="toast-icon">${/* safe */ icon}</span>` : '';
  const safeMsg = (typeof window !== 'undefined' && typeof window.escapeHtml === 'function') 
    ? window.escapeHtml(message) 
    : message;
  toast.innerHTML = safeIcon + safeMsg;
  toast.classList.add('show');
  clearTimeout(toast._timeout);
  toast._timeout = setTimeout(() => toast.classList.remove('show'), duration || 3000);
};

// ------------------------------------------------
// SCROLL ANIMATION OBSERVER (dùng chung)
// ------------------------------------------------
function initScrollAnimations() {
  const observer = new IntersectionObserver(
    (entries) => entries.forEach(e => {
      if (e.isIntersecting) {
        e.target.classList.add('visible');
        observer.unobserve(e.target); // chỉ animate 1 lần
      }
    }),
    { threshold: 0.12 }
  );
  document.querySelectorAll('.animate-in').forEach(el => observer.observe(el));
}

// ------------------------------------------------
// STATS COUNTER ANIMATION
// ------------------------------------------------
function animateCounter(el, target, suffix, duration) {
  const start    = 0;
  const steps    = 60;
  const stepDur  = (duration || 1800) / steps;
  let   current  = 0;
  const increment = target / steps;

  const timer = setInterval(() => {
    current = Math.min(current + increment, target);
    el.textContent = Math.floor(current).toLocaleString('vi-VN') + (suffix || '');
    if (current >= target) clearInterval(timer);
  }, stepDur);
}

function initStatsCounters() {
  const counters = document.querySelectorAll('[data-count]');
  if (!counters.length) return;

  const observer = new IntersectionObserver((entries) => {
    entries.forEach(e => {
      if (e.isIntersecting) {
        const el     = e.target;
        const target = parseInt(el.dataset.count, 10);
        const suffix = el.dataset.suffix || '';
        animateCounter(el, target, suffix);
        observer.unobserve(el);
      }
    });
  }, { threshold: 0.5 });

  counters.forEach(el => observer.observe(el));
}

// ------------------------------------------------
// CAROUSEL HELPER
// ------------------------------------------------
function createCarousel(options) {
  const {
    trackId,     // ID của carousel-track element
    prevId,      // ID nút prev
    nextId,      // ID nút next
    dotsId,      // ID container dots
    itemsPerView,// Số item hiện mỗi lần (desktop)
    gap          // gap px
  } = options;

  const track = document.getElementById(trackId);
  if (!track) return;

  const items  = Array.from(track.children);
  const total  = items.length;
  let   current = 0;

  const gapPx = gap || 20;

  // Set item widths
  function setWidths() {
    const pv    = window.innerWidth <= 700 ? 1 : (itemsPerView || 3);
    const trackW = track.parentElement.offsetWidth;
    const itemW  = (trackW - gapPx * (pv - 1)) / pv;
    items.forEach(item => { item.style.width = itemW + 'px'; });
    return { pv, itemW };
  }

  let { pv, itemW } = setWidths();
  const maxSlide = Math.max(0, total - pv);

  // Render dots
  const dotsContainer = document.getElementById(dotsId);
  if (dotsContainer) {
    dotsContainer.innerHTML = '';
    const numDots = maxSlide + 1;
    for (let i = 0; i <= maxSlide; i++) {
      const dot = document.createElement('button');
      dot.className = 'carousel-dot' + (i === 0 ? ' active' : '');
      dot.setAttribute('aria-label', `Slide ${i+1}`);
      dot.addEventListener('click', () => goTo(i));
      dotsContainer.appendChild(dot);
    }
  }

  function goTo(index) {
    current = Math.max(0, Math.min(index, maxSlide));
    const offset = current * (itemW + gapPx);
    track.style.transform = `translateX(-${offset}px)`;

    // Update dots
    if (dotsContainer) {
      dotsContainer.querySelectorAll('.carousel-dot').forEach((dot, i) => {
        dot.classList.toggle('active', i === current);
      });
    }
  }

  // Buttons
  const prevBtn = document.getElementById(prevId);
  const nextBtn = document.getElementById(nextId);
  if (prevBtn) prevBtn.addEventListener('click', () => goTo(current - 1));
  if (nextBtn) nextBtn.addEventListener('click', () => goTo(current + 1));

  // Touch/swipe
  let touchStart = 0;
  track.addEventListener('touchstart', e => { touchStart = e.touches[0].clientX; }, { passive: true });
  track.addEventListener('touchend', e => {
    const diff = touchStart - e.changedTouches[0].clientX;
    if (Math.abs(diff) > 50) goTo(current + (diff > 0 ? 1 : -1));
  }, { passive: true });

  // Auto resize
  window.addEventListener('resize', () => {
    const res = setWidths();
    pv    = res.pv;
    itemW = res.itemW;
    goTo(Math.min(current, Math.max(0, total - pv)));
  });

  // Auto play (optional)
  let autoPlay = setInterval(() => goTo((current + 1) % (maxSlide + 1)), 4500);
  track.addEventListener('mouseenter', () => clearInterval(autoPlay));
  track.addEventListener('mouseleave', () => {
    autoPlay = setInterval(() => goTo((current + 1) % (maxSlide + 1)), 4500);
  });
}

// ------------------------------------------------
// AUTO-INIT: Chạy khi DOM sẵn sàng
// ------------------------------------------------
document.addEventListener('DOMContentLoaded', function() {
  renderNavbar();
  renderFooter();
  renderToast();
  initScrollAnimations();
  initStatsCounters();

  // Event Delegation cho nút Logout (CSP Clean)
  document.addEventListener('click', function(e) {
    const logoutBtn = e.target.closest('[data-action="logout"]');
    if (logoutBtn) {
      e.preventDefault();
      logout();
    }
  });

  // Kiểm tra và hiển thị toast từ trang trước (nếu có)
  const toastOnLoad = localStorage.getItem('pawstay_toast_on_load');
  if (toastOnLoad) {
    try {
      const data = JSON.parse(toastOnLoad);
      setTimeout(() => {
        if (window.showToast) {
          window.showToast(data.message, data.duration || 3500, data.icon || '🐾');
        }
      }, 400);
    } catch (e) {
      console.error("Error parsing toastOnLoad:", e);
    }
    localStorage.removeItem('pawstay_toast_on_load');
  }
});
