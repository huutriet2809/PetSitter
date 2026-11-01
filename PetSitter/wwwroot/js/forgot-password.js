document.addEventListener('DOMContentLoaded', function() {
    const form = document.getElementById('forgot-form');
    if (!form) return;

    form.addEventListener('submit', async (e) => {
        e.preventDefault();
        const emailInput = document.getElementById('email');
        const email = emailInput ? emailInput.value.trim() : '';
        const btn = form.querySelector('button[type="submit"]');
        const resultMsg = document.getElementById('result-msg');

        if (!btn || !resultMsg) return;

        btn.disabled = true;
        btn.textContent = 'Đang gửi...';
        resultMsg.innerHTML = '';

        try {
            const res = await apiFetch('/auth/forgot-password', {
                method: 'POST',
                body: JSON.stringify({ email })
            });

            const successDiv = document.createElement('div');
            successDiv.className = 'alert-success';
            successDiv.textContent = res.message;
            resultMsg.appendChild(successDiv);

            if (res.devResetLink) {
                const devDiv = document.createElement('div');
                devDiv.className = 'dev-link';
                devDiv.innerHTML = '<strong>Link dành cho Dev:</strong><br>';
                const link = document.createElement('a');
                link.href = res.devResetLink;
                link.textContent = res.devResetLink;
                devDiv.appendChild(link);
                resultMsg.appendChild(devDiv);
            }
        } catch (err) {
            const errorDiv = document.createElement('div');
            errorDiv.className = 'alert-error';
            errorDiv.textContent = err.message || 'Có lỗi xảy ra khi gửi yêu cầu.';
            resultMsg.appendChild(errorDiv);
        } finally {
            btn.disabled = false;
            btn.textContent = 'Gửi yêu cầu';
        }
    });
});

