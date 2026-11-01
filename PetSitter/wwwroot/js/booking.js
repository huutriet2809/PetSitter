// ────────────────────────────────────────────────────────────
// ĐỌC THÔNG TIN SITTER VÀ THIẾT LẬP BAN ĐẦU
// ────────────────────────────────────────────────────────────
let selectedSitter = null;
let selectedAddons = []; // Lưu danh sách addon ID đã chọn
let ADDONS = [];
let currentQuote = null;
let mySavedPets = [];

document.addEventListener('DOMContentLoaded', async function() {
  if (!localStorage.getItem('pawstay_token')) {
    window.location.href = 'login.html';
    return;
  }
  const urlParams = new URLSearchParams(window.location.search);
  const sitterId = urlParams.get('sitter') || urlParams.get('id') || localStorage.getItem('pawstay_selected_sitter');
  const container = document.getElementById('booking-content-container');

  if (sitterId) {
    localStorage.setItem('pawstay_selected_sitter', sitterId);
    try {
      selectedSitter = await apiFetch(`/sitters/${sitterId}`);
      // Fetch addons
      ADDONS = await apiFetch('/services');
      renderBookingPage(container, selectedSitter);
      initFormEvents();
    } catch (e) {
      renderNoSitterSelected(container);
    }
  } else {
    renderNoSitterSelected(container);
  }
});

// Render giao diện khi không có sitter nào được chọn
function renderNoSitterSelected(container) {
  container.innerHTML = `
    <div class="empty-state animate-in">
      <div class="empty-state-icon">⚠️</div>
      <h2>Bạn chưa chọn Pet-sitter</h2>
      <p>Để tiếp tục đặt lịch, vui lòng chọn một pet-sitter phù hợp từ danh sách của chúng tôi.</p>
      <a href="sitters.html" class="btn btn-primary btn-lg">
        🔍 Tìm Pet-sitter ngay
      </a>
    </div>
  `;
}

// Render giao diện form đặt lịch đầy đủ
function renderBookingPage(container, sitter) {
  const initials = getInitials(sitter.ten);
  const baseDailyPrice = (sitter.dichVu && sitter.dichVu.length > 0)
    ? (sitter.dichVu.find(s => (s.gia || s.price) > 0)?.gia || sitter.dichVu.find(s => (s.gia || s.price) > 0)?.price || sitter.giaTu || 0)
    : (sitter.giaTu || 0);
  
  // Render các add-ons
  const addonsHtml = ADDONS.map(addon => {
    const priceSuffix = (addon.id === 'anh-ngay' || addon.id === 'giu-thuoc') ? ' / ngày' : '';
    return `
      <div class="addon-card" data-id="${/* safe */ addon.id}">
        <div class="addon-checkbox" aria-hidden="true">✓</div>
        <div class="addon-icon">${/* safe */ addon.icon}</div>
        <div class="addon-info">
          <div class="addon-title-row">
            <span class="addon-title">${escapeHtml(addon.ten)}</span>
            <span class="addon-price">+${formatTien(addon.gia)}${/* safe */ priceSuffix}</span>
          </div>
          <p class="addon-desc">${escapeHtml(addon.moTa)}</p>
        </div>
      </div>
    `;
  }).join('');

  // Render danh sách dịch vụ chính của Sitter (Item 5a)
  const availableServices = (sitter.dichVu || []).filter(s => (s.gia || s.price) > 0);
  let primaryServicesOptionsHtml = '';
  if (availableServices.length > 0) {
    primaryServicesOptionsHtml = availableServices.map(s => {
      const sId = s.serviceId || s.id || '';
      const sName = s.ten || s.serviceName || s.name || 'Dịch vụ';
      const sPrice = s.gia || s.price || 0;
      return `<option value="${escapeHtml(sId)}">${escapeHtml(sName)} — ${formatTien(sPrice)} / ngày</option>`;
    }).join('');
  } else {
    primaryServicesOptionsHtml = `<option value="">Dịch vụ trông giữ chính — ${formatTien(sitter.giaTu || 0)} / ngày</option>`;
  }

  container.innerHTML = `
    <div class="booking-header">
      <p class="section-label">Hoàn tất đặt lịch</p>
      <h1 class="section-title booking-title">Thông tin đặt lịch của bạn</h1>
      <div class="section-divider"></div>
    </div>

    <form class="booking-grid" id="booking-form" novalidate>
      <!-- Cột bên trái: FORM nhập liệu -->
      <div class="booking-form-card">
        
        <!-- PHẦN 1: Thời gian & Dịch vụ chính -->
        <div class="form-section-title">
          <span>📅</span> Thời gian chăm sóc & Gói dịch vụ chính
        </div>

        <div class="form-group form-group-mb20">
          <label for="primary-service-select">Gói dịch vụ chính <span class="required">*</span></label>
          <select id="primary-service-select" name="serviceId" class="primary-service-select">
            ${/* safe */ primaryServicesOptionsHtml}
          </select>
        </div>
        
        <div class="form-row">
          <!-- Ngày bắt đầu -->
          <div class="form-group">
            <label for="ngay-bat-dau">Ngày bắt đầu <span class="required">*</span></label>
            <input type="date" id="ngay-bat-dau" name="ngayBatDau" required />
            <p class="field-error" id="error-bat-dau">⚠️ Vui lòng chọn ngày bắt đầu gửi</p>
          </div>
          <!-- Ngày kết thúc -->
          <div class="form-group">
            <label for="ngay-ket-thuc">Ngày kết thúc <span class="required">*</span></label>
            <input type="date" id="ngay-ket-thuc" name="ngayKetThuc" required />
            <p class="field-error" id="error-ket-thuc">⚠️ Vui lòng chọn ngày kết thúc (sau ngày bắt đầu)</p>
          </div>
        </div>

        <!-- PHẦN 2: Thông tin thú cưng -->
        <div class="form-section-title form-section-title-pet">
          <span>🐾</span> Thông tin thú cưng
        </div>

        <!-- Tên thú cưng -->
        <div id="saved-pets-container" class="saved-pets-container hidden">
          <label class="saved-pets-label">Chọn thú cưng đã lưu (Tùy chọn)</label>
          <select id="saved-pets" class="saved-pets-select">
            <option value="">-- Nhập thông tin mới --</option>
          </select>
        </div>
        <div class="form-group">
          <label for="ten-pet">Tên của bé <span class="required">*</span></label>
          <input type="text" id="ten-pet" name="tenPet" placeholder="Ví dụ: Bông, Milu, Kiki..." required />
          <p class="field-error" id="error-ten-pet">⚠️ Vui lòng điền tên thú cưng</p>
        </div>

        <div class="form-row">
          <!-- Loại pet -->
          <div class="form-group">
            <label>Loại thú cưng <span class="required">*</span></label>
            <div class="radio-group" role="group" aria-label="Loại thú cưng">
              <div class="radio-option">
                <input type="radio" id="pet-cho" name="loaiPet" value="Chó" checked />
                <label for="pet-cho">🐶 Chó</label>
              </div>
              <div class="radio-option">
                <input type="radio" id="pet-meo" name="loaiPet" value="Mèo" />
                <label for="pet-meo">🐱 Mèo</label>
              </div>
              <div class="radio-option">
                <input type="radio" id="pet-khac" name="loaiPet" value="Khác" />
                <label for="pet-khac">🐾 Khác</label>
              </div>
            </div>
          </div>

          <!-- Kích cỡ pet -->
          <div class="form-group">
            <label>Kích cỡ thú cưng <span class="required">*</span></label>
            <div class="radio-group" role="group" aria-label="Kích cỡ thú cưng">
              <div class="radio-option">
                <input type="radio" id="size-nho" name="kichCo" value="Nhỏ (dưới 5kg)" checked />
                <label for="size-nho">Nhỏ <small class="radio-hint">&lt;5kg</small></label>
              </div>
              <div class="radio-option">
                <input type="radio" id="size-vua" name="kichCo" value="Vừa (5–15kg)" />
                <label for="size-vua">Vừa <small class="radio-hint">5–15kg</small></label>
              </div>
              <div class="radio-option">
                <input type="radio" id="size-lon" name="kichCo" value="Lớn (trên 15kg)" />
                <label for="size-lon">Lớn <small class="radio-hint">&gt;15kg</small></label>
              </div>
            </div>
          </div>
        </div>

        <!-- Ghi chú đặc biệt -->
        <div class="form-group form-group-mb0">
          <label for="ghi-chu">Ghi chú & Hướng dẫn đặc biệt</label>
          <textarea
            id="ghi-chu"
            name="ghiChu"
            placeholder="Ví dụ: Bé cần ăn hạt loại nào, lịch cho uống thuốc, thói quen đi vệ sinh, bé sợ người lạ hoặc tiếng sấm..."
          ></textarea>
        </div>

        <!-- PHẦN 3: Dịch vụ bổ sung -->
        <div class="form-section-title form-section-title-addons">
          <span>✨</span> Dịch vụ bổ sung (Tùy chọn)
        </div>

        <div class="addons-grid">
          ${/* safe */ addonsHtml}
        </div>

      </div>

      <!-- Cột bên phải: TỔNG HỢP & THANH TOÁN (Sticky) -->
      <div class="sidebar-sticky">
        <!-- Thông tin sitter -->
        <div class="sitter-preview-card">
          <div class="preview-avatar preview-avatar-default">
            ${/* safe */ initials}
          </div>
          <div class="preview-info">
            <p class="preview-tag">Sitter được chọn</p>
            <p class="preview-name">${escapeHtml(sitter.ten)}</p>
            <div class="preview-meta">
              <span>📍 ${escapeHtml(sitter.khuVuc)}</span>
              <span>${renderSao(sitter.danhGia)} <strong>${/* safe */ sitter.danhGia}</strong></span>
            </div>
          </div>
          <div class="preview-price">
            <span class="preview-price-amount">${formatTien(baseDailyPrice)}</span>
            <span class="preview-price-unit">/ ngày</span>
          </div>
        </div>

        <!-- Hộp tóm tắt giá chi tiết -->
        <div class="price-summary-card">
          <p class="price-summary-title">💰 Tóm tắt chi phí</p>
          
          <div id="price-content">
            <p class="price-placeholder">
              Vui lòng chọn ngày bắt đầu và kết thúc gửi thú cưng để xem ước tính chi tiết.
            </p>
          </div>

          <!-- Nút đặt lịch -->
          <div class="submit-container">
            <button type="submit" class="btn btn-primary btn-lg submit-btn" id="btn-submit-booking">
              🔒 Xác nhận đặt lịch ngay
            </button>
            <p class="security-badge">
              Nhấn xác nhận nghĩa là bạn đồng ý với chính sách của PawStay. 
              Hủy lịch miễn phí 100% trước 48 giờ.
            </p>
          </div>
        </div>
      </div>
    </form>
  `;
}

// ────────────────────────────────────────────────────────────
// XỬ LÝ SỰ KIỆN FORM VÀ TÍNH TIỀN REALTIME
// ────────────────────────────────────────────────────────────
function initFormEvents() {
  const form = document.getElementById('booking-form');
  const inputBatDau = document.getElementById('ngay-bat-dau');
  const inputKetThuc = document.getElementById('ngay-ket-thuc');
  const inputTenPet = document.getElementById('ten-pet');
  const savedPetsSelect = document.getElementById('saved-pets');

  loadSavedPets();

  if (savedPetsSelect) {
    savedPetsSelect.addEventListener('change', autoFillPet);
  }

  // Set ngày tối thiểu gửi là ngày hôm nay
  const today = new Date().toISOString().split('T')[0];
  inputBatDau.min = today;
  inputKetThuc.min = today;

  // Khi đổi ngày bắt đầu -> cập nhật ngày kết thúc
  inputBatDau.addEventListener('change', function() {
    inputKetThuc.min = this.value;
    if (inputKetThuc.value && inputKetThuc.value < this.value) {
      inputKetThuc.value = '';
    }
    capNhatGia();
  });

  inputKetThuc.addEventListener('change', capNhatGia);
  const primarySelect = document.getElementById('primary-service-select');
  if (primarySelect) {
    primarySelect.addEventListener('change', capNhatGia);
  }

  // Event toggle chọn dịch vụ bổ sung
  document.querySelectorAll('.addon-card').forEach(card => {
    card.addEventListener('click', function() {
      const addonId = this.dataset.id;
      const isSelected = this.classList.toggle('selected');
      
      if (isSelected) {
        selectedAddons.push(addonId);
      } else {
        selectedAddons = selectedAddons.filter(id => id !== addonId);
      }
      
      capNhatGia();
    });
  });

  // Submit form
  form.addEventListener('submit', async function(e) {
    e.preventDefault();
    
    const batDau = inputBatDau.value;
    const ketThuc = inputKetThuc.value;
    const tenPet = inputTenPet.value.trim();
    
    let valid = true;
    document.querySelectorAll('.field-error').forEach(el => el.classList.remove('show'));

    if (!batDau) { document.getElementById('error-bat-dau').classList.add('show'); valid = false; }
    if (!ketThuc || tinhSoNgay(batDau, ketThuc) <= 0) { document.getElementById('error-ket-thuc').classList.add('show'); valid = false; }
    if (!tenPet) { document.getElementById('error-ten-pet').classList.add('show'); valid = false; }

    if (!valid) return;

    if (!currentQuote || !currentQuote.totalAmount) {
      alert('Vui lòng chờ hệ thống tính toán chi phí trước khi đặt lịch.');
      return;
    }

    const btn = document.getElementById('btn-submit-booking');
    btn.innerHTML = '⏳ Đang xử lý...';
    btn.disabled = true;

    const formData = new FormData(form);
    const primaryServiceEl = document.getElementById('primary-service-select');
    const payload = {
      sitterId: selectedSitter.sitterId || selectedSitter.id,
      serviceId: primaryServiceEl ? primaryServiceEl.value : null,
      ngayBatDau: batDau,
      ngayKetThuc: ketThuc,
      tenPet: tenPet,
      loaiPet: formData.get('loaiPet'),
      kichCo: formData.get('kichCo'),
      ghiChu: formData.get('ghiChu'),
      addons: selectedAddons,
      petId: (document.getElementById('saved-pets') && document.getElementById('saved-pets').value) ? document.getElementById('saved-pets').value : null,
      expectedTotal: currentQuote.totalAmount
    };

    try {
      const res = await apiFetch('/bookings', {
        method: 'POST',
        body: JSON.stringify(payload)
      });
      // Success
      window.location.href = 'confirmation.html?id=' + res.bookingId;
    } catch(err) {
      if (err.status === 409 || err.statusCode === 409 || err.newTotal) {
        alert(err.message || 'Giá dịch vụ đã thay đổi hoặc không khớp. Hệ thống đang cập nhật báo giá mới.');
        await capNhatGia();
      } else {
        alert(err.message || 'Lỗi khi đặt lịch');
      }
      btn.innerHTML = '🔒 Xác nhận đặt lịch ngay';
      btn.disabled = false;
    }
  });
}

// Hàm tính tiền và hiển thị chi tiết phí lấy trực tiếp từ Server Quote (Item 5b)
async function capNhatGia() {
  const inputBatDau = document.getElementById('ngay-bat-dau');
  const inputKetThuc = document.getElementById('ngay-ket-thuc');
  const primaryServiceSelect = document.getElementById('primary-service-select');
  const priceContent = document.getElementById('price-content');

  const batDau = inputBatDau ? inputBatDau.value : '';
  const ketThuc = inputKetThuc ? inputKetThuc.value : '';
  const serviceId = (primaryServiceSelect && primaryServiceSelect.value) ? primaryServiceSelect.value : null;

  if (!batDau || !ketThuc || !selectedSitter) {
    priceContent.innerHTML = `
      <p class="price-placeholder">
        Vui lòng chọn ngày bắt đầu và kết thúc gửi thú cưng để xem ước tính chi tiết.
      </p>
    `;
    return;
  }

  const soNgay = tinhSoNgay(batDau, ketThuc);

  if (soNgay <= 0) {
    priceContent.innerHTML = `
      <p class="price-placeholder price-placeholder-error">
        ⚠️ Ngày kết thúc phải sau hoặc cùng ngày bắt đầu gửi. Vui lòng kiểm tra lại.
      </p>
    `;
    return;
  }

  priceContent.innerHTML = `
    <p class="price-placeholder price-placeholder-loading">
      ⏳ Đang tính toán chi phí từ máy chủ...
    </p>
  `;

  try {
    const quote = await apiFetch('/bookings/quote', {
      method: 'POST',
      body: JSON.stringify({
        sitterId: selectedSitter.sitterId || selectedSitter.id,
        serviceId: serviceId,
        ngayBatDau: batDau,
        ngayKetThuc: ketThuc,
        addons: selectedAddons
      })
    });

    currentQuote = quote;

    // Cập nhật giá trên thẻ preview nếu có
    const previewPriceEl = document.querySelector('.preview-price-amount');
    if (previewPriceEl && quote.primaryServiceUnitPrice) {
      previewPriceEl.textContent = formatTien(quote.primaryServiceUnitPrice);
    }

    let rowsHtml = '';
    if (quote.breakdown && quote.breakdown.length > 0) {
      rowsHtml = quote.breakdown.map(item => `
        <div class="price-row ${/* safe */ item.type && item.type.includes('ADDON') ? 'addon-item' : ''}">
          <div>
            <span>${escapeHtml(item.name)}</span>
            <span class="price-row-label-sub">${formatTien(item.unitPrice)} × ${/* safe */ item.quantity}</span>
          </div>
          <span>${/* safe */ item.type && item.type.includes('ADDON') ? '+' : ''}${formatTien(item.total)}</span>
        </div>
      `).join('');
    }

    const formatNgayDoc = (str) => {
      const d = new Date(str);
      return `${d.getDate()}/${d.getMonth()+1}/${d.getFullYear()}`;
    };

    priceContent.innerHTML = `
      ${/* safe */ rowsHtml}

      <!-- Mốc thời gian -->
      <div class="price-timeline">
        📅 Lịch: ${formatNgayDoc(batDau)} – ${formatNgayDoc(ketThuc)} (${/* safe */ quote.soNgay} ngày)
      </div>

      <!-- Chi phí ước tính -->
      <div class="price-row total">
        <span>Chi phí ước tính</span>
        <span class="amount">${formatTien(quote.totalAmount)}</span>
      </div>
      <div class="price-note">
        💡 Thanh toán an toàn qua VietQR (PayOS) sau khi Sitter chấp nhận đơn. Tiền được tạm giữ an toàn đến khi hoàn tất dịch vụ.
      </div>
    `;
  } catch (err) {
    priceContent.innerHTML = `
      <p class="price-placeholder price-placeholder-error">
        ⚠️ Không thể tính giá: ${escapeHtml(err.message || 'Lỗi kết nối')}
      </p>
    `;
  }
}

async function loadSavedPets() {
  const container = document.getElementById('saved-pets-container');
  const select = document.getElementById('saved-pets');
  if (!container || !select) return;

  const token = localStorage.getItem('pawstay_token');
  if (!token) {
    container.classList.add('hidden');
    return;
  }

  try {
    const pets = await apiFetch('/pets');
    if (Array.isArray(pets) && pets.length > 0) {
      mySavedPets = pets;
      container.classList.remove('hidden');
      select.innerHTML = '<option value="">-- Nhập thông tin mới --</option>';
      pets.forEach(p => {
        const opt = document.createElement('option');
        opt.value = p.petId || '';
        const speciesText = p.species ? ' (' + p.species + ')' : '';
        opt.textContent = (p.name || 'Thú cưng') + speciesText;
        select.appendChild(opt);
      });
    } else {
      container.classList.add('hidden');
    }
  } catch (err) {
    console.warn('Không thể tải thú cưng đã lưu:', err);
    container.classList.add('hidden');
  }
}

function autoFillPet() {
  const select = document.getElementById('saved-pets');
  if (!select) return;
  const petId = select.value;
  if (!petId) return;

  const pet = mySavedPets.find(p => p.petId === petId);
  if (!pet) return;

  const inputTen = document.getElementById('ten-pet');
  if (inputTen && pet.name) inputTen.value = pet.name;

  if (pet.species) {
    const sp = pet.species.toLowerCase();
    if (sp.includes('chó') || sp.includes('cho') || sp.includes('dog')) {
      const r = document.getElementById('pet-cho');
      if (r) r.checked = true;
    } else if (sp.includes('mèo') || sp.includes('meo') || sp.includes('cat')) {
      const r = document.getElementById('pet-meo');
      if (r) r.checked = true;
    } else {
      const r = document.getElementById('pet-khac');
      if (r) r.checked = true;
    }
  }

  if (pet.weight) {
    const w = parseFloat(pet.weight);
    if (w < 5) {
      const s = document.getElementById('size-nho');
      if (s) s.checked = true;
    } else if (w <= 15) {
      const s = document.getElementById('size-vua');
      if (s) s.checked = true;
    } else {
      const s = document.getElementById('size-lon');
      if (s) s.checked = true;
    }
  }
}

