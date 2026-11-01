document.addEventListener('DOMContentLoaded', () => {
  const user = typeof getCurrentUser === 'function' ? getCurrentUser() : JSON.parse(localStorage.getItem('pawstay_user') || 'null');
  if (!user || user.role !== 'SITTER') {
    document.body.innerHTML = `
      <div class="no-permission-box">
        <h2>Bạn không có quyền truy cập</h2>
        <br>
        <a href="index.html" class="btn btn-primary">Về trang chủ</a>
      </div>
    `;
    return;
  }
  init();
});

async function init() {
  try {
    const services = await apiFetch('/services');
    const srvContainer = document.getElementById('services-container');
    if (srvContainer) {
      srvContainer.innerHTML = services.map(s => 
        `<label class="checkbox-item"><input type="checkbox" name="dichvu" value="${/* safe */ s.id}"> ${escapeHtml(s.ten)}</label>`
      ).join('');
    }

    const primarySelect = document.getElementById('primary-service-select');
    if (primarySelect) {
      primarySelect.innerHTML = '<option value="">-- Chọn dịch vụ chính --</option>' + services.map(s =>
        `<option value="${/* safe */ s.id}">${escapeHtml(s.ten)}</option>`
      ).join('');
    }

    const profile = await apiFetch('/sitter/profile');
    if (profile) {
      const bioEl = document.getElementById('bio');
      if (bioEl) bioEl.value = profile.bio || '';
      
      let basePrice = 100000;
      if (profile.dichVu && profile.dichVu.length > 0) {
        const primaryDichVu = profile.dichVu.find(d => d.isPrimary) || profile.dichVu[0];
        basePrice = primaryDichVu.gia;
      }
      const giaEl = document.getElementById('gia');
      if (giaEl) giaEl.value = basePrice;
      
      const khuVucEl = document.getElementById('khuvuc');
      if (khuVucEl && profile.khuVuc) khuVucEl.value = profile.khuVuc;

      const tagsEl = document.getElementById('tags');
      if (tagsEl) tagsEl.value = (profile.tags || []).join(', ');

      const pets = (profile.tags || []).filter(t => ['Chó', 'Mèo', 'Khác'].includes(t));
      document.querySelectorAll('input[name="pet"]').forEach(cb => {
        if (pets.includes(cb.value)) cb.checked = true;
      });

      // Đánh dấu các dịch vụ đã chọn và chọn dịch vụ chính
      const primarySrv = (profile.dichVu || []).find(d => d.isPrimary);
      if (primarySelect) {
        if (primarySrv && primarySrv.serviceId) {
          primarySelect.value = primarySrv.serviceId;
        } else if (profile.dichVu && profile.dichVu.length > 0) {
          const matched = services.find(s => s.ten === profile.dichVu[0].ten);
          if (matched) primarySelect.value = matched.id;
        }
      }

      document.querySelectorAll('input[name="dichvu"]').forEach(cb => {
        const srv = profile.dichVu.find(d => d.ten === cb.parentElement.textContent.trim());
        if (srv) cb.checked = true;
      });

      const btnView = document.getElementById('view-profile-btn');
      if (btnView && profile.sitterId) {
        btnView.href = `sitter-profile.html?id=${encodeURIComponent(profile.sitterId)}`;
        btnView.classList.remove('hidden');
      }
    }
  } catch (err) {
    console.error('Lỗi khởi tạo hồ sơ sitter:', err);
  }

  const form = document.getElementById('sitter-form');
  if (form) {
    form.addEventListener('submit', async (e) => {
      e.preventDefault();
      const btn = document.getElementById('btn-submit');
      if (btn) {
        btn.textContent = 'Đang lưu...';
        btn.disabled = true;
      }

      const primarySrvId = document.getElementById('primary-service-select')?.value;
      if (!primarySrvId) {
        if (typeof showToast === 'function') {
          showToast('Vui lòng chọn dịch vụ trông giữ chính!', 3000, '⚠️');
        } else {
          alert('Vui lòng chọn dịch vụ trông giữ chính!');
        }
        if (btn) {
          btn.textContent = 'Lưu thay đổi';
          btn.disabled = false;
        }
        return;
      }

      const petChecked = Array.from(document.querySelectorAll('input[name="pet"]:checked')).map(cb => cb.value);
      const srvChecked = Array.from(document.querySelectorAll('input[name="dichvu"]:checked')).map(cb => cb.value);
      if (!srvChecked.includes(primarySrvId)) {
        srvChecked.push(primarySrvId);
      }

      const tagsInput = (document.getElementById('tags')?.value || '').split(',').map(t => t.trim()).filter(t => t);
      const allTags = [...new Set([...petChecked, ...tagsInput])];

      const payload = {
        bio: document.getElementById('bio')?.value || '',
        giaMoiNgay: parseInt(document.getElementById('gia')?.value || '0', 10),
        khuVuc: document.getElementById('khuvuc')?.value || '',
        loaiPetNhan: petChecked,
        tags: allTags,
        dichVuBoSung: srvChecked,
        primaryServiceId: primarySrvId
      };

      try {
        await apiFetch('/sitter/profile', { method: 'PUT', body: JSON.stringify(payload) });
        if (typeof showToast === 'function') {
          showToast('Lưu hồ sơ thành công!', 3000, '✅');
        }
      } catch (err) {
        if (typeof showToast === 'function') {
          showToast(err.message || 'Lỗi khi lưu hồ sơ.', 3000, '❌');
        } else {
          alert(err.message || 'Lỗi khi lưu hồ sơ.');
        }
      } finally {
        if (btn) {
          btn.textContent = 'Lưu thay đổi';
          btn.disabled = false;
        }
      }
    });
  }
}

