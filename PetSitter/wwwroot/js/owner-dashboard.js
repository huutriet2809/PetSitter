window.addEventListener('pageshow', () => {
  if (!localStorage.getItem('pawstay_token')) {
    window.location.replace('login.html');
  }
});

document.addEventListener('DOMContentLoaded', async () => {
  if (!localStorage.getItem('pawstay_token')) {
    window.location.href = 'login.html';
    return;
  }

  // Delegation cho mở thông báo
  document.addEventListener('click', (e) => {
    const notifBtn = e.target.closest('[data-action="open-notifications"]');
    if (notifBtn) {
      openAllNotifications();
    }
  });

  const user = typeof getCurrentUser === 'function' ? getCurrentUser() : JSON.parse(localStorage.getItem('pawstay_user') || 'null');
  const role = (user && user.role ? user.role : '').toUpperCase();
  if (role === 'ADMIN') {
    window.location.href = 'admin.html';
    return;
  }
  if (role === 'SITTER') {
    window.location.href = 'sitter-dashboard.html';
    return;
  }

  if (user) {
    const rawName = (user.fullName || user.ten || user.email || 'Bạn').trim();
    const nameEl = document.getElementById('owner-name');
    if (nameEl) nameEl.textContent = rawName;
  }

  await loadDashboardData();
});

async function loadDashboardData() {
  try {
    // Tải đồng thời bookings, pets và notifications
    const [bookings, pets, notifs] = await Promise.all([
      apiFetch('/bookings/my').catch(() => []),
      apiFetch('/pets').catch(() => []),
      apiFetch('/notifications?limit=5').catch(() => [])
    ]);

    renderMetrics(bookings, pets);
    renderActiveStayBanner(bookings);
    renderRecentBookings(bookings);
    renderPets(pets);
    renderNotifications(notifs);
  } catch (err) {
    console.error('Lỗi tải dữ liệu dashboard:', err);
  }
}

function renderMetrics(bookings, pets) {
  const nowMs = Date.now();
  const vnTodayStr = new Date(nowMs + 7 * 3600000).toISOString().split('T')[0];

  let upcomingCount = 0;
  let activeCount = 0;
  let pendingPaymentCount = 0;

  (bookings || []).forEach(b => {
    // Loại trừ tuyệt đối các đơn đã hủy, hết hạn hoặc từ chối
    if (b.trangThai === 'CANCELLED' || b.trangThai === 'EXPIRED' || b.trangThai === 'REJECTED') {
      return;
    }

    const startVnStr = new Date(new Date(b.ngayBatDau).getTime() + 7 * 3600000).toISOString().split('T')[0];
    const endVnStr = new Date(new Date(b.ngayKetThuc).getTime() + 7 * 3600000).toISOString().split('T')[0];

    // Đơn chờ thanh toán
    if (b.paymentStatus === 'PENDING_PAYMENT') {
      pendingPaymentCount++;
    }

    // Đơn đã chấp nhận
    if (b.trangThai === 'ACCEPTED') {
      if (startVnStr > vnTodayStr) {
        upcomingCount++;
      } else if (startVnStr <= vnTodayStr && endVnStr >= vnTodayStr) {
        activeCount++;
      }
    } else if (b.trangThai === 'COMPLETION_PENDING') {
      // COMPLETION_PENDING chỉ tính vào Đang gửi trông
      activeCount++;
    }
  });

  const upEl = document.getElementById('metric-upcoming');
  const actEl = document.getElementById('metric-active');
  const payEl = document.getElementById('metric-pending-payment');
  const petEl = document.getElementById('metric-pets');

  if (upEl) upEl.textContent = upcomingCount;
  if (actEl) actEl.textContent = activeCount;
  if (payEl) payEl.textContent = pendingPaymentCount;
  if (petEl) petEl.textContent = (pets || []).length;
}

function renderActiveStayBanner(bookings) {
  const bannerContainer = document.getElementById('active-stay-section');
  if (!bannerContainer) return;
  const nowMs = Date.now();
  const vnTodayStr = new Date(nowMs + 7 * 3600000).toISOString().split('T')[0];

  // Tìm đơn đang lưu trú
  const activeBooking = (bookings || []).find(b => {
    const startVnStr = new Date(new Date(b.ngayBatDau).getTime() + 7 * 3600000).toISOString().split('T')[0];
    const endVnStr = new Date(new Date(b.ngayKetThuc).getTime() + 7 * 3600000).toISOString().split('T')[0];
    return (b.trangThai === 'ACCEPTED' && b.paymentStatus === 'HELD' && startVnStr <= vnTodayStr && endVnStr >= vnTodayStr) 
        || b.trangThai === 'COMPLETION_PENDING';
  });

  if (!activeBooking) {
    bannerContainer.classList.add('hidden');
    bannerContainer.innerHTML = '';
    return;
  }

  const isPendingConfirm = activeBooking.trangThai === 'COMPLETION_PENDING';
  bannerContainer.classList.remove('hidden');
  bannerContainer.innerHTML = `
    <div class="active-stay-banner">
      <div class="active-stay-info">
        <div class="active-stay-icon">🐾</div>
        <div>
          <div class="active-stay-tag">
            ${isPendingConfirm ? '⏳ CHỜ BẠN XÁC NHẬN HOÀN TẤT' : '🏡 ĐANG GỬI CHĂM SÓC'}
          </div>
          <h3 class="active-stay-title">
            Bé <strong>${escapeHtml(activeBooking.pet?.tenPet || 'Thú cưng')}</strong> đang ở cùng Sitter <strong>${escapeHtml(activeBooking.sitterName)}</strong>
          </h3>
          <p class="active-stay-time">
            Thời gian: ${formatNgay(activeBooking.ngayBatDau)} - ${formatNgay(activeBooking.ngayKetThuc)} • Đơn #${escapeHtml(activeBooking.maDatLich)}
          </p>
        </div>
      </div>
      <div class="active-stay-actions">
        <a href="stay-updates.html?bookingId=${/* safe */ activeBooking.bookingId}" class="btn btn-primary btn-sm">📷 Xem nhật ký lưu trú</a>
        ${isPendingConfirm ? `<a href="my-bookings.html?bookingId=${/* safe */ activeBooking.bookingId}" class="btn btn-outline btn-sm">Xác nhận hoàn tất</a>` : ''}
      </div>
    </div>
  `;
}

function renderRecentBookings(bookings) {
  const container = document.getElementById('recent-bookings-list');
  if (!container) return;
  if (!bookings || bookings.length === 0) {
    container.innerHTML = `
      <div class="empty-box">
        <p>Bạn chưa có lịch đặt nào.</p>
        <a href="sitters.html" class="btn btn-outline btn-sm btn-mt-sm">Tìm người chăm sóc ngay</a>
      </div>
    `;
    return;
  }

  // Lấy tối đa 5 đơn gần nhất
  const top5 = bookings.slice(0, 5);
  container.innerHTML = top5.map(b => {
    let statusBadge = getStatusBadgeHtml(b.trangThai);
    let paymentBadge = getPaymentBadgeHtml(b.paymentStatus);

    let actionBtn = '';
    if (b.paymentStatus === 'PENDING_PAYMENT') {
      actionBtn = `<a href="my-bookings.html?bookingId=${/* safe */ b.bookingId}" class="btn btn-primary btn-sm btn-pay-green">💳 Thanh toán</a>`;
    } else if (b.trangThai === 'ACCEPTED' || b.trangThai === 'COMPLETION_PENDING') {
      actionBtn = `<a href="stay-updates.html?bookingId=${/* safe */ b.bookingId}" class="btn btn-outline btn-sm">Nhật ký</a>`;
    }

    return `
      <div class="mini-booking-item">
        <div class="mini-booking-head">
          <div>
            <strong>Đơn #${escapeHtml(b.maDatLich)}</strong> • <a href="sitter-profile.html?id=${/* safe */ b.sitterId}" class="sitter-name-link">${escapeHtml(b.sitterName)}</a>
          </div>
          <div class="header-badges-wrap">
            ${statusBadge}
            ${paymentBadge}
          </div>
        </div>
        <div class="mini-booking-body">
          <div>
            <span>🐶 Thú cưng:</span> <strong>${escapeHtml(b.pet?.tenPet || 'Chưa cập nhật')}</strong>
          </div>
          <div>
            <span>💰 Tổng tiền:</span> <strong>${formatTien(b.tongTien)}</strong>
          </div>
          <div class="grid-col-full">
            <span>🗓️ Thời gian:</span> ${formatNgay(b.ngayBatDau)} - ${formatNgay(b.ngayKetThuc)}
          </div>
        </div>
        <div class="mini-booking-actions">
          ${actionBtn}
          <a href="my-bookings.html?bookingId=${/* safe */ b.bookingId}" class="btn btn-ghost btn-sm">Chi tiết →</a>
        </div>
      </div>
    `;
  }).join('');
}

function renderPets(pets) {
  const container = document.getElementById('my-pets-list');
  if (!container) return;
  if (!pets || pets.length === 0) {
    container.innerHTML = `
      <div class="empty-box">
        <p>Bạn chưa lưu thông tin thú cưng nào.</p>
        <a href="my-pets.html" class="btn btn-outline btn-sm btn-mt-sm">+ Thêm thú cưng</a>
      </div>
    `;
    return;
  }

  container.innerHTML = pets.slice(0, 3).map(p => `
    <div class="mini-pet-item">
      <div class="mini-pet-avatar">
        ${p.species === 'Mèo' ? '🐱' : '🐶'}
      </div>
      <div class="flex-1">
        <strong class="text-primary-bold">${escapeHtml(p.name)}</strong>
        <div class="pet-subtext">
          ${escapeHtml(p.species || '')} ${p.breed ? '• ' + escapeHtml(p.breed) : ''} ${p.weight ? '• ' + p.weight + 'kg' : ''}
        </div>
      </div>
    </div>
  `).join('');
}

function renderNotifications(notifs) {
  const container = document.getElementById('recent-notifications-list');
  if (!container) return;
  if (!notifs || notifs.length === 0) {
    container.innerHTML = `<div class="empty-box">Không có thông báo mới.</div>`;
    return;
  }

  container.innerHTML = notifs.map(n => `
    <div class="mini-notif-item">
      <div class="mini-notif-title">${escapeHtml(n.title)}</div>
      <div class="notif-message">${escapeHtml(n.message)}</div>
      <div class="mini-notif-time">${formatThoiGian(n.createdAt)}</div>
    </div>
  `).join('');
}

function formatNgay(dateStr) {
  if (!dateStr) return '';
  const d = new Date(dateStr);
  return `${d.getDate()}/${d.getMonth()+1}/${d.getFullYear()}`;
}

function formatThoiGian(isoStr) {
  if (!isoStr) return '';
  const d = new Date(isoStr);
  return d.toLocaleDateString('vi-VN') + ' ' + d.toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' });
}

function getStatusBadgeHtml(status) {
  const map = {
    'PENDING': { label: 'Chờ duyệt', cls: 'badge-status-pending' },
    'ACCEPTED': { label: 'Đã nhận', cls: 'badge-status-accepted' },
    'COMPLETION_PENDING': { label: 'Chờ hoàn tất', cls: 'badge-status-comp-pending' },
    'COMPLETED': { label: 'Hoàn thành', cls: 'badge-status-completed' },
    'DISPUTED': { label: 'Tranh chấp', cls: 'badge-status-disputed' },
    'CANCELLED': { label: 'Đã hủy', cls: 'badge-status-cancelled' }
  };
  const cfg = map[status] || { label: status, cls: 'badge-status-default' };
  return `<span class="badge-status-generic ${cfg.cls}">${escapeHtml(cfg.label)}</span>`;
}

function getPaymentBadgeHtml(status) {
  if (status === 'PENDING_PAYMENT') {
    return `<span class="badge-pay-pending">Chờ thanh toán</span>`;
  } else if (status === 'HELD') {
    return `<span class="badge-pay-held">Đã giữ cọc</span>`;
  } else if (status === 'REFUND_PENDING') {
    return `<span class="badge-pay-refund-pending">Chờ hoàn tiền</span>`;
  }
  return '';
}

function openAllNotifications() {
  const bell = document.getElementById('notif-bell-btn');
  if (bell) {
    bell.click();
    window.scrollTo({ top: 0, behavior: 'smooth' });
  } else if (typeof showToast === 'function') {
    showToast('Tất cả thông báo đang hiển thị trên thanh menu chuông.', 2000, '🔔');
  }
}

