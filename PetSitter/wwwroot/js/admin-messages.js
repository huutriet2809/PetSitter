/* PawStay Admin Messages Script */
document.addEventListener('DOMContentLoaded', async () => {
    const user = await getMe();
    if (!user || user.role !== 'ADMIN') {
        window.location.href = 'index.html';
        return;
    }

    const readFilter = document.getElementById('read-filter');
    if (readFilter) {
        readFilter.addEventListener('change', loadMessages);
    }

    const container = document.getElementById('messages-container');
    if (container) {
        container.addEventListener('click', async (e) => {
            const btn = e.target.closest('[data-action="mark-read"]');
            if (btn) {
                const id = btn.getAttribute('data-id');
                if (id) {
                    await markAsRead(id);
                }
            }
        });
    }

    await loadMessages();
});

async function loadMessages() {
    const container = document.getElementById('messages-container');
    if (!container) return;

    const readFilter = document.getElementById('read-filter');
    const isRead = readFilter ? readFilter.value : '';

    try {
        let url = '/admin/messages';
        if (isRead !== '') {
            url += `?isRead=${encodeURIComponent(isRead)}`;
        }

        const messages = await apiFetch(url);

        if (!messages || messages.length === 0) {
            container.innerHTML = '<div class="empty-state">Không có tin nhắn nào.</div>';
            return;
        }

        container.innerHTML = messages.map(msg => {
            const isUnread = !msg.isRead;
            const formattedDate = msg.createdAt ? new Date(msg.createdAt).toLocaleString('vi-VN') : '';
            const unreadBadge = isUnread ? '<span class="badge-new">Mới</span>' : '';
            const markReadButton = isUnread ? `<button type="button" class="btn btn-primary btn-sm" data-action="mark-read" data-id="${escapeHtml(msg.id)}">Đánh dấu đã đọc</button>` : '';

            return `
                <div class="message-card ${isUnread ? 'unread' : ''}" id="msg-${escapeHtml(msg.id)}">
                    <div class="message-header">
                        <div>
                            <div class="message-title">${escapeHtml(msg.subject)}</div>
                            <div class="message-meta">${escapeHtml(msg.name)} &lt;${escapeHtml(msg.email)}&gt;</div>
                        </div>
                        <div class="message-meta-right">
                            <div class="message-meta">${escapeHtml(formattedDate)}</div>
                            ${unreadBadge}
                        </div>
                    </div>
                    <div class="message-body">${escapeHtml(msg.message)}</div>
                    <div class="message-actions">
                        <a href="mailto:${encodeURI(msg.email || '')}" class="btn btn-outline btn-sm">Trả lời qua Email</a>
                        ${markReadButton}
                    </div>
                </div>
            `;
        }).join('');
    } catch (error) {
        container.innerHTML = `<div class="error-state">Lỗi: ${escapeHtml(error.message)}</div>`;
    }
}

async function markAsRead(id) {
    try {
        await apiFetch(`/admin/messages/${encodeURIComponent(id)}/read`, { method: 'PATCH' });
        await loadMessages();
    } catch (err) {
        alert(err.message || 'Không thể đánh dấu đã đọc');
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

