let currentDate = new Date();
let selectedDates = new Set();
let availabilityData = [];

document.addEventListener('DOMContentLoaded', () => {
  const user = typeof getCurrentUser === 'function' ? getCurrentUser() : JSON.parse(localStorage.getItem('pawstay_user') || 'null');
  if (!user || user.role !== 'SITTER') {
    document.body.innerHTML = `
      <div class="no-permission-box">
        <h2>Bạn không có quyền truy cập</h2>
        <br>
        <a href="index.html" class="btn btn-primary">Về trang chủ</a>
      </div>
    `;
    return;
  }

  // Delegation cho tất cả tương tác trong card lịch
  const card = document.querySelector('.card');
  if (card) {
    card.addEventListener('click', (e) => {
      // 1. Click vào ô ngày (hoặc phần tử con bên trong ô ngày)
      const dayCell = e.target.closest('[data-action="toggle-date"]');
      if (dayCell && !dayCell.classList.contains('booked') && !dayCell.classList.contains('disabled')) {
        const dateStr = dayCell.dataset.date;
        if (selectedDates.has(dateStr)) {
          selectedDates.delete(dateStr);
          dayCell.classList.remove('selected');
        } else {
          selectedDates.add(dateStr);
          dayCell.classList.add('selected');
        }
        return;
      }

      // 2. Chuyển tháng trước
      if (e.target.closest('[data-action="prev-month"]')) {
        changeMonth(-1);
        return;
      }

      // 3. Chuyển tháng sau
      if (e.target.closest('[data-action="next-month"]')) {
        changeMonth(1);
        return;
      }

      // 4. Đánh dấu Rảnh
      if (e.target.closest('[data-action="mark-available"]')) {
        saveAvailability(true);
        return;
      }

      // 5. Đánh dấu Bận
      if (e.target.closest('[data-action="mark-busy"]')) {
        saveAvailability(false);
        return;
      }
    });
  }

  renderCalendar();
});

async function loadAvailability(year, month) {
  const mStr = year + '-' + String(month).padStart(2, '0');
  try {
    availabilityData = await apiFetch(`/sitter/availability?month=${mStr}`);
  } catch (err) {
    if (typeof showToast === 'function') {
      showToast("Lỗi tải lịch", 3000, "❌");
    }
    availabilityData = [];
  }
}

async function renderCalendar() {
  const year = currentDate.getFullYear();
  const month = currentDate.getMonth() + 1;
  const monthEl = document.getElementById('current-month');
  if (monthEl) {
    monthEl.textContent = `Tháng ${month}, ${year}`;
  }

  await loadAvailability(year, month);
  selectedDates.clear();

  const daysContainer = document.getElementById('calendar-days');
  if (!daysContainer) return;
  daysContainer.innerHTML = '';

  const firstDay = new Date(year, month - 1, 1).getDay();
  const daysInMonth = new Date(year, month, 0).getDate();
  const today = new Date();
  today.setHours(0,0,0,0);

  // Ô trống trước ngày đầu tiên của tháng
  for (let i = 0; i < firstDay; i++) {
    const emptyDiv = document.createElement('div');
    daysContainer.appendChild(emptyDiv);
  }

  for (let i = 1; i <= daysInMonth; i++) {
    const dateStr = year + '-' + String(month).padStart(2, '0') + '-' + String(i).padStart(2, '0');
    const dayCell = document.createElement('div');
    dayCell.className = 'cal-day';
    dayCell.textContent = i;
    dayCell.setAttribute('data-action', 'toggle-date');
    dayCell.setAttribute('data-date', dateStr);
    
    const currentIterDate = new Date(year, month - 1, i);
    
    if (currentIterDate < today) {
      dayCell.classList.add('disabled');
    } else {
      const avail = availabilityData.find(a => a.date === dateStr);
      if (avail) {
        if (avail.status === 'Available') dayCell.classList.add('available');
        if (avail.status === 'Booked') {
          dayCell.classList.add('booked');
          dayCell.title = "Đã có người đặt lịch";
        }
      }
    }
    daysContainer.appendChild(dayCell);
  }
}

function changeMonth(delta) {
  currentDate.setMonth(currentDate.getMonth() + delta);
  renderCalendar();
}

async function saveAvailability(isAvailable) {
  if (selectedDates.size === 0) {
    if (typeof showToast === 'function') {
      showToast("Vui lòng chọn ngày", 3000, "⚠️");
    } else {
      alert("Vui lòng chọn ngày");
    }
    return;
  }

  if (selectedDates.size > 62) {
    if (typeof showToast === 'function') {
      showToast("Chỉ cập nhật tối đa 62 ngày/lần", 3000, "⚠️");
    } else {
      alert("Chỉ cập nhật tối đa 62 ngày/lần");
    }
    return;
  }

  const payload = {
    dates: Array.from(selectedDates),
    isAvailable: isAvailable
  };

  try {
    await apiFetch('/sitter/availability', { method: 'PUT', body: JSON.stringify(payload) });
    if (typeof showToast === 'function') {
      showToast("Cập nhật lịch thành công!", 3000, "✅");
    }
    renderCalendar();
  } catch (err) {
    if (typeof showToast === 'function') {
      showToast(err.message || 'Lỗi cập nhật lịch', 3000, "❌");
    } else {
      alert(err.message || 'Lỗi cập nhật lịch');
    }
  }
}

