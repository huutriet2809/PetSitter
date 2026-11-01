document.addEventListener('DOMContentLoaded', function() {
  const pwdInput = document.getElementById('login-password');
  const toggleBtn = document.getElementById('btn-toggle-pwd');
  const form = document.getElementById('login-form');

  // Toggle ẩn/hiện mật khẩu
  if (toggleBtn && pwdInput) {
    toggleBtn.addEventListener('click', function() {
      const isPwd = pwdInput.type === 'password';
      pwdInput.type = isPwd ? 'text' : 'password';
      this.textContent = isPwd ? '🙈' : '👁️';
      this.setAttribute('aria-label', isPwd ? 'Ẩn mật khẩu' : 'Hiện mật khẩu');
    });
  }

  // Submit đăng nhập
  if (form) {
    form.addEventListener('submit', async function(e) {
      e.preventDefault();

      const emailInput = document.getElementById('login-email');
      const email = emailInput ? emailInput.value.trim() : '';
      const matKhau = pwdInput ? pwdInput.value : '';

      if (!email || !matKhau) {
        showToast('Vui lòng nhập đầy đủ email và mật khẩu! ⚠️', 2500, '❌');
        return;
      }

      try {
        const data = await apiFetch('/auth/login', {
          method: 'POST',
          body: JSON.stringify({ email: email, password: matKhau })
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
          message: "Chào mừng " + realUser.fullName + " quay trở lại! 🐾",
          type: 'success',
          icon: '👋'
        }));

        const urlParams = new URLSearchParams(window.location.search);
        let returnUrl = urlParams.get('returnUrl');
        if (returnUrl) {
          returnUrl = decodeURIComponent(returnUrl);
          if (returnUrl.startsWith('//') || returnUrl.includes('://') || (!returnUrl.startsWith('/') && !returnUrl.endsWith('.html'))) {
            returnUrl = 'index.html';
          }
          window.location.href = returnUrl;
        } else {
          if (realUser.role === 'ADMIN') {
            window.location.href = 'admin.html';
          } else if (realUser.role === 'SITTER') {
            window.location.href = 'sitter-dashboard.html';
          } else {
            window.location.href = 'owner-dashboard.html';
          }
        }
      } catch (error) {
        showToast(error.message, 2500, '❌');
      }
    });
  }
});

