/* PawStay Home Page Script */
document.addEventListener('DOMContentLoaded', () => {
    // ──────────────────────────────────────────────────
    // HERO BG: kích hoạt animation pan sau khi load
    // ──────────────────────────────────────────────────
    const heroBg = document.getElementById('hero-bg');
    if (heroBg) {
        const heroImg = new Image();
        heroImg.onload = () => heroBg.classList.add('loaded');
        heroImg.src = 'assets/images/hero_pets.png';
    }

    // ──────────────────────────────────────────────────
    // EVENT DELEGATION CHO FEATURED SITTERS
    // ──────────────────────────────────────────────────
    const track = document.getElementById('featured-track');
    if (track) {
        track.addEventListener('click', (e) => {
            const bookBtn = e.target.closest('[data-action="book-sitter"]');
            if (bookBtn) {
                e.stopPropagation();
                const id = bookBtn.getAttribute('data-id');
                if (id) bookSitter(id);
                return;
            }

            const card = e.target.closest('[data-action="view-sitter"]');
            if (card) {
                const id = card.getAttribute('data-id');
                if (id) {
                    window.location.href = `sitter-profile.html?id=${encodeURIComponent(id)}`;
                }
            }
        });
    }

    // ──────────────────────────────────────────────────
    // NEWSLETTER FORM
    // ──────────────────────────────────────────────────
    const newsletterForm = document.getElementById('newsletter-form');
    if (newsletterForm) {
        newsletterForm.addEventListener('submit', async function(e) {
            e.preventDefault();
            const inputEl = document.getElementById('newsletter-email');
            const email = inputEl ? inputEl.value.trim() : '';
            if (!email) return;

            const submitBtn = document.getElementById('newsletter-submit');
            if (submitBtn) submitBtn.disabled = true;

            try {
                const res = await apiFetch('/newsletter', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ email: email })
                });
                const msg = (res && res.message) ? res.message : 'Đăng ký nhận bản tin thành công.';
                newsletterForm.innerHTML = `<div class="newsletter-success-msg">✅ ${escapeHtml(msg)} Cảm ơn bạn! 🐾</div>`;
            } catch (err) {
                if (typeof showToast === 'function') {
                    showToast(err.message || 'Đăng ký nhận tin thất bại. Vui lòng thử lại.', 4000, '❌');
                } else {
                    alert(err.message || 'Đăng ký nhận tin thất bại.');
                }
                if (submitBtn) submitBtn.disabled = false;
            }
        });
    }

    // ──────────────────────────────────────────────────
    // Smooth scroll cho link "Cách hoạt động"
    // ──────────────────────────────────────────────────
    const ctaHeroHow = document.getElementById('cta-hero-how');
    if (ctaHeroHow) {
        ctaHeroHow.addEventListener('click', function(e) {
            e.preventDefault();
            const howSec = document.getElementById('how');
            if (howSec) {
                howSec.scrollIntoView({ behavior: 'smooth' });
            }
        });
    }

    loadHomePageData();
});

function buildSitterCard(sitter) {
    const initials = getInitials(sitter.ten);
    const hasValidAvatar = sitter.avatarUrl && (sitter.avatarUrl.startsWith("https://") || sitter.avatarUrl.startsWith("/uploads/"));
    const avatarHtml = hasValidAvatar 
        ? `<img src="${escapeHtml(sitter.avatarUrl)}" class="sitter-avatar-img" alt="${escapeHtml(sitter.ten)}">`
        : `<div class="avatar-placeholder avatar-placeholder-gradient">${escapeHtml(initials)}</div>`;

    const verifiedBadge = sitter.noiBat ? '<div class="verified-overlay"><span class="badge badge-verified">✓ Đã xác minh</span></div>' : '';
    const petTags = (sitter.tags || []).map(p => {
        const icon = p === 'Chó' ? '🐶' : p === 'Mèo' ? '🐱' : '🐾';
        return `<span class="pet-tag">${icon} ${escapeHtml(p)}</span>`;
    }).join('');

    return `
        <div class="sitter-card"
             data-action="view-sitter"
             data-id="${escapeHtml(sitter.sitterId)}"
             role="article"
             aria-label="Sitter: ${escapeHtml(sitter.ten)}">
            <div class="sitter-avatar-wrap">
                ${avatarHtml}
                <div class="avatar-gradient"></div>
                ${verifiedBadge}
            </div>
            <div class="sitter-info">
                <div class="sitter-name">${escapeHtml(sitter.ten)}</div>
                <div class="sitter-area"><span>📍 </span>${escapeHtml(sitter.khuVuc)}, Đà Nẵng</div>
                <div class="sitter-meta">
                    ${renderSao(sitter.danhGia)}
                    <span class="sitter-rating-text">${escapeHtml(String(sitter.danhGia || 0))}</span>
                    <span class="sitter-trips">✅ ${escapeHtml(String(sitter.soDanhGia || 0))} lượt</span>
                </div>
                <p class="sitter-desc">${escapeHtml(sitter.moTaNgan)}</p>
                <div class="sitter-pets">
                    ${petTags}
                </div>
                <div class="sitter-footer">
                    <div class="sitter-price">
                        <span class="price-amount">${formatTien(sitter.giaTu)}</span>
                        <span class="price-unit">mỗi ngày</span>
                    </div>
                    <button type="button" class="btn btn-primary btn-sm"
                            data-action="book-sitter"
                            data-id="${escapeHtml(sitter.sitterId)}"
                            id="book-${escapeHtml(sitter.sitterId)}">
                        Đặt lịch
                    </button>
                </div>
            </div>
        </div>
    `;
}

function bookSitter(id) {
    localStorage.setItem('pawstay_selected_sitter', id);
    window.location.href = 'booking.html';
}

async function loadHomePageData() {
    try {
        const stats = await apiFetch('/stats');
        if (stats) {
            const bEl = document.querySelector('.stat-item:nth-child(1) .stat-num span');
            if (bEl) bEl.setAttribute('data-count', stats.bookings);

            const sEl = document.querySelector('.stat-item:nth-child(2) .stat-num span');
            if (sEl) sEl.setAttribute('data-count', stats.sitters);

            const pEl = document.querySelector('.stat-item:nth-child(3) .stat-num span');
            if (pEl) pEl.setAttribute('data-count', '98');
        }

        const featuredSitters = await apiFetch('/sitters/featured');
        const track = document.getElementById('featured-track');
        if (track && featuredSitters) {
            track.innerHTML = featuredSitters.map(buildSitterCard).join('');
            if (typeof createCarousel === 'function') {
                requestAnimationFrame(() => {
                    createCarousel({
                        trackId: 'featured-track',
                        prevId:  'featured-prev',
                        nextId:  'featured-next',
                        dotsId:  'featured-dots',
                        itemsPerView: 3,
                        gap: 20
                    });
                });
            }
        }

        const topReviews = await apiFetch('/reviews');
        const reviewsGrid = document.getElementById('reviews-grid');
        if (reviewsGrid && topReviews && topReviews.length) {
            reviewsGrid.innerHTML = topReviews.map(review => {
                const initials = review.avatar || getInitials(review.nguoiDung);
                return `
                    <article class="review-card">
                        <div class="review-quote" aria-hidden="true">"</div>
                        <p class="review-body">${escapeHtml(review.comment)}</p>
                        <div class="review-header review-header-mt">
                            <div class="review-avatar" aria-hidden="true">${escapeHtml(initials)}</div>
                            <div>
                                <div class="review-name">${escapeHtml(review.nguoiDung)}</div>
                                <div class="review-pet">Review</div>
                            </div>
                            <div class="review-stars-right">
                                ${renderSao(review.rating)}
                            </div>
                        </div>
                    </article>
                `;
            }).join('');
        }
    } catch (error) {
        console.error("Lỗi khi load data trang chủ:", error);
    }
}

function escapeHtml(unsafe) {
    return (unsafe || '').toString()
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;")
        .replace(/'/g, "&#039;");
}

