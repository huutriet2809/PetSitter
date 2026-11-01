import re

with open('c:/PetSitter/PetSitter/wwwroot/confirmation.html', 'r', encoding='utf-8') as f:
    content = f.read()

new_render = '''    function renderConfirmation(container, booking) {
      const initials = getInitials(booking.sitterName || "?");
      const avatarColor = booking.sitterAvatarUrl ? 'transparent' : '#888';

      // Format dates
      const formatNgayDep = (str) => {
        const d = new Date(str);
        const thu = ['Ch? Nh?t', 'Th? 2', 'Th? 3', 'Th? 4', 'Th? 5', 'Th? 6', 'Th? 7'][d.getDay()];
        return ${thu}, ngày //;
      };

      // Addons details
      let addonsHtml = '';
      if (booking.addons && booking.addons.length > 0) {
        addonsHtml = booking.addons.map(add => {
          const detailSuffix = add.isDaily ?  ( ×  ngày) : '';
          return 
            <div class="summary-row">
              <span class="summary-row-label">
                <span>?</span>
                <div>
                  <span></span>
                  <small style="display: block; font-size: 0.72rem; color: var(--text-muted); margin-top: 1px;">
                    D?ch v? b? sung
                  </small>
                </div>
              </span>
              <span class="summary-row-value">+</span>
            </div>
          ;
        }).join('');
      }

      const avatarHtml = booking.sitterAvatarUrl 
        ? <img src="" alt="Avatar" style="width:100%;height:100%;border-radius:50%;object-fit:cover;"> 
        : initials;
        
      const noteHtml = booking.ghiChu ? 
          <div class="summary-row" style="align-items: flex-start;">
            <span class="summary-row-label">
              <span>??</span> Ghi chú
            </span>
            <span class="summary-row-value" style="text-align: right; max-width: 60%; font-size: 0.85rem; font-style: italic; color: var(--text-secondary);">
              ""
            </span>
          </div>
           : '';

      container.innerHTML = 
        <!-- Big Success Circle -->
        <div class="success-icon-wrap">
          <div class="success-circle">?</div>
        </div>

        <!-- Heading text -->
        <div class="success-heading">
          <h1>Ð?t l?ch thành công! ??</h1>
          <p>
            Yêu c?u cham sóc dã du?c g?i t?i sitter. Sitter s? ph?n h?i và liên h? xác nh?n trong vòng <strong>2 gi?</strong>.
          </p>
        </div>

        <!-- Booking Code Display -->
        <div class="booking-code-wrap">
          <p class="booking-code-label">Mã d?t l?ch c?a b?n</p>
          <div class="booking-code-value" id="booking-code"></div>
          <p style="font-size: 0.8rem; color: var(--text-muted); margin-top: 8px;">
            Vui lòng luu l?i mã này d? tra c?u ho?c liên h? h? tr?.
          </p>
        </div>

        <!-- SUMMARY CARD -->
        <div class="summary-card">
          <div class="summary-header">
            <h2 class="summary-title">Tóm t?t chuy?n di</h2>
            <div class="summary-status">
              <span class="status-dot"></span> Ch? xác nh?n
            </div>
          </div>

          <!-- Sitter Info Box -->
          <div class="sitter-info-box">
            <div class="sitter-avatar-small" style="background: ;">
              
            </div>
            <div>
              <p style="font-size: 0.75rem; color: var(--text-secondary); text-transform: uppercase; letter-spacing: 0.05em; margin-bottom: 2px;">
                Pet-sitter
              </p>
              <p style="font-weight: 700; font-size: 1rem; color: var(--text-primary);">
                
              </p>
            </div>
            <a href="sitter-profile.html?id=" class="btn btn-outline btn-sm" style="margin-left: auto;">
              Xem h? so
            </a>
          </div>

          <!-- Divider -->
          <div class="summary-divider"></div>

          <!-- Dates & Pets -->
          <div class="summary-row">
            <span class="summary-row-label">
              <span>??</span> Th?i gian
            </span>
            <span class="summary-row-value" style="text-align: right;">
              <br/>
              <span style="color: var(--text-muted); font-size: 0.8rem;">d?n</span><br/>
              <br/>
              <span style="color: var(--accent); font-size: 0.85rem;">( ngày)</span>
            </span>
          </div>

          <div class="summary-row">
            <span class="summary-row-label">
              <span>??</span> Thú cung
            </span>
            <span class="summary-row-value">
               (, )
            </span>
          </div>
          
          

          <div class="summary-divider"></div>

          <!-- Prices -->
          <div class="summary-row">
            <span class="summary-row-label">Phí cham sóc ( ngày)</span>
            <span class="summary-row-value"></span>
          </div>

          

          <div class="summary-row">
            <span class="summary-row-label">Phí d?ch v? PawStay</span>
            <span class="summary-row-value"></span>
          </div>

          <div class="summary-divider"></div>

          <!-- Total -->
          <div class="summary-total">
            <span>T?ng thanh toán</span>
            <span></span>
          </div>
        </div>

        <!-- Actions -->
        <div class="action-buttons">
          <a href="index.html" class="btn btn-primary btn-lg">
            V? trang ch?
          </a>
          <button class="btn btn-outline btn-lg" onclick="window.print()">
            ??? In biên nh?n
          </button>
        </div>
      ;
    }'''

content = re.sub(r'function renderConfirmation\(container, booking\) \{.*?(?=</script>)', new_render + '\n  ', content, flags=re.DOTALL)

with open('c:/PetSitter/PetSitter/wwwroot/confirmation.html', 'w', encoding='utf-8') as f:
    f.write(content)
