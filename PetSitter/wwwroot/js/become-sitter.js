document.addEventListener('DOMContentLoaded', async () => {
  if (!localStorage.getItem('pawstay_token')) {
    window.location.href = 'login.html';
    return;
  }
  
  const content = document.getElementById('content');

  // Event delegation cho nút logout
  content.addEventListener('click', (e) => {
    const logoutBtn = e.target.closest('[data-action="logout"]');
    if (logoutBtn) {
      if (typeof logout === 'function') {
        logout();
      } else {
        localStorage.removeItem('pawstay_token');
        localStorage.removeItem('pawstay_user');
        window.location.href = 'login.html';
      }
    }
  });
  
  try {
    const profile = await apiFetch('/sitter-applications/me');
    if (profile.status === 'Pending') {
      content.innerHTML = `
        <div class="status-box status-pending">
          <h3>Hồ sơ đang chờ duyệt</h3>
          <p>Vui lòng kiên nhẫn, quản trị viên đang xem xét hồ sơ của bạn.</p>
        </div>
      `;
    } else if (profile.status === 'Approved') {
      content.innerHTML = `
        <div class="status-box status-approved">
          <h3>Hồ sơ đã được duyệt! 🎉</h3>
          <p>Bạn đã là một Pet-sitter của PawStay. Vui lòng đăng xuất và đăng nhập lại để cập nhật quyền hạn hoặc truy cập trực tiếp nếu đã có quyền.</p>
          <div class="approved-actions">
            <button class="btn btn-outline" data-action="logout">Đăng xuất để cập nhật quyền</button>
            <a href="sitter-dashboard.html" class="btn btn-primary">Vào Bảng điều khiển Sitter</a>
          </div>
        </div>
      `;
    } else if (profile.status === 'Rejected') {
      content.innerHTML = `
        <div class="status-box status-rejected">
          <h3>Hồ sơ bị từ chối</h3>
          <p>Lý do: <strong>${escapeHtml(profile.lyDoTuChoi || 'Không có')}</strong></p>
        </div>
        <hr class="form-divider" />
        <h3>Nộp lại hồ sơ</h3>
        ${/* safe */ renderForm()}
      `;
      setupForm();
    }
  } catch (e) {
    // Chưa nộp hồ sơ
    content.innerHTML = renderForm();
    setupForm();
  }
});

function renderForm() {
  return `
    <form id="apply-form">
      <div class="form-group">
        <label>Giới thiệu bản thân</label>
        <textarea id="bio" rows="4" required></textarea>
      </div>
      <div class="form-group">
        <label>Khu vực (Quận/Huyện)</label>
        <input type="text" id="khuVuc" required />
      </div>
      <div class="form-group">
        <label>Giá trông giữ cơ bản (VNĐ/ngày)</label>
        <input type="number" id="giaMoiNgay" required min="10000" />
      </div>
      <div class="form-group">
        <label>Năm kinh nghiệm</label>
        <input type="number" id="kinhNghiem" required min="0" />
      </div>
      <div class="form-group">
        <label>Loại thú cưng nhận trông</label>
        <input type="text" id="loaiPetNhan" placeholder="Ví dụ: Chó, Mèo" required />
      </div>
      <div class="form-group">
        <label>Các dịch vụ cung cấp (Tags)</label>
        <input type="text" id="tags" placeholder="Ví dụ: Tắm rửa, Trông đêm, Đi dạo" required />
      </div>
      <div id="error-msg" class="error-message hidden"></div>
      <button type="submit" class="btn btn-primary submit-btn-full">Nộp hồ sơ đăng ký</button>
    </form>
  `;
}

function setupForm() {
  const form = document.getElementById('apply-form');
  if (!form) return;
  
  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    const payload = {
      bio: document.getElementById('bio').value,
      khuVuc: document.getElementById('khuVuc').value,
      giaMoiNgay: parseFloat(document.getElementById('giaMoiNgay').value),
      kinhNghiem: parseInt(document.getElementById('kinhNghiem').value),
      loaiPetNhan: document.getElementById('loaiPetNhan').value.split(',').map(s => s.trim()),
      tags: document.getElementById('tags').value.split(',').map(s => s.trim())
    };

    try {
      await apiFetch('/sitter-applications', { method: 'POST', body: JSON.stringify(payload) });
      window.location.reload();
    } catch (err) {
      const errEl = document.getElementById('error-msg');
      if (errEl) {
        errEl.textContent = err.message || 'Có lỗi xảy ra.';
        errEl.classList.remove('hidden');
      }
    }
  });
}

