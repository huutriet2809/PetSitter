document.addEventListener('DOMContentLoaded', function() {
    const urlParams = new URLSearchParams(window.location.search);
    const token = urlParams.get('token');
    const errorMsg = document.getElementById('error-msg');
    const resetForm = document.getElementById('reset-form');
    const successMsg = document.getElementById('success-msg');

    if (!token) {
        if (errorMsg) errorMsg.textContent = 'Liên kết không hợp lệ (thiếu token).';
    } else {
        if (resetForm) resetForm.classList.remove('hidden');
    }

    if (resetForm) {
        resetForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            const passInput = document.getElementById('password');
            const confirmInput = document.getElementById('confirm-password');
            const pass = passInput ? passInput.value : '';
            const confirm = confirmInput ? confirmInput.value : '';

            if (pass !== confirm) {
                if (errorMsg) errorMsg.textContent = 'Mật khẩu xác nhận không khớp.';
                return;
            }

            const btn = resetForm.querySelector('button[type="submit"]');
            if (errorMsg) errorMsg.textContent = '';

            if (btn) {
                btn.disabled = true;
                btn.textContent = 'Đang xử lý...';
            }

            try {
                await apiFetch('/auth/reset-password', {
                    method: 'POST',
                    body: JSON.stringify({ token, newPassword: pass })
                });

                resetForm.classList.add('hidden');
                if (successMsg) successMsg.classList.remove('hidden');
            } catch (err) {
                if (errorMsg) errorMsg.textContent = err.message || 'Không thể đặt lại mật khẩu.';
            } finally {
                if (btn) {
                    btn.disabled = false;
                    btn.textContent = 'Đặt lại mật khẩu';
                }
            }
        });
    }
});

