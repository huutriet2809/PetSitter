document.addEventListener('DOMContentLoaded', function() {
  const form = document.getElementById('contact-form');
  if (!form) return;

  form.addEventListener('submit', async function(e) {
    e.preventDefault();

    const nameInput = document.getElementById('contact-name');
    const emailInput = document.getElementById('contact-email');
    const subjectInput = document.getElementById('contact-subject');
    const messageInput = document.getElementById('contact-message');
    const websiteInput = document.getElementById('contact-website');
    const btn = form.querySelector('button[type="submit"]');

    const name = nameInput ? nameInput.value.trim() : '';
    const email = emailInput ? emailInput.value.trim() : '';
    const subject = subjectInput ? subjectInput.value.trim() : '';
    const message = messageInput ? messageInput.value.trim() : '';
    const website = websiteInput ? websiteInput.value : '';

    if (btn) {
      btn.disabled = true;
      btn.innerHTML = 'Đang gửi...';
    }

    try {
      const res = await apiFetch('/contact', {
        method: 'POST',
        body: JSON.stringify({ name, email, subject, message, website })
      });
      alert(res.message);
      form.reset();
    } catch (err) {
      alert(err.message);
    } finally {
      if (btn) {
        btn.disabled = false;
        btn.innerHTML = '📨 Gửi tin nhắn ngay';
      }
    }
  });
});

