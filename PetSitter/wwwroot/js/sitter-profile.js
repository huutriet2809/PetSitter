let currentSitter = null;
let allReviews = [];
let currentReviewPage = 1;
const REVIEWS_PER_PAGE = 10;

let currentMonth = new Date();
currentMonth.setUTCDate(1);

document.addEventListener('DOMContentLoaded', async () => {
  const urlParams = new URLSearchParams(window.location.search);
  const sitterId = urlParams.get('id');
  if (!sitterId) {
    showError("Không tìm thấy mã sitter.");
    return;
  }
  try {
    currentSitter = await apiFetch(`/sitters/${encodeURIComponent(sitterId)}`);
    allReviews = await apiFetch(`/sitters/${encodeURIComponent(sitterId)}/reviews`);
    renderProfile();
  } catch (e) {
    showError(e.status === 404 ? "Sitter không tồn tại hoặc đã bị xóa." : "Lỗi tải thông tin.");
  }
});

function showError(msg) {
  const content = document.getElementById('content');
  if (!content) return;
  content.innerHTML = `
    <div class="error-state">
      <h2>Lỗi</h2>
      <p>${escapeHtml(msg)}</p>
      <a href="sitters.html" class="btn btn-primary mt-4">Tìm Sitter khác</a>
    </div>
  `;
}

function renderProfile() {
  const s = currentSitter;
  if (!s) return;

  const initials = typeof getInitials === 'function' ? getInitials(s.ten || "ST") : (s.ten ? s.ten.substring(0, 2) : 'ST');
  const avatar = (s.avatarUrl && (s.avatarUrl.startsWith("https://") || s.avatarUrl.startsWith("/")))
    ? `<img src="${escapeHtml(s.avatarUrl)}" class="profile-avatar" alt="${escapeHtml(s.ten)}">`
    : `<div class="profile-avatar">${escapeHtml(initials)}</div>`;

  const tags = (s.tags || []).map(t => `<span class="tag">${escapeHtml(t)}</span>`).join('');
  const basePrice = (s.dichVu && s.dichVu.length > 0) ? s.dichVu[0].gia : (s.giaMoiNgay || s.giaTu || 150000);

  const content = document.getElementById('content');
  if (!content) return;

  content.innerHTML = `
    <div class="profile-header">
      ${avatar}
      <div class="profile-info">
        <h1 id="sitter-name">${escapeHtml(s.ten)}</h1>
        <p>📍 ${escapeHtml(s.khuVuc || "Đà Nẵng")} | ⭐ ${escapeHtml(String(s.danhGia || 5.0))} (${escapeHtml(String(s.soDanhGia || 0))} đánh giá)</p>
        <div class="tags">${tags}</div>
      </div>
    </div>
    <div class="content-grid">
      <div class="left-col">
        <div class="section-card">
          <h3>Giới thiệu</h3>
          <p id="sitter-bio" class="sitter-bio-text">${escapeHtml(s.bio || "Chưa có giới thiệu.")}</p>
        </div>
        <div class="section-card">
          <h3>Đánh giá (${allReviews.length})</h3>
          <div id="reviews-container"></div>
          <div id="reviews-pagination" class="pagination"></div>
        </div>
      </div>
      <div class="right-col">
        <div class="section-card booking-card">
          <h2 class="price-tag">${typeof formatTien === 'function' ? formatTien(basePrice) : basePrice + ' ₫'} <span class="price-per-day">/ ngày</span></h2>
          <button class="btn btn-primary book-btn" id="btn-book-sitter">Đặt lịch ngay</button>
        </div>
        <div class="section-card">
          <h3>Dịch vụ bổ sung</h3>
          <ul class="services-list" id="services-list">
          </ul>
        </div>
        <div class="section-card">
          <div class="calendar-header">
            <button class="btn btn-ghost btn-sm" id="btn-cal-prev" aria-label="Tháng trước">◀</button>
            <h4 id="cal-month"></h4>
            <button class="btn btn-ghost btn-sm" id="btn-cal-next" aria-label="Tháng sau">▶</button>
          </div>
          <div class="calendar-grid">
            <div class="cal-day-header">CN</div><div class="cal-day-header">T2</div>
            <div class="cal-day-header">T3</div><div class="cal-day-header">T4</div>
            <div class="cal-day-header">T5</div><div class="cal-day-header">T6</div>
            <div class="cal-day-header">T7</div>
          </div>
          <div class="calendar-grid" id="cal-days"></div>
        </div>
      </div>
    </div>
  `;

  // Gắn event listener cho nút đặt lịch
  const bookBtn = document.getElementById('btn-book-sitter');
  if (bookBtn) {
    bookBtn.addEventListener('click', bookSitter);
  }

  // Gắn event listener cho điều hướng lịch
  const prevBtn = document.getElementById('btn-cal-prev');
  if (prevBtn) {
    prevBtn.addEventListener('click', () => changeMonth(-1));
  }
  const nextBtn = document.getElementById('btn-cal-next');
  if (nextBtn) {
    nextBtn.addEventListener('click', () => changeMonth(1));
  }

  // Render danh sách dịch vụ bổ sung
  const sList = document.getElementById('services-list');
  if (sList) {
    if (s.dichVu && s.dichVu.length > 0) {
      s.dichVu.forEach(srv => {
        const li = document.createElement('li');
        li.textContent = `${srv.ten} (+ ${typeof formatTien === 'function' ? formatTien(srv.gia) : srv.gia + ' ₫'})`;
        sList.appendChild(li);
      });
    } else {
      const li = document.createElement('li');
      li.className = 'muted-text';
      li.textContent = 'Không có dịch vụ phụ';
      sList.appendChild(li);
    }
  }

  renderReviews();
  loadCalendar();
}

function renderReviews() {
  const container = document.getElementById('reviews-container');
  const pContainer = document.getElementById('reviews-pagination');
  if (!container || !pContainer) return;
  container.innerHTML = '';
  pContainer.innerHTML = '';

  const totalPages = Math.ceil(allReviews.length / REVIEWS_PER_PAGE);
  if (allReviews.length === 0) {
    container.innerHTML = '<p class="no-reviews">Chưa có đánh giá nào.</p>';
    return;
  }

  const start = (currentReviewPage - 1) * REVIEWS_PER_PAGE;
  const end = start + REVIEWS_PER_PAGE;
  const pageReviews = allReviews.slice(start, end);

  pageReviews.forEach(r => {
    const div = document.createElement('div');
    div.className = 'review-item';
    const ratingCount = Math.max(1, Math.min(5, parseInt(r.rating, 10) || 5));
    const ratingHtml = '⭐'.repeat(ratingCount);
    const dateStr = r.ngay ? new Date(r.ngay).toLocaleDateString('vi-VN') : '';

    div.innerHTML = `
      <div class="review-meta">
        <strong>${escapeHtml(r.nguoiDung || 'Khách hàng')}</strong>
        <span>${escapeHtml(dateStr)}</span>
      </div>
      <div class="rating-stars">${ratingHtml}</div>
      <p class="review-comment">${escapeHtml(r.comment || r.moTa || '')}</p>
    `;
    container.appendChild(div);
  });

  if (totalPages > 1) {
    for (let i = 1; i <= totalPages; i++) {
      const btn = document.createElement('button');
      btn.className = 'btn btn-sm ' + (i === currentReviewPage ? 'btn-primary' : 'btn-ghost');
      btn.textContent = i;
      btn.addEventListener('click', () => {
        currentReviewPage = i;
        renderReviews();
      });
      pContainer.appendChild(btn);
    }
  }
}

async function loadCalendar() {
  const y = currentMonth.getUTCFullYear();
  const m = (currentMonth.getUTCMonth() + 1).toString().padStart(2, '0');
  const monthEl = document.getElementById('cal-month');
  if (monthEl) monthEl.textContent = m + '/' + y;

  const daysContainer = document.getElementById('cal-days');
  if (!daysContainer) return;
  daysContainer.innerHTML = '<div class="cal-loading">Đang tải...</div>';

  try {
    const sitterId = currentSitter.sitterId || currentSitter.id;
    const availabilities = await apiFetch(`/sitters/${encodeURIComponent(sitterId)}/availability?month=${y}-${m}`);
    const availSet = new Set(availabilities || []);

    daysContainer.innerHTML = '';
    const firstDay = new Date(Date.UTC(y, currentMonth.getUTCMonth(), 1)).getUTCDay();
    const daysInMonth = new Date(Date.UTC(y, currentMonth.getUTCMonth() + 1, 0)).getUTCDate();

    for (let i = 0; i < firstDay; i++) {
      const emptyDiv = document.createElement('div');
      daysContainer.appendChild(emptyDiv);
    }
    for (let i = 1; i <= daysInMonth; i++) {
      const dateStr = `${y}-${m}-${i.toString().padStart(2, '0')}`;
      const isAvail = availSet.has(dateStr);
      const dayDiv = document.createElement('div');
      dayDiv.className = 'cal-day' + (isAvail ? ' available' : '');
      dayDiv.textContent = i;
      daysContainer.appendChild(dayDiv);
    }
  } catch (e) {
    daysContainer.innerHTML = '<div class="cal-error">Lỗi tải lịch</div>';
  }
}

function changeMonth(delta) {
  currentMonth.setUTCMonth(currentMonth.getUTCMonth() + delta);
  loadCalendar();
}

function bookSitter() {
  if (!currentSitter) return;
  const sitterId = currentSitter.sitterId || currentSitter.id;
  localStorage.setItem('pawstay_selected_sitter', sitterId);
  if (typeof isLoggedIn === 'function' && !isLoggedIn()) {
    const returnUrl = encodeURIComponent(`booking.html?sitter=${sitterId}`);
    window.location.href = `login.html?returnUrl=${returnUrl}`;
  } else {
    window.location.href = `booking.html?sitter=${sitterId}`;
  }
}

