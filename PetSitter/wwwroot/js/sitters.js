// Hàm tạo HTML cho một sitter card
function taoSitterCard(sitter) {
  const initials = typeof getInitials === 'function' ? getInitials(sitter.ten) : (sitter.ten ? sitter.ten.substring(0, 2) : 'PS');
  const ratingHtml = typeof renderSao === 'function' ? renderSao(sitter.danhGia) : '';

  // Render các loại pet hỗ trợ
  const petTags = (sitter.tags || []).map(p => {
    const icon = p === 'Chó' ? '🐶' : p === 'Mèo' ? '🐱' : '🐾';
    return `<span class="pet-tag">${icon} ${escapeHtml(p)}</span>`;
  }).join('');

  const hasAvatar = sitter.avatarUrl && (sitter.avatarUrl.startsWith("https://") || sitter.avatarUrl.startsWith("/"));
  const avatarHtml = hasAvatar
    ? `<img src="${escapeHtml(sitter.avatarUrl)}" class="sitter-avatar-img" alt="${escapeHtml(sitter.ten)}">`
    : `<div class="avatar-placeholder avatar-placeholder-bg">${escapeHtml(initials)}</div>`;

  const verifiedHtml = sitter.noiBat
    ? '<div class="verified-overlay"><span class="badge badge-verified">✓ Đã xác minh</span></div>'
    : '';

  return `
    <div class="sitter-card" id="sitter-${escapeHtml(sitter.sitterId)}" data-sitter-id="${escapeHtml(sitter.sitterId)}" data-area="${escapeHtml(sitter.khuVuc || '')}" role="article" aria-label="Sitter: ${escapeHtml(sitter.ten)}">
      <!-- Ảnh đại diện -->
      <div class="sitter-avatar-wrap">
        ${avatarHtml}
        <div class="avatar-gradient"></div>
        ${verifiedHtml}
      </div>

      <!-- Thông tin sitter -->
      <div class="sitter-info">
        <div class="sitter-name">${escapeHtml(sitter.ten)}</div>
        <div class="sitter-area"><span>📍 </span><span>${escapeHtml(sitter.khuVuc || '')}, Đà Nẵng</span></div>

        <div class="sitter-meta">
          ${ratingHtml}
          <span class="sitter-rating-text">${escapeHtml(String(sitter.danhGia || 0))}</span>
          <span class="sitter-trips">✓ ${escapeHtml(String(sitter.soDanhGia || 0))} lượt nhận</span>
        </div>

        <p class="sitter-desc">${escapeHtml(sitter.moTaNgan || '')}</p>
        <div class="sitter-pets">${petTags}</div>

        <div class="sitter-footer">
          <div class="sitter-price">
            <span class="price-amount">${typeof formatTien === 'function' ? formatTien(sitter.giaTu) : sitter.giaTu + ' ₫'}</span>
            <span class="price-unit">mỗi ngày</span>
          </div>
          <button class="btn btn-primary btn-sm" data-action="book" data-sitter-id="${escapeHtml(sitter.sitterId)}" id="book-btn-${escapeHtml(sitter.sitterId)}">
            Đặt lịch
          </button>
        </div>
      </div>
    </div>
  `;
}

// Hàm render tất cả sitter lên grid
function renderSitters(danhSach) {
  const grid = document.getElementById('sitters-grid');
  const countEl = document.getElementById('results-count');
  if (!grid) return;

  if (!danhSach || danhSach.length === 0) {
    grid.innerHTML = `
      <div class="empty-state">
        <div class="empty-state-icon">🔍</div>
        <p class="empty-state-text">Chưa có sitter ở khu vực này.<br/>Vui lòng chọn khu vực khác.</p>
      </div>
    `;
    if (countEl) countEl.textContent = '0 sitter';
  } else {
    grid.innerHTML = danhSach.map(taoSitterCard).join('');
    if (countEl) countEl.textContent = `${danhSach.length} sitter`;
  }
}

// Hàm xử lý khi click nút "Đặt lịch"
function datLich(sitterId) {
  localStorage.setItem('pawstay_selected_sitter', sitterId);
  window.location.href = 'booking.html';
}

// Hàm lọc theo khu vực
async function loc(area) {
  try {
    let url = '/sitters';
    if (area && area !== 'all') {
      url += '?khuVuc=' + encodeURIComponent(area);
    }
    const data = await apiFetch(url);
    renderSitters(data);
  } catch (err) {
    console.error(err);
  }
}

document.addEventListener('DOMContentLoaded', function() {
  const grid = document.getElementById('sitters-grid');

  // Event delegation cho sitter card và nút đặt lịch
  if (grid) {
    grid.addEventListener('click', function(e) {
      const bookBtn = e.target.closest('[data-action="book"]');
      if (bookBtn) {
        e.stopPropagation();
        const sitterId = bookBtn.getAttribute('data-sitter-id');
        if (sitterId) datLich(sitterId);
        return;
      }

      const card = e.target.closest('.sitter-card');
      if (card) {
        const sitterId = card.getAttribute('data-sitter-id');
        if (sitterId) {
          window.location.href = 'sitter-profile.html?id=' + encodeURIComponent(sitterId);
        }
      }
    });
  }

  // Gắn sự kiện cho các nút filter
  document.querySelectorAll('.filter-btn').forEach(btn => {
    btn.addEventListener('click', function() {
      document.querySelectorAll('.filter-btn').forEach(b => b.classList.remove('active'));
      this.classList.add('active');
      loc(this.dataset.area);
    });
  });

  // Khởi tạo: đọc ?area= từ URL khi trang load
  const urlParams = new URLSearchParams(window.location.search);
  const initialArea = urlParams.get('area') || 'all';

  let matchedBtn = null;
  document.querySelectorAll('.filter-btn').forEach(b => {
    b.classList.remove('active');
    if (b.dataset.area === initialArea) {
      matchedBtn = b;
    }
  });

  if (matchedBtn) {
    matchedBtn.classList.add('active');
  } else {
    const allBtn = document.querySelector('.filter-btn[data-area="all"]');
    if (allBtn) allBtn.classList.add('active');
  }

  loc(initialArea);
});

