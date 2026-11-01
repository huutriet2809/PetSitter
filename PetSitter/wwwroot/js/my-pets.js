if (typeof isLoggedIn === 'function' && !isLoggedIn()) {
  window.location.href = 'login.html';
}

let myPets = [];

document.addEventListener('DOMContentLoaded', () => {
  loadPets();

  const addBtn = document.getElementById('btn-open-pet-modal');
  if (addBtn) {
    addBtn.addEventListener('click', () => openPetModal());
  }

  const closeBtn = document.getElementById('btn-close-pet-modal');
  if (closeBtn) {
    closeBtn.addEventListener('click', closePetModal);
  }

  const container = document.getElementById('pets-container');
  if (container) {
    container.addEventListener('click', (e) => {
      const editBtn = e.target.closest('[data-action="edit"]');
      if (editBtn) {
        const id = editBtn.getAttribute('data-id');
        if (id) editPet(id);
        return;
      }

      const delBtn = e.target.closest('[data-action="delete"]');
      if (delBtn) {
        const id = delBtn.getAttribute('data-id');
        if (id) deletePet(id);
        return;
      }

      const addPetTrigger = e.target.closest('[data-action="add-pet"]');
      if (addPetTrigger) {
        openPetModal();
      }
    });
  }

  const petForm = document.getElementById('pet-form');
  if (petForm) {
    petForm.addEventListener('submit', async (e) => {
      e.preventDefault();
      const idInput = document.getElementById('pet-id');
      const nameInput = document.getElementById('pet-name');
      const speciesInput = document.getElementById('pet-species');
      const breedInput = document.getElementById('pet-breed');
      const weightInput = document.getElementById('pet-weight');
      const ageInput = document.getElementById('pet-age');
      const healthInput = document.getElementById('pet-health');

      const id = idInput ? idInput.value : '';
      const data = {
        name: nameInput ? nameInput.value.trim() : '',
        species: speciesInput ? speciesInput.value : 'Chó',
        breed: breedInput ? breedInput.value.trim() : '',
        weight: weightInput ? parseFloat(weightInput.value) || 0 : 0,
        age: ageInput ? parseInt(ageInput.value, 10) || 0 : 0,
        healthStatus: healthInput ? healthInput.value.trim() : ''
      };

      try {
        if (id) {
          await apiFetch(`/pets/${encodeURIComponent(id)}`, { method: 'PUT', body: JSON.stringify(data) });
        } else {
          await apiFetch('/pets', { method: 'POST', body: JSON.stringify(data) });
        }
        closePetModal();
        if (typeof showToast === 'function') {
          showToast("Lưu thông tin thành công", 3000, "✅");
        }
        loadPets();
      } catch (err) {
        if (typeof showToast === 'function') {
          showToast(err.message || "Có lỗi xảy ra", 3000, "❌");
        }
      }
    });
  }
});

async function loadPets() {
  const container = document.getElementById('pets-container');
  try {
    myPets = await apiFetch('/pets');
    renderPets();
  } catch (err) {
    if (container) {
      container.innerHTML = '<div class="error-card">Lỗi tải thú cưng</div>';
    }
  }
}

function renderPets() {
  const container = document.getElementById('pets-container');
  if (!container) return;

  if (!myPets || myPets.length === 0) {
    container.innerHTML = `
      <div class="empty-state empty-state-full">
        <h2>Chưa có thú cưng nào</h2>
        <p class="empty-state-desc">Hãy thêm thông tin thú cưng để đặt lịch nhanh chóng hơn.</p>
        <button class="btn btn-primary" data-action="add-pet">Thêm thú cưng ngay</button>
      </div>
    `;
    return;
  }

  container.innerHTML = myPets.map(p => {
    const icon = p.species === 'Chó' ? '🐶' : (p.species === 'Mèo' ? '🐱' : '🐾');
    const breedText = p.breed ? ` (${escapeHtml(p.breed)})` : '';
    const healthText = p.healthStatus ? `<p><strong>Sức khỏe:</strong> ${escapeHtml(p.healthStatus)}</p>` : '';

    return `
      <div class="pet-card">
        <div class="pet-actions">
          <button class="btn btn-ghost btn-sm" data-action="edit" data-id="${escapeHtml(p.petId)}" title="Sửa">✏️</button>
          <button class="btn btn-ghost btn-sm btn-danger-ghost" data-action="delete" data-id="${escapeHtml(p.petId)}" title="Xóa">🗑️</button>
        </div>
        <h3>${icon} ${escapeHtml(p.name)}</h3>
        <p><strong>Loại:</strong> ${escapeHtml(p.species || '')}${breedText}</p>
        <p><strong>Cân nặng:</strong> ${escapeHtml(String(p.weight || 0))} kg | <strong>Tuổi:</strong> ${escapeHtml(String(p.age || 0))} năm</p>
        ${healthText}
      </div>
    `;
  }).join('');
}

function openPetModal(pet = null) {
  const titleEl = document.getElementById('pet-modal-title');
  const idEl = document.getElementById('pet-id');
  const nameEl = document.getElementById('pet-name');
  const speciesEl = document.getElementById('pet-species');
  const breedEl = document.getElementById('pet-breed');
  const weightEl = document.getElementById('pet-weight');
  const ageEl = document.getElementById('pet-age');
  const healthEl = document.getElementById('pet-health');
  const modal = document.getElementById('pet-modal');

  if (titleEl) titleEl.textContent = pet ? 'Sửa thông tin' : 'Thêm thú cưng';
  if (idEl) idEl.value = pet ? pet.petId : '';
  if (nameEl) nameEl.value = pet ? pet.name : '';
  if (speciesEl) speciesEl.value = pet ? pet.species : 'Chó';
  if (breedEl) breedEl.value = pet ? (pet.breed || '') : '';
  if (weightEl) weightEl.value = pet ? pet.weight : '';
  if (ageEl) ageEl.value = pet ? pet.age : '';
  if (healthEl) healthEl.value = pet ? (pet.healthStatus || '') : '';
  if (modal) modal.classList.add('open');
}

function closePetModal() {
  const modal = document.getElementById('pet-modal');
  if (modal) modal.classList.remove('open');
}

function editPet(id) {
  const pet = myPets.find(p => p.petId === id);
  if (pet) openPetModal(pet);
}

async function deletePet(id) {
  if (!confirm("Bạn có chắc chắn muốn xóa thú cưng này?")) return;
  try {
    await apiFetch(`/pets/${encodeURIComponent(id)}`, { method: 'DELETE' });
    if (typeof showToast === 'function') {
      showToast("Đã xóa thú cưng", 3000, "✅");
    }
    loadPets();
  } catch (err) {
    if (typeof showToast === 'function') {
      showToast("Lỗi khi xóa: " + err.message, 3000, "❌");
    }
  }
}

