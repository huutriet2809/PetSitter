const API_BASE_URL = '/api';

let refreshPromise = null;

async function executeTokenRefresh() {
    if (!refreshPromise) {
        refreshPromise = (async () => {
            try {
                const refreshResp = await fetch(`${API_BASE_URL}/auth/refresh`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' }
                });
                if (refreshResp.ok) {
                    const refreshData = await refreshResp.json();
                    if (refreshData && refreshData.accessToken) {
                        localStorage.setItem('pawstay_token', refreshData.accessToken);
                        if (refreshData.role) {
                            try {
                                const cachedUser = JSON.parse(localStorage.getItem('pawstay_user') || '{}');
                                cachedUser.role = refreshData.role;
                                localStorage.setItem('pawstay_user', JSON.stringify(cachedUser));
                            } catch (e) {
                                // ignore parse error
                            }
                        }
                        return refreshData.accessToken;
                    }
                }
                return null;
            } catch (err) {
                return null;
            } finally {
                refreshPromise = null;
            }
        })();
    }
    return refreshPromise;
}

async function apiFetch(endpoint, options = {}) {
    const token = localStorage.getItem('pawstay_token');

    // Chuẩn hóa endpoint nếu caller đã truyền sẵn tiền tố /api
    let normalizedEndpoint = endpoint;
    if (normalizedEndpoint.startsWith('/api/')) {
        normalizedEndpoint = normalizedEndpoint.substring(4);
    } else if (normalizedEndpoint === '/api') {
        normalizedEndpoint = '';
    }

    const headers = {
        ...(options.headers || {})
    };

    // Nếu body không phải FormData thì mặc định đặt Content-Type json
    if (!(options.body instanceof FormData) && !headers['Content-Type']) {
        headers['Content-Type'] = 'application/json';
    }

    if (token) {
        headers['Authorization'] = `Bearer ${token}`;
    }

    const response = await fetch(`${API_BASE_URL}${normalizedEndpoint}`, {
        ...options,
        headers
    });

    let data = null;
    try {
        const text = await response.text();
        data = text ? JSON.parse(text) : null;
    } catch (e) {
        data = null;
    }

    const isAuthEndpoint = normalizedEndpoint.includes('/auth/login') ||
                           normalizedEndpoint.includes('/auth/register') ||
                           normalizedEndpoint.includes('/auth/refresh');

    if (response.status === 401 && !isAuthEndpoint && !options._isRetry) {
        const newAccessToken = await executeTokenRefresh();
        if (newAccessToken) {
            // Thử lại request gốc đúng một lần với token mới
            const retryOptions = {
                ...options,
                _isRetry: true,
                headers: {
                    ...(options.headers || {}),
                    'Authorization': `Bearer ${newAccessToken}`
                }
            };
            return await apiFetch(endpoint, retryOptions);
        }

        // Nếu refresh thất bại: dọn dẹp và đẩy về login
        localStorage.removeItem('pawstay_token');
        localStorage.removeItem('pawstay_user');
        let currentPath = window.location.pathname + window.location.search;
        if (currentPath.startsWith('//') || currentPath.includes('://')) {
            currentPath = 'index.html';
        }
        const returnUrl = encodeURIComponent(currentPath);
        if (!window.location.pathname.endsWith('login.html')) {
            window.location.href = `login.html?returnUrl=${returnUrl}`;
        }
    }

    if (!response.ok) {
        let message = '';

        // 1. Kiểm tra validation errors từ ASP.NET model state
        if (data && data.errors && typeof data.errors === 'object') {
            const errorKeys = Object.keys(data.errors);
            if (errorKeys.length > 0) {
                const firstField = errorKeys[0];
                message = `Dữ liệu không hợp lệ: ${firstField}`;
            }
        }

        // 2. Đọc message từ server nếu có
        if (!message && data && typeof data.message === 'string' && data.message.trim()) {
            message = data.message.trim();
        }

        // 3. Body rỗng hoặc không có JSON hợp lệ -> thông báo mặc định theo mã HTTP
        if (!message) {
            const status = response.status;
            if (status === 400) {
                message = 'Dữ liệu không hợp lệ';
            } else if (status === 401) {
                message = 'Vui lòng đăng nhập lại';
            } else if (status === 403) {
                message = 'Bạn không có quyền thực hiện thao tác này';
            } else if (status === 404) {
                message = 'Không tìm thấy dữ liệu';
            } else if (status === 409) {
                message = 'Thao tác xung đột, vui lòng thử lại';
            } else if (status >= 500 && status < 600) {
                message = 'Lỗi hệ thống, vui lòng thử lại sau';
            } else {
                message = 'Có lỗi xảy ra';
            }
        }

        const error = new Error(message);
        error.status = response.status;
        error.message = message;
        error.data = data;
        throw error;
    }

    return data;
}

function isLoggedIn() {
    return !!localStorage.getItem('pawstay_token');
}

function getCurrentUser() {
    const raw = localStorage.getItem('pawstay_user');
    return raw ? JSON.parse(raw) : null;
}

async function getMe() {
    try {
        const user = await apiFetch('/auth/me');
        if (user) {
            const cachedUser = {
                userId: user.userId,
                fullName: user.fullName,
                email: user.email,
                role: user.role,
                phone: user.phone,
                avatarUrl: user.avatarUrl,
                id: user.userId,
                ten: user.fullName
            };
            localStorage.setItem('pawstay_user', JSON.stringify(cachedUser));
        }
        return user;
    } catch (e) {
        return null;
    }
}

function logout() {
    localStorage.removeItem('pawstay_user');
    localStorage.removeItem('pawstay_token');
    window.location.href = 'index.html';
}