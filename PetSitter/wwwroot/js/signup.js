document.addEventListener('DOMContentLoaded', function() {
  const pwdInput = document.getElementById('signup-password');
  const confirmInput = document.getElementById('signup-confirm');
  const togglePwdBtn = document.getElementById('btn-toggle-pwd');
  const toggleConfirmBtn = document.getElementById('btn-toggle-confirm');
  const form = document.getElementById('signup-form');

  // Toggle ẩn/hiện mật khẩu chính
  if (togglePwdBtn && pwdInput) {
    togglePwdBtn.addEventListener('click', function() {
      const isPwd = pwdInput.type === 'password';
      pwdInput.type = isPwd ? 'text' : 'password';
      this.textContent = isPwd ? '🙈' : '👁️';
      this.setAttribute('aria-label', isPwd ? 'Ẩn mật khẩu' : 'Hiện mật khẩu');
    });
  }

  // Toggle ẩn/hiện mật khẩu xác nhận
  if (toggleConfirmBtn && confirmInput) {
    toggleConfirmBtn.addEventListener('click', function() {
      const isPwd = confirmInput.type === 'password';
      confirmInput.type = isPwd ? 'text' : 'password';
      this.textContent = isPwd ? '🙈' : '👁️';
      this.setAttribute('aria-label', isPwd ? 'Ẩn mật khẩu' : 'Hiện mật khẩu xác nhận');
    });
  }

  // Submit đăng ký
  if (form) {
    form.addEventListener('submit', async function(e) {
      e.preventDefault();

      const nameInput = document.getElementById('signup-name');
      const emailInput = document.getElementById('signup-email');
      const termsInput = document.getElementById('signup-terms');

      const name = nameInput ? nameInput.value.trim() : '';
      const email = emailInput ? emailInput.value.trim() : '';
      const password = pwdInput ? pwdInput.value : '';
      const confirm = confirmInput ? confirmInput.value : '';
      const termsChecked = termsInput ? termsInput.checked : false;

      // Validation các trường trống
      if (!name || !email || !password || !confirm) {
        showToast('Vui lòng điền đầy đủ tất cả thông tin! ⚠️', 2500, '❌');
        return;
      }

      // Validation độ dài mật khẩu
      if (password.length < 8) {
        showToast('Mật khẩu phải chứa ít nhất 8 ký tự! ⚠️', 2500, '❌');
        return;
      }

      // Validation trùng khớp mật khẩu
      if (password !== confirm) {
        showToast('Mật khẩu xác nhận không khớp! Vui lòng thử lại. ⚠️', 2500, '❌');
        return;
      }

      // Validation điều khoản
      if (!termsChecked) {
        showToast('Vui lòng đồng ý với các Điều khoản & Chính sách! ⚠️', 2500, '❌');
        return;
      }

      try {
        const data = await apiFetch('/auth/register', {
          method: 'POST',
          body: JSON.stringify({ fullName: name, email: email, password: password })
        });

        localStorage.setItem('pawstay_token', data.token);

        // Get user info
        const me = await apiFetch('/auth/me');
        const realUser = {
          userId: me.userId,
          fullName: me.fullName,
          email: me.email,
          role: me.role,
          phone: me.phone,
          avatarUrl: me.avatarUrl || (typeof getInitials === 'function' ? getInitials(me.fullName || 'US') : 'US'),
          id: me.userId,
          ten: me.fullName
        };
        localStorage.setItem('pawstay_user', JSON.stringify(realUser));

        localStorage.setItem('pawstay_toast_on_load', JSON.stringify({
          message: "Đăng ký tài khoản thành công! Chào mừng " + realUser.fullName + " gia nhập PawStay 🐾",
          type: 'success',
          icon: '🐾'
        }));

        window.location.href = 'index.html';
      } catch (error) {
        showToast(error.message, 2500, '❌');
      }
    });
  }
});

