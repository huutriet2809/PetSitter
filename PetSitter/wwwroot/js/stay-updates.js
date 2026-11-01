const urlParams = new URLSearchParams(window.location.search);
const bookingId = urlParams.get('bookingId') || urlParams.get('id');
let selectedFiles = [];
let me = null;

async function init() {
    const timelineContainer = document.getElementById('timeline-container');
    if (!bookingId) {
        if (timelineContainer) {
            timelineContainer.innerHTML = '<div class="error-text">Không tìm thấy mã đặt lịch.</div>';
        }
        return;
    }

    if (typeof getMe === 'function') {
        me = await getMe();
    }
    if (!me) return;

    loadUpdates();
    checkRole();
}

async function checkRole() {
    const postFormContainer = document.getElementById('post-form-container');
    if (me && me.role === 'SITTER' && postFormContainer) {
        postFormContainer.classList.remove('hidden');
    }
}

function handleFileSelect(event) {
    const files = Array.from(event.target.files);
    if (selectedFiles.length + files.length > 3) {
        alert("Bạn chỉ được chọn tối đa 3 ảnh.");
        return;
    }

    for (let f of files) {
        if (f.size > 2097152) {
            alert("Ảnh " + f.name + " vượt quá 2MB.");
            continue;
        }
        const ext = f.name.split('.').pop().toLowerCase();
        if (!['jpg', 'jpeg', 'png', 'webp'].includes(ext)) {
            alert("Chỉ chấp nhận ảnh jpg, png, webp.");
            continue;
        }
        selectedFiles.push(f);
    }

    renderPreviews();
    event.target.value = '';
}

function renderPreviews() {
    const container = document.getElementById('preview-container');
    if (!container) return;
    container.innerHTML = '';

    selectedFiles.forEach((f, index) => {
        const url = URL.createObjectURL(f);
        const wrap = document.createElement('div');
        wrap.className = 'preview-wrap';

        const img = document.createElement('img');
        img.src = url;
        img.className = 'preview-item';
        img.alt = 'Preview';

        const del = document.createElement('button');
        del.type = 'button';
        del.textContent = '×';
        del.className = 'preview-del-btn';
        del.setAttribute('aria-label', 'Xóa ảnh này');
        del.addEventListener('click', () => {
            selectedFiles.splice(index, 1);
            renderPreviews();
        });

        wrap.appendChild(img);
        wrap.appendChild(del);
        container.appendChild(wrap);
    });
}

async function submitUpdate() {
    const noteInput = document.getElementById('note-input');
    const note = noteInput ? noteInput.value.trim() : '';

    if (!note && selectedFiles.length === 0) {
        alert("Cần có ghi chú hoặc ảnh.");
        return;
    }

    const btn = document.getElementById('btn-submit');
    if (btn) {
        btn.disabled = true;
        btn.textContent = 'Đang tải lên...';
    }

    const formData = new FormData();
    if (note) formData.append('note', note);
    selectedFiles.forEach(f => formData.append('photos', f));

    try {
        await apiFetch('/sitter/bookings/' + encodeURIComponent(bookingId) + '/updates', {
            method: 'POST',
            body: formData
        });

        if (noteInput) noteInput.value = '';
        selectedFiles = [];
        renderPreviews();
        loadUpdates();
    } catch (err) {
        alert(err.message || 'Lỗi khi gửi cập nhật.');
    } finally {
        if (btn) {
            btn.disabled = false;
            btn.textContent = 'Đăng tải';
        }
    }
}

async function loadUpdates() {
    const container = document.getElementById('timeline-container');
    if (!container) return;
    container.innerHTML = '<div class="loading-text">Đang tải nhật ký...</div>';

    try {
        const data = await apiFetch('/bookings/' + encodeURIComponent(bookingId) + '/updates');
        if (!data || data.length === 0) {
            container.innerHTML = '<div class="empty-text">Chưa có cập nhật nào.</div>';
            return;
        }

        container.innerHTML = '';
        data.forEach(item => {
            const el = document.createElement('div');
            el.className = 'timeline-item';

            const header = document.createElement('div');
            header.className = 'update-header';

            const createdDate = new Date(item.createdAt);
            const timeStr = createdDate.toLocaleString('vi-VN');

            const timeSpan = document.createElement('span');
            timeSpan.className = 'update-time';
            timeSpan.textContent = timeStr;
            header.appendChild(timeSpan);

            if (item.canDelete) {
                const delBtn = document.createElement('button');
                delBtn.className = 'btn-delete';
                delBtn.textContent = 'Xóa';
                delBtn.setAttribute('data-action', 'delete-update');
                delBtn.setAttribute('data-id', item.id);
                header.appendChild(delBtn);
            }
            el.appendChild(header);

            if (item.note) {
                const noteDiv = document.createElement('div');
                noteDiv.className = 'update-note';
                noteDiv.textContent = item.note;
                el.appendChild(noteDiv);
            }

            if (item.photoPaths && item.photoPaths.length > 0) {
                const photoGrid = document.createElement('div');
                photoGrid.className = 'photo-grid';
                item.photoPaths.forEach(path => {
                    if (path.startsWith('/uploads/')) {
                        const img = document.createElement('img');
                        img.src = path;
                        img.alt = 'Ảnh cập nhật';
                        img.setAttribute('data-action', 'view-photo');
                        img.setAttribute('data-src', path);
                        photoGrid.appendChild(img);
                    }
                });
                el.appendChild(photoGrid);
            }

            container.appendChild(el);
        });
    } catch (err) {
        const alertMsg = document.getElementById('alert-message');
        if (err.message && err.message.includes('404')) {
            if (alertMsg) {
                alertMsg.innerHTML = '<div class="alert-warning-box">Booking không tồn tại hoặc bạn không có quyền xem.</div>';
            }
            container.innerHTML = '';
        } else {
            container.innerHTML = '<div class="error-text">Lỗi: ' + escapeHtml(err.message || 'Không thể tải nhật ký') + '</div>';
        }
    }
}

async function deleteUpdate(updateId) {
    if (!confirm('Bạn có chắc chắn muốn xóa bài đăng này?')) return;
    try {
        await apiFetch('/sitter/bookings/' + encodeURIComponent(bookingId) + '/updates/' + encodeURIComponent(updateId), { method: 'DELETE' });
        loadUpdates();
    } catch (err) {
        alert(err.message || 'Lỗi khi xóa bài đăng.');
    }
}

function isValidPhotoUrl(url) {
    if (!url || typeof url !== 'string') return false;
    const trimmed = url.trim();
    const lower = trimmed.toLowerCase();

    // Chặn tuyệt đối các pseudo-protocol nguy hiểm và protocol-relative
    if (lower.startsWith('javascript:') || lower.startsWith('data:') || lower.startsWith('vbscript:') || lower.startsWith('//')) {
        return false;
    }

    // Chỉ cho phép https:// và đường dẫn nội bộ /uploads/... (chặn http:// không an toàn)
    if (lower.startsWith('https://')) {
        return true;
    }
    if (trimmed.startsWith('/uploads/')) {
        return true;
    }
    return false;
}

function viewPhoto(src) {
    if (!isValidPhotoUrl(src)) {
        console.warn('URL ảnh không hợp lệ hoặc bị chặn vì lý do bảo mật:', src);
        return;
    }
    const viewerImg = document.getElementById('viewer-img');
    const photoViewer = document.getElementById('photo-viewer');
    if (viewerImg && photoViewer) {
        viewerImg.src = src;
        photoViewer.classList.add('active');
    }
}

function closeViewer() {
    const photoViewer = document.getElementById('photo-viewer');
    if (photoViewer) {
        photoViewer.classList.remove('active');
    }
}

document.addEventListener('DOMContentLoaded', () => {
    init();

    // Event listeners tĩnh
    const photoInput = document.getElementById('photo-input');
    if (photoInput) {
        photoInput.addEventListener('change', handleFileSelect);
    }

    const addPhotosBtn = document.getElementById('btn-add-photos');
    if (addPhotosBtn && photoInput) {
        addPhotosBtn.addEventListener('click', () => photoInput.click());
    }

    const submitBtn = document.getElementById('btn-submit');
    if (submitBtn) {
        submitBtn.addEventListener('click', submitUpdate);
    }

    const closeViewerBtn = document.getElementById('btn-close-viewer');
    if (closeViewerBtn) {
        closeViewerBtn.addEventListener('click', closeViewer);
    }

    // Event delegation cho timeline container (xem ảnh và xóa update)
    const timelineContainer = document.getElementById('timeline-container');
    if (timelineContainer) {
        timelineContainer.addEventListener('click', (e) => {
            const delBtn = e.target.closest('[data-action="delete-update"]');
            if (delBtn) {
                const id = delBtn.getAttribute('data-id');
                if (id) deleteUpdate(id);
                return;
            }

            const photoImg = e.target.closest('[data-action="view-photo"]');
            if (photoImg) {
                const src = photoImg.getAttribute('data-src');
                if (src) viewPhoto(src);
            }
        });
    }
});

