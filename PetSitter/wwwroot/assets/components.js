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

// ------------------------------------------------
// NAV LINK DATA — Chỉnh ở đây để thay đổi menu
// ------------------------------------------------
const NAV_LINKS = [
  { href: 'index.html',        label: 'Trang chủ' },
  { href: 'sitters.html',      label: 'Tìm Sitter' },
  { href: 'become-sitter.html',label: 'Trở thành Sitter' },
  { href: 'how-it-works.html', label: 'Cách hoạt động' },
  { href: 'about.html',        label: 'Về chúng tôi' },
  { href: 'contact.html',      label: 'Liên hệ' },
];

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

  // Tạo nav links HTML
  const navLinksHtml = NAV_LINKS.map(link => {
    const isActive = currentPage === link.href ? 'active' : '';
    return `<li><a href="${/* safe */ link.href}" class="${/* safe */ isActive}">${/* safe */ link.label}</a></li>`;
  }).join('');

  // Mobile nav links
  const mobileLinksHtml = NAV_LINKS.map(link => {
    const isActive = currentPage === link.href ? 'active' : '';
    return `<a href="${/* safe */ link.href}" class="${/* safe */ isActive}">${/* safe */ link.label}</a>`;
  }).join('');

  let shortName = 'Bạn';
  let roleLinks = [];

  if (user) {
    const rawName = (user.fullName || user.ten || user.email || 'Bạn').trim();
    shortName = rawName.split(/\s+/).pop() || rawName;
    const role = (user.role || '').toUpperCase();

    if (role === 'ADMIN') {
      roleLinks = [
        { href: 'admin.html', label: 'Quản trị' },
        { href: 'admin-messages.html', label: 'Tin nhắn' },
        { href: 'profile.html', label: 'Tài khoản' }
      ];
    } else if (role === 'SITTER') {
      roleLinks = [
        { href: 'sitter-dashboard.html', label: 'Bảng điều khiển' },
        { href: 'sitter-profile.html', label: 'Hồ sơ Sitter' },
        { href: 'sitter-calendar.html', label: 'Lịch rảnh' },
        { href: 'profile.html', label: 'Tài khoản' }
      ];
    } else {
      // USER
      roleLinks = [
        { href: 'my-bookings.html', label: 'Lịch của tôi' },
        { href: 'my-pets.html', label: 'Thú cưng của tôi' },
        { href: 'profile.html', label: 'Tài khoản' },
        { href: 'become-sitter.html', label: 'Trở thành Sitter' }
      ];
    }
  }

  const safeShortName = (typeof window !== 'undefined' && typeof window.escapeHtml === 'function') 
    ? window.escapeHtml(shortName) 
    : shortName;

  const authHtml = user
    ? `<span style="font-size:0.82rem;color:var(--text-secondary)">Xin chào, <strong style="color:var(--accent)">${/* safe */ safeShortName}</strong></span>
       ${roleLinks.map(l => `<a href="${/* safe */ l.href}" class="btn btn-ghost btn-sm">${/* safe */ l.label}</a>`).join(' ')}
       <button onclick="logout()" class="btn btn-ghost btn-sm">Đăng xuất</button>`
    : `<a href="login.html" class="btn btn-ghost btn-sm" id="nav-login">Đăng nhập</a>
       <a href="signup.html" class="btn btn-primary btn-sm" id="nav-signup">Đăng ký</a>`;

  // Mobile auth
  const mobileAuthHtml = user
    ? `${roleLinks.map(l => `<a href="${/* safe */ l.href}" class="btn btn-ghost btn-sm">${/* safe */ l.label}</a>`).join('')}
       <button onclick="logout()" class="btn btn-ghost btn-sm">Đăng xuất</button>`
    : `<a href="login.html" class="btn btn-ghost btn-sm">Đăng nhập</a>
       <a href="signup.html" class="btn btn-primary btn-sm">Đăng ký</a>`;

  const navbarHtml = `
    <nav class="navbar" id="navbar" role="navigation" aria-label="Điều hướng chính">
      <div class="navbar-inner">
        <!-- Logo -->
        <a href="index.html" class="logo" aria-label="PawStay - Trang chủ">
          <div class="logo-icon" aria-hidden="true">🐾</div>
          <span class="logo-text">Paw<span>Stay</span></span>
        </a>

        <!-- Nav Links (Desktop) -->
        <ul class="nav-center" role="list">
          ${/* safe */ navLinksHtml}
        </ul>

        <!-- Auth buttons (Desktop) -->
        <div class="nav-right">
          ${/* safe */ authHtml}
        </div>

        <!-- Mobile toggle -->
        <button class="nav-toggle" id="nav-toggle" aria-label="Mở menu" aria-expanded="false">
          <span id="nav-toggle-icon">☰</span>
        </button>
      </div>
    </nav>

    <!-- Mobile drawer -->
    <div class="mobile-nav" id="mobile-nav" role="dialog" aria-label="Menu di động">
      ${/* safe */ mobileLinksHtml}
      <div class="mobile-nav-divider"></div>
      <div class="mobile-nav-auth">
        ${/* safe */ mobileAuthHtml}
      </div>
    </div>
  `;

  // Chèn navbar vào đầu body
  document.body.insertAdjacentHTML('afterbegin', navbarHtml);

  // Gắn sự kiện mobile toggle
  const toggleBtn = document.getElementById('nav-toggle');
  if (toggleBtn) {
    toggleBtn.addEventListener('click', function() {
      const mobileNav  = document.getElementById('mobile-nav');
      const toggleIcon = document.getElementById('nav-toggle-icon');
      if (!mobileNav || !toggleIcon) return;
      const isOpen     = mobileNav.classList.toggle('open');
      this.setAttribute('aria-expanded', isOpen);
      toggleIcon.textContent = isOpen ? '✕' : '☰';
    });
  }

  // Đóng mobile nav khi click ra ngoài
  document.addEventListener('click', function(e) {
    const mobileNav = document.getElementById('mobile-nav');
    const toggle    = document.getElementById('nav-toggle');
    if (mobileNav && mobileNav.classList.contains('open') &&
        !mobileNav.contains(e.target) && !toggle.contains(e.target)) {
      mobileNav.classList.remove('open');
      const toggleIcon = document.getElementById('nav-toggle-icon');
      if (toggleIcon) toggleIcon.textContent = '☰';
    }
  });
}

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
          <div style="margin-top:16px;">
            <p style="font-size:0.75rem;color:var(--text-muted);margin-bottom:4px;">Hotline hỗ trợ (7 ngày/tuần)</p>
            <a href="tel:02363123456" style="font-size:1rem;font-weight:700;color:var(--accent);">0236 312 3456</a>
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
