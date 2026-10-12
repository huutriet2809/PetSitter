$content = Get-Content 'c:\PetSitter\PetSitter\wwwroot\my-bookings.html' -Raw
$newActions = @'
              <div class="booking-actions">
                ${b.trangThai === 'PENDING' || (b.trangThai === 'CONFIRMED' && new Date(b.ngayBatDau) > new Date()) 
                  ? <button class="btn btn-outline btn-sm" onclick="cancelBooking(' + b.bookingId + ')">Hủy lịch</button> 
                  : ''}
                ${b.trangThai === 'COMPLETED' ? (b.hasReview ? <span style="font-size:0.85rem; color:var(--text-muted);">Đã đánh giá</span> : <button class="btn btn-primary btn-sm" onclick="openReviewModal(' + b.bookingId + ')">Viết đánh giá</button>) : ''}
                <a href="sitter-profile.html?id=${b.sitterId}" class="btn btn-ghost btn-sm">Xem Sitter</a>
              </div>
'@
$oldActions = '(?s)<div class="booking-actions">.*?</div>'
$content = [System.Text.RegularExpressions.Regex]::Replace($content, $oldActions, $newActions)

$reviewModal = @'
  <!-- Modal Đánh giá -->
  <div class="modal" id="review-modal">
    <div class="modal-content">
      <div class="modal-header">
        <h3 class="modal-title">Viết đánh giá</h3>
        <button class="modal-close" onclick="closeReviewModal()">✕</button>
      </div>
      <div class="modal-body">
        <form id="review-form">
          <input type="hidden" id="review-booking-id" />
          <div style="margin-bottom: 16px;">
            <label style="display:block; margin-bottom:8px; font-weight:600;">Đánh giá (1-5 sao)</label>
            <select id="review-rating" style="width:100%; padding:10px; border-radius:6px; border:1px solid var(--border); background:var(--bg-primary);">
              <option value="5">5 - Rất hài lòng</option>
              <option value="4">4 - Hài lòng</option>
              <option value="3">3 - Bình thường</option>
              <option value="2">2 - Không hài lòng</option>
              <option value="1">1 - Tệ</option>
            </select>
          </div>
          <div style="margin-bottom: 16px;">
            <label style="display:block; margin-bottom:8px; font-weight:600;">Nhận xét (Tối đa 500 ký tự)</label>
            <textarea id="review-comment" style="width:100%; height:100px; padding:10px; border-radius:6px; border:1px solid var(--border); background:var(--bg-primary);"></textarea>
          </div>
          <button type="submit" class="btn btn-primary" style="width:100%;">Gửi đánh giá</button>
        </form>
      </div>
    </div>
  </div>
  <script src="assets/components.js"></script>
'@
$content = $content.Replace('<script src="assets/components.js"></script>', $reviewModal)

$reviewJs = @'
    function openReviewModal(bookingId) {
      document.getElementById('review-booking-id').value = bookingId;
      document.getElementById('review-rating').value = '5';
      document.getElementById('review-comment').value = '';
      document.getElementById('review-modal').classList.add('open');
    }

    function closeReviewModal() {
      document.getElementById('review-modal').classList.remove('open');
    }

    document.getElementById('review-form').addEventListener('submit', async function(e) {
      e.preventDefault();
      const bookingId = document.getElementById('review-booking-id').value;
      const rating = parseInt(document.getElementById('review-rating').value);
      const comment = document.getElementById('review-comment').value;

      try {
        await apiFetch(/bookings/ + bookingId + /review, {
          method: 'POST',
          body: JSON.stringify({ rating, comment })
        });
        showToast('Đánh giá thành công!', 3000, '⭐');
        closeReviewModal();
        loadMyBookings();
      } catch (err) {
        showToast(err.message, 3000, '❌');
      }
    });

    async function cancelBooking(id) {
'@
$content = $content.Replace('async function cancelBooking(id) {', $reviewJs)

[System.IO.File]::WriteAllText('c:\PetSitter\PetSitter\wwwroot\my-bookings.html', $content, [System.Text.Encoding]::UTF8)