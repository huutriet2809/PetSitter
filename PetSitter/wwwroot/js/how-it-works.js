document.addEventListener('DOMContentLoaded', function() {
  renderFAQ();
});

// Render FAQ Accordion from data.js
function renderFAQ() {
  const faqList = document.getElementById('faq-accordion-list');
  if (!faqList || typeof FAQ_DATA === 'undefined' || !FAQ_DATA.length) return;

  // Chọn lọc ra 5 câu hỏi thông dụng nhất
  const selectedFaqs = FAQ_DATA.slice(0, 5);

  faqList.innerHTML = selectedFaqs.map(faq => `
    <div class="faq-item" id="faq-item-${faq.id}" data-faq-id="${faq.id}">
      <div class="faq-header" role="button" tabindex="0" aria-expanded="false">
        <span class="faq-question">${escapeHtml(faq.cauHoi)}</span>
        <span class="faq-toggle-icon">▼</span>
      </div>
      <div class="faq-body" id="faq-body-${faq.id}">
        <div class="faq-answer">${escapeHtml(faq.traLoi)}</div>
      </div>
    </div>
  `).join('');

  // Event delegation thay vì inline onclick
  faqList.addEventListener('click', function(e) {
    const header = e.target.closest('.faq-header');
    if (!header) return;

    const item = header.closest('.faq-item');
    if (!item) return;

    toggleFaqItem(item);
  });

  // Hỗ trợ Enter/Space cho bàn phím tiếp cận (Accessibility)
  faqList.addEventListener('keydown', function(e) {
    if (e.key === 'Enter' || e.key === ' ') {
      const header = e.target.closest('.faq-header');
      if (header) {
        e.preventDefault();
        const item = header.closest('.faq-item');
        if (item) toggleFaqItem(item);
      }
    }
  });
}

// Toggle FAQ item class
function toggleFaqItem(item) {
  const isActive = item.classList.contains('active');
  const allItems = document.querySelectorAll('.faq-item');

  allItems.forEach(el => {
    el.classList.remove('active');
    const hdr = el.querySelector('.faq-header');
    if (hdr) hdr.setAttribute('aria-expanded', 'false');
  });

  if (!isActive) {
    item.classList.add('active');
    const hdr = item.querySelector('.faq-header');
    if (hdr) hdr.setAttribute('aria-expanded', 'true');
  }
}

