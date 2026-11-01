if (!isLoggedIn()) {
  window.location.href = 'login.html';
}

document.addEventListener('DOMContentLoaded', async () => {
  // Load thông tin người dùng
  try {
    const user = await apiFetch('/auth/me');
    const emailEl = document.getElementById('email');
    const nameEl = document.getElementById('name');
    const phoneEl = document.getElementById('phone');
    const avatarUrlEl = document.getElementById('avatarUrl');

    if (emailEl) emailEl.value = user.email || '';
    if (nameEl) nameEl.value = user.fullName || '';
    if (phoneEl) phoneEl.value = user.phone || '';
    if (avatarUrlEl) avatarUrlEl.value = user.avatarUrl || '';
  } catch (err) {
    showToast("Không tải được thông tin", 3000, "❌");
  }

  // Profile update form
  const profileForm = document.getElementById('profile-form');
  if (profileForm) {
    profileForm.addEventListener('submit', async (e) => {
      e.preventDefault();
      const nameInput = document.getElementById('name');
      const phoneInput = document.getElementById('phone');
      const avatarUrlInput = document.getElementById('avatarUrl');

      const payload = {
        name: nameInput ? nameInput.value.trim() : '',
        phone: phoneInput ? phoneInput.value.trim() : '',
        avatarUrl: avatarUrlInput ? avatarUrlInput.value.trim() : ''
      };

      try {
        await apiFetch('/auth/me', { method: 'PUT', body: JSON.stringify(payload) });

        // Cập nhật lại cache và navbar
        const user = await apiFetch('/auth/me');
        localStorage.setItem('pawstay_user', JSON.stringify(user));
        if (typeof renderNavbar === 'function') {
          renderNavbar();
        }

        showToast("Đã cập nhật thông tin thành công!", 3000, "✅");
      } catch (err) {
        showToast(err.message, 3000, "❌");
      }
    });
  }

  // Password update form
  const passwordForm = document.getElementById('password-form');
  if (passwordForm) {
    passwordForm.addEventListener('submit', async (e) => {
      e.preventDefault();
      const currentPasswordInput = document.getElementById('currentPassword');
      const newPasswordInput = document.getElementById('newPassword');
      const errEl = document.getElementById('pwd-error');

      const currentPassword = currentPasswordInput ? currentPasswordInput.value : '';
      const newPassword = newPasswordInput ? newPasswordInput.value : '';

      if (errEl) {
        errEl.classList.remove('show');
        errEl.textContent = '';
      }

      try {
        await apiFetch('/auth/change-password', {
          method: 'POST',
          body: JSON.stringify({ currentPassword, newPassword }),
          suppressAuthError: true // prevent redirect on 401 just in case, though API returns 400
        });
        showToast("Đổi mật khẩu thành công!", 3000, "✅");
        passwordForm.reset();
      } catch (err) {
        if (errEl) {
          errEl.textContent = err.message;
          errEl.classList.add('show');
        }
      }
    });
  }

  // Logout all devices
  const logoutAllBtn = document.getElementById('btn-logout-all');
  if (logoutAllBtn) {
    logoutAllBtn.addEventListener('click', async () => {
      if (!confirm('Bạn có chắc chắn muốn đăng xuất khỏi TẤT CẢ các thiết bị?\n\nTất cả phiên đăng nhập hiện tại sẽ bị thu hồi và bạn sẽ cần đăng nhập lại.')) {
        return;
      }
      try {
        await apiFetch('/auth/logout-all', { method: 'POST' });
        localStorage.removeItem('pawstay_token');
        localStorage.removeItem('pawstay_user');
        alert('Đã đăng xuất khỏi tất cả thiết bị thành công.');
        window.location.href = 'login.html';
      } catch (err) {
        showToast(err.message || 'Lỗi khi đăng xuất tất cả thiết bị', 3000, '❌');
      }
    });
  }
});

