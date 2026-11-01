const { chromium } = require('playwright');
const http = require('http');
const https = require('https');
const fs = require('fs');
const path = require('path');

// Danh sách đầy đủ 29 trang HTML của hệ thống PawStay
const allPages = [
    // Trang chủ & Đợt 1 (9 trang công khai / chính sách)
    { name: 'index.html', auth: 'public' },
    { name: 'about.html', auth: 'public' },
    { name: 'contact.html', auth: 'public' },
    { name: 'faq.html', auth: 'public' },
    { name: 'how-it-works.html', auth: 'public' },
    { name: 'privacy.html', auth: 'public' },
    { name: 'terms.html', auth: 'public' },
    { name: 'refund-policy.html', auth: 'public' },
    { name: '404.html', auth: 'public' },

    // Đợt 2 (5 trang tài khoản)
    { name: 'login.html', auth: 'public' },
    { name: 'signup.html', auth: 'public' },
    { name: 'forgot-password.html', auth: 'public' },
    { name: 'reset-password.html', auth: 'public' },
    { name: 'profile.html', auth: 'user' },

    // Đợt 3A (4 trang sitter & thú cưng)
    { name: 'sitters.html', auth: 'public' },
    { name: 'sitter-profile.html', auth: 'public' },
    { name: 'my-pets.html', auth: 'user' },
    { name: 'stay-updates.html', auth: 'user' },

    // Đợt 3B (4 trang đặt lịch)
    { name: 'booking.html', auth: 'public' },
    { name: 'payment.html', auth: 'public' },
    { name: 'confirmation.html', auth: 'public' },
    { name: 'my-bookings.html', auth: 'user' },

    // Đợt 3C (5 trang dashboard & đối tác)
    { name: 'become-sitter.html', auth: 'public' },
    { name: 'owner-dashboard.html', auth: 'user' },
    { name: 'sitter-dashboard.html', auth: 'user' },
    { name: 'sitter-edit.html', auth: 'user' },
    { name: 'sitter-calendar.html', auth: 'user' },

    // Đợt 4 (2 trang quản trị Admin)
    { name: 'admin-messages.html', auth: 'admin' },
    { name: 'admin.html', auth: 'admin' }
];

// Danh sách các tab và tương tác chuyên sâu cho Admin
const adminTabsToTest = [
    { id: 'applications', name: 'Quản trị Sitter (Applications)', phase: 'Đợt 4B (Đã xong)', trigger: async (page) => {
        const approvedBtn = await page.$('[data-action="switch-app-status"][data-status="Approved"]');
        if (approvedBtn) await approvedBtn.click();
        await page.waitForTimeout(300);
        const rejectedBtn = await page.$('[data-action="switch-app-status"][data-status="Rejected"]');
        if (rejectedBtn) await rejectedBtn.click();
        await page.waitForTimeout(300);
        const pendingBtn = await page.$('[data-action="switch-app-status"][data-status="Pending"]');
        if (pendingBtn) await pendingBtn.click();
    }},
    { id: 'users', name: 'Người dùng (Users)', phase: 'Đợt 4B (Đã xong)', trigger: async (page) => {
        const searchInput = await page.$('#user-search-input');
        if (searchInput) {
            await searchInput.fill('test');
            await page.keyboard.press('Enter');
            await page.waitForTimeout(300);
        }
        const resetBtn = await page.$('[data-action="reset-user-filters"]');
        if (resetBtn) await resetBtn.click();
    }},
    { id: 'payments', name: 'Thanh toán & Tạm giữ (Payments)', phase: 'Đợt 4C-1 (Đã xong)', trigger: async (page) => {
        await page.waitForTimeout(400);
    }},
    { id: 'refunds', name: 'Yêu cầu Hoàn tiền (Refunds)', phase: 'Đợt 4C-1 (Đã xong)', trigger: async (page) => {
        const reloadBtn = await page.$('[data-action="reload-payments"]');
        if (reloadBtn) await reloadBtn.click();
        await page.waitForTimeout(300);
    }},
    { id: 'withdrawals', name: 'Rút tiền Sitter (Withdrawals)', phase: 'Đợt 4C-2 (Đã xong)', trigger: async (page) => {
        const approvedBtn = await page.$('[data-action="switch-withdrawal-status"][data-status="APPROVED"]');
        if (approvedBtn) await approvedBtn.click();
        await page.waitForTimeout(300);
        const reqBtn = await page.$('[data-action="switch-withdrawal-status"][data-status="REQUESTED"]');
        if (reqBtn) await reqBtn.click();
        await page.waitForTimeout(300);
        const reloadBtn = await page.$('[data-action="reload-withdrawals"]');
        if (reloadBtn) await reloadBtn.click();
        await page.waitForTimeout(300);
    }},
    { id: 'revenue', name: 'Doanh thu Nền tảng (Revenue)', phase: 'Đợt 4C-3 (Đã xong)', trigger: async (page) => {
        const sweepBtn = await page.$('[data-action="open-sweep-modal"]');
        if (sweepBtn) {
            const isDisabled = await sweepBtn.isDisabled();
            if (isDisabled) {
                console.log('      [revenue] Nút [data-action="open-sweep-modal"] bị disabled (số dư < 10k), bỏ qua click');
            } else {
                await sweepBtn.click();
                await page.waitForTimeout(300);
                const closeBtn = await page.$('[data-action="close-sweep-modal"]');
                if (closeBtn) await closeBtn.click();
            }
        }
    }},
    { id: 'reconcile', name: 'Đối soát & Kiểm toán Sổ cái (Reconcile)', phase: 'Đợt 4D-1 (Đã xong)', trigger: async (page) => {
        // 1. Chạy kiểm toán đối soát sổ cái kép
        const runBtn = await page.$('[data-action="run-reconciliation"]');
        if (runBtn) {
            page.once('dialog', async dialog => {
                await dialog.accept();
            });
            await runBtn.click();
            await page.waitForTimeout(500);
        }

        // 2. Upload file CSV sao kê mẫu
        const fileInput = await page.$('#bank-statement-file');
        const uploadBtn = await page.$('[data-action="upload-bank-statement"]');
        if (fileInput && uploadBtn) {
            const sampleCsvPath = path.resolve(__dirname, 'fixtures/bank-statement-sample.csv');
            if (fs.existsSync(sampleCsvPath)) {
                await fileInput.setInputFiles(sampleCsvPath);
                await uploadBtn.click();
                await page.waitForTimeout(800);

                // 3. Click các nút bộ lọc
                const filterMatched = await page.$('[data-action="filter-bank-items"][data-status="MATCHED"]');
                if (filterMatched) {
                    await filterMatched.click();
                    await page.waitForTimeout(200);
                }
                const filterAll = await page.$('[data-action="filter-bank-items"][data-status="ALL"]');
                if (filterAll) {
                    await filterAll.click();
                    await page.waitForTimeout(200);
                }
            }
        }
    }},
    { id: 'suspense', name: 'Giao dịch bất thường (Suspense)', phase: 'Đợt 4D-2 (Đã xong)', trigger: async (page) => {
        // 1. Chuyển đổi bộ lọc
        const filterPending = await page.$('[data-action="switch-suspense-status"][data-status="REFUND_PENDING"]');
        if (filterPending) {
            await filterPending.click();
            await page.waitForTimeout(300);
        }
        const filterAll = await page.$('[data-action="switch-suspense-status"][data-status="ALL"]');
        if (filterAll) {
            await filterAll.click();
            await page.waitForTimeout(300);
        }
        const filterAbnormal = await page.$('[data-action="switch-suspense-status"][data-status="ABNORMAL"]');
        if (filterAbnormal) {
            await filterAbnormal.click();
            await page.waitForTimeout(300);
        }

        // 2. Mở modal Gán đơn, nhập dữ liệu, đóng (không submit)
        const btnAssign = await page.$('[data-action="open-assign-modal"]');
        if (btnAssign) {
            await btnAssign.click();
            await page.waitForTimeout(300);
            const bookingInput = await page.$('#assign-booking-id');
            const noteInput = await page.$('#assign-note');
            if (bookingInput) await bookingInput.fill('6718a0000000000000000001');
            if (noteInput) await noteInput.fill('Căn cứ gán giao dịch kiểm thử E2E');
            await page.waitForTimeout(200);

            const closeAssignBtn = await page.$('[data-action="close-suspense-assign-modal"]');
            if (closeAssignBtn) await closeAssignBtn.click();
            await page.waitForTimeout(300);
        }

        // 3. Mở modal Hoàn tiền, nhập dữ liệu, đóng (không submit)
        const btnRefund = await page.$('[data-action="open-refund-modal"]');
        if (btnRefund) {
            await btnRefund.click();
            await page.waitForTimeout(300);
            const reasonInput = await page.$('#refund-reason');
            if (reasonInput) await reasonInput.fill('Khách chuyển nhầm tiền, tạo yêu cầu hoàn trả');
            await page.waitForTimeout(200);

            const closeRefundBtn = await page.$('[data-action="close-suspense-refund-modal"]');
            if (closeRefundBtn) await closeRefundBtn.click();
            await page.waitForTimeout(300);
        }

        // 4. Mở modal STK người gửi (giải mã) và kiểm tra làm sạch DOM khi đóng
        const btnDecrypt = await page.$('[data-action="open-decrypt-modal"]');
        if (btnDecrypt) {
            await btnDecrypt.click();
            await page.waitForTimeout(400);

            const closeDecryptBtn = await page.$('[data-action="close-bank-decrypt-modal"]');
            if (closeDecryptBtn) await closeDecryptBtn.click();
            await page.waitForTimeout(300);
        }
    }},
    { id: 'disputes', name: 'Khiếu nại & Tranh chấp (Disputes)', phase: 'Đợt 4D-3 (Đã xong)', trigger: async (page) => {
        // 1. Kiểm tra đơn tranh chấp hiển thị
        await page.waitForTimeout(300);

        // 2. Mở modal SPLIT
        const splitBtn = await page.$('[data-action="open-split-modal"]');
        if (splitBtn) {
            await splitBtn.click();
            await page.waitForTimeout(300);

            // 3. Nhập số tiền hoàn và xem preview
            const refundInput = await page.$('#split-refund-amount');
            if (refundInput) {
                await refundInput.fill('100000');
                await refundInput.dispatchEvent('input');
                await page.waitForTimeout(200);
            }

            // 4. Nhập kết luận và căn cứ hòa giải
            const noteInput = await page.$('#split-note');
            if (noteInput) {
                await noteInput.fill('Căn cứ hòa giải tranh chấp thỏa thuận hai bên 50-50');
            }

            // 5. Checkbox ghi nhận vi phạm
            const violCheck = await page.$('#split-record-violation');
            if (violCheck) {
                await violCheck.check();
                const reasonInput = await page.$('#split-violation-reason');
                if (reasonInput) await reasonInput.fill('Giao thú cưng muộn so với cam kết');
            }
            await page.waitForTimeout(300);

            // 6. Đóng modal (không submit trên DB không phải _e2e)
            const closeBtn = await page.$('[data-action="close-split-modal"]');
            if (closeBtn) await closeBtn.click();
            await page.waitForTimeout(300);
        }
    }}
];

const BASE_URL = process.env.BASE_URL || 'http://localhost:5000';
const ADMIN_EMAIL = process.env.E2E_ADMIN_EMAIL;
const ADMIN_PASSWORD = process.env.E2E_ADMIN_PASSWORD;
const USER_EMAIL = process.env.E2E_USER_EMAIL;
const USER_PASSWORD = process.env.E2E_USER_PASSWORD;

function checkServerHealth(urlStr) {
    return new Promise((resolve) => {
        try {
            const parsed = new URL(urlStr);
            const client = parsed.protocol === 'https:' ? https : http;
            const req = client.get(urlStr, { timeout: 3000 }, (res) => {
                resolve(res.statusCode < 500);
            });
            req.on('error', () => resolve(false));
            req.on('timeout', () => { req.destroy(); resolve(false); });
        } catch {
            resolve(false);
        }
    });
}

async function loginAndGetToken(page, email, password, roleName) {
    if (!email || !password) {
        const missing = [];
        if (!email) missing.push(`E2E_${roleName}_EMAIL`);
        if (!password) missing.push(`E2E_${roleName}_PASSWORD`);
        return { success: false, reason: `Thiếu biến môi trường: ${missing.join(', ')}` };
    }

    try {
        const response = await page.request.post(`${BASE_URL}/api/auth/login`, {
            data: { email, password },
            headers: { 'Content-Type': 'application/json' }
        });

        const status = response.status();
        let bodyText = '';
        try { bodyText = await response.text(); } catch {}

        if (!response.ok()) {
            return {
                success: false,
                reason: `Đăng nhập thất bại (HTTP ${status}): ${bodyText.substring(0, 100)}`
            };
        }

        const data = JSON.parse(bodyText);
        if (!data.token) {
            return { success: false, reason: `API không trả về token: ${bodyText.substring(0, 100)}` };
        }

        return { success: true, data };
    } catch (err) {
        return { success: false, reason: `Lỗi kết nối khi đăng nhập: ${err.message}` };
    }
}

async function runAdminTabAudit(browser, adminTokenData) {
    console.log(`\n======================================================================`);
    console.log(`🔍 KIỂM THỬ PLAYWRIGHT E2E TỪNG TAB ADMIN (ĐÃ REFACTOR & CHƯA REFACTOR)`);
    console.log(`======================================================================\n`);

    const context = await browser.newContext({ viewport: { width: 1280, height: 800 } });
    const page = await context.newPage();

    const adminConsoleErrors = [];
    const cspReports = [];

    page.on('console', msg => {
        if (msg.type() === 'error') {
            const loc = msg.location();
            adminConsoleErrors.push({ text: msg.text(), url: loc ? loc.url : '', location: loc });
        }
    });
    page.on('pageerror', err => {
        adminConsoleErrors.push({ text: err.message || String(err), url: page.url(), stack: err.stack || '', location: {} });
    });
    page.on('request', req => {
        if (req.url().includes('/api/csp-report')) {
            cspReports.push({ url: req.url(), postData: req.postData() });
        }
    });

    await page.addInitScript(() => {
        window.__cspViolations = [];
        document.addEventListener('securitypolicyviolation', (e) => {
            window.__cspViolations.push({
                violatedDirective: e.violatedDirective,
                effectiveDirective: e.effectiveDirective,
                blockedURI: e.blockedURI,
                sourceFile: e.sourceFile || '',
                lineNumber: e.lineNumber || 0,
                columnNumber: e.columnNumber || 0,
                sample: (e.sample || '').trim()
            });
        });
    });

    await page.addInitScript((data) => {
        localStorage.setItem('pawstay_token', data.token);
        localStorage.setItem('pawstay_user', JSON.stringify(data.user || data));
    }, adminTokenData);

    const adminResults = [];

    try {
        await page.goto(`${BASE_URL}/admin.html`, { waitUntil: 'domcontentloaded', timeout: 15000 });
        await page.waitForTimeout(600);

        for (const tab of adminTabsToTest) {
            const startViolations = await page.evaluate(() => (window.__cspViolations || []).length);
            const startConsoleErr = adminConsoleErrors.length;

            // Bấm vào tab qua sidebar menu
            let tabOpened = false;
            const tabLink = await page.$(`[data-tab="${tab.id}"]`);
            if (tabLink) {
                await tabLink.click();
                tabOpened = true;
                await page.waitForTimeout(400);
            }

            // Thực hiện tương tác chuyên biệt của tab
            if (tab.trigger) {
                try {
                    await tab.trigger(page);
                } catch (trigErr) {
                    adminConsoleErrors.push({ text: `[${tab.id}] ${trigErr.message}`, location: {} });
                }
            }

            await page.waitForTimeout(400);

            const endViolations = await page.evaluate(() => (window.__cspViolations || []).length);
            const endConsoleErr = adminConsoleErrors.length;

            const tabViolationsCount = endViolations - startViolations;
            const tabConsoleErrCount = endConsoleErr - startConsoleErr;

            if (tabConsoleErrCount > 0) {
                const newErrors = adminConsoleErrors.slice(startConsoleErr);
                console.log(`\n   ⚠️ [Tab ${tab.name}] Ghi nhận ${tabConsoleErrCount} console error:`);
                newErrors.forEach((ce, i) => {
                    console.log(`      (${i + 1}) Message: ${ce.text}`);
                    console.log(`          URL:     ${ce.url || (ce.location ? ce.location.url : '')}`);
                    if (ce.stack) {
                        console.log(`          Stack:   ${ce.stack.split('\n')[0]}`);
                    }
                });
            }

            adminResults.push({
                tabId: tab.id,
                tabName: tab.name,
                phase: tab.phase,
                opened: tabOpened ? '✅ Mở được' : '❌ Lỗi mở tab',
                cspViolations: tabViolationsCount,
                consoleErrors: tabConsoleErrCount,
                status: tabViolationsCount === 0 ? '✅ PASS' : `⚠️ VIOLATION (${tabViolationsCount})`
            });
        }
    } catch (err) {
        console.error('Lỗi khi kiểm thử chi tiết Admin Tabs:', err);
    } finally {
        await context.close();
    }

    console.table(adminResults);
    return adminResults;
}

async function runCspAudit() {
    console.log(`\n======================================================================`);
    console.log(`🐾 PAWSTAY E2E CSP AUDIT (PLAYWRIGHT HEADLESS CHROMIUM)`);
    console.log(`Target URL: ${BASE_URL}`);
    console.log(`Total Pages: ${allPages.length}`);
    console.log(`======================================================================\n`);

    const isHealthy = await checkServerHealth(BASE_URL);
    if (!isHealthy) {
        console.error(`❌ LỖI KẾT NỐI: Không thể kết nối tới máy chủ tại ${BASE_URL}. Vui lòng khởi động server trước.`);
        process.exit(1);
    }
    console.log(`✅ Health check thành công: Server đang hoạt động tại ${BASE_URL}.\n`);

    const browser = await chromium.launch({ headless: true });
    
    const setupPage = await browser.newPage();
    console.log('--- THÔNG TIN XÁC THỰC TÀI KHOẢN E2E ---');
    console.log(`E2E_ADMIN_EMAIL    : ${ADMIN_EMAIL ? ADMIN_EMAIL : '(chưa đặt)'}`);
    console.log(`E2E_ADMIN_PASSWORD : ${ADMIN_PASSWORD ? '********' : '(chưa đặt)'}`);
    console.log(`E2E_USER_EMAIL     : ${USER_EMAIL ? USER_EMAIL : '(chưa đặt)'}`);
    console.log(`E2E_USER_PASSWORD  : ${USER_PASSWORD ? '********' : '(chưa đặt)'}\n`);

    const adminLoginRes = await loginAndGetToken(setupPage, ADMIN_EMAIL, ADMIN_PASSWORD, 'ADMIN');
    if (adminLoginRes.success) {
        console.log(`🔑 [ADMIN] Đăng nhập thành công (${ADMIN_EMAIL}).`);
        // Tự động seed 4 bản ghi test E2E để render template hàng thật
        try {
            const seedResp = await setupPage.request.post(`${BASE_URL}/api/admin/seed-e2e-data`, {
                headers: { 'Authorization': `Bearer ${adminLoginRes.data.token}` }
            });
            if (seedResp.ok()) {
                console.log(`🌱 [ADMIN] Tự động seed dữ liệu test E2E (REFUND_PENDING, DISPUTED, WITHDRAWAL, SUSPENSE) thành công.`);
            }
        } catch (e) {
            console.warn(`⚠️ [ADMIN] Không thể tự động seed dữ liệu test: ${e.message}`);
        }
    } else {
        console.warn(`⚠️ [ADMIN] Không thể đăng nhập: ${adminLoginRes.reason}`);
    }

    const userLoginRes = await loginAndGetToken(setupPage, USER_EMAIL, USER_PASSWORD, 'USER');
    if (userLoginRes.success) {
        console.log(`🔑 [USER]  Đăng nhập thành công (${USER_EMAIL}).`);
    } else {
        console.warn(`⚠️ [USER]  Không thể đăng nhập: ${userLoginRes.reason}`);
    }
    console.log('----------------------------------------\n');
    await setupPage.close();

    const auditResults = [];
    const allViolationsDetail = [];
    const allConsoleErrorsDetail = [];
    const violationGroups = {};

    let countSuccess = 0;
    let countSkipped = 0;
    let countFail = 0;
    let countViolation = 0;

    for (const item of allPages) {
        const pageName = item.name;
        const authType = item.auth;

        if (authType === 'admin' && !adminLoginRes.success) {
            auditResults.push({
                page: pageName,
                httpStatus: '-',
                consoleErrors: 0,
                cspViolations: 0,
                status: `⏭️ SKIPPED (${adminLoginRes.reason})`
            });
            countSkipped++;
            continue;
        }

        if (authType === 'user' && !userLoginRes.success) {
            auditResults.push({
                page: pageName,
                httpStatus: '-',
                consoleErrors: 0,
                cspViolations: 0,
                status: `⏭️ SKIPPED (${userLoginRes.reason})`
            });
            countSkipped++;
            continue;
        }

        const context = await browser.newContext({ viewport: { width: 1280, height: 720 } });
        const page = await context.newPage();
        const pageConsoleErrors = [];

        page.on('console', msg => {
            if (msg.type() === 'error') {
                const loc = msg.location();
                const errDetail = {
                    page: pageName,
                    text: msg.text(),
                    url: loc ? loc.url : '',
                    lineNumber: loc ? loc.lineNumber : 0,
                    columnNumber: loc ? loc.columnNumber : 0
                };
                pageConsoleErrors.push(errDetail);
                allConsoleErrorsDetail.push(errDetail);
            }
        });
        page.on('pageerror', err => {
            const errDetail = {
                page: pageName,
                text: err.message || String(err),
                url: page.url(),
                stack: err.stack || '',
                lineNumber: 0,
                columnNumber: 0
            };
            pageConsoleErrors.push(errDetail);
            allConsoleErrorsDetail.push(errDetail);
        });

        await page.addInitScript(() => {
            window.__cspViolations = [];
            document.addEventListener('securitypolicyviolation', (e) => {
                window.__cspViolations.push({
                    violatedDirective: e.violatedDirective,
                    effectiveDirective: e.effectiveDirective,
                    blockedURI: e.blockedURI,
                    sourceFile: e.sourceFile || '',
                    lineNumber: e.lineNumber || 0,
                    columnNumber: e.columnNumber || 0,
                    sample: (e.sample || '').trim()
                });
            });
        });

        if (authType === 'admin' && adminLoginRes.success) {
            await page.addInitScript((data) => {
                localStorage.setItem('pawstay_token', data.token);
                localStorage.setItem('pawstay_user', JSON.stringify(data.user || data));
            }, adminLoginRes.data);
        } else if (authType === 'user' && userLoginRes.success) {
            await page.addInitScript((data) => {
                localStorage.setItem('pawstay_token', data.token);
                localStorage.setItem('pawstay_user', JSON.stringify(data.user || data));
            }, userLoginRes.data);
        }

        const targetUrl = `${BASE_URL}/${pageName}`;

        try {
            const resp = await page.goto(targetUrl, { waitUntil: 'domcontentloaded', timeout: 15000 });
            const httpStatus = resp ? resp.status() : 0;

            if (httpStatus >= 400 || httpStatus === 0) {
                auditResults.push({
                    page: pageName,
                    httpStatus: httpStatus || 'ERR',
                    consoleErrors: pageConsoleErrors.length,
                    cspViolations: 0,
                    status: `❌ FAIL (HTTP ${httpStatus})`
                });
                countFail++;
                await context.close();
                continue;
            }

            if (authType === 'admin' || authType === 'user') {
                try {
                    const firstBtn = await page.$('button:not([disabled])');
                    if (firstBtn) await firstBtn.hover();
                } catch {}
            }

            await page.waitForTimeout(600);

            const collected = await page.evaluate(() => window.__cspViolations || []);
            
            collected.forEach(v => {
                allViolationsDetail.push({ page: pageName, ...v });

                const groupKey = `${v.effectiveDirective || v.violatedDirective} | ${v.blockedURI || '(inline)'} | ${v.sourceFile || '(inline/document)'}`;
                if (!violationGroups[groupKey]) {
                    violationGroups[groupKey] = {
                        directive: v.effectiveDirective || v.violatedDirective,
                        blockedURI: v.blockedURI || '(inline)',
                        sourceFile: v.sourceFile || '(inline/document)',
                        sample: v.sample || '',
                        occurrences: 0,
                        affectedPages: new Set()
                    };
                }
                violationGroups[groupKey].occurrences++;
                violationGroups[groupKey].affectedPages.add(pageName);
            });

            if (collected.length > 0) {
                auditResults.push({
                    page: pageName,
                    httpStatus,
                    consoleErrors: pageConsoleErrors.length,
                    cspViolations: collected.length,
                    status: `❌ VIOLATION (${collected.length})`
                });
                countViolation++;
            } else {
                auditResults.push({
                    page: pageName,
                    httpStatus,
                    consoleErrors: pageConsoleErrors.length,
                    cspViolations: 0,
                    status: '✅ PASS'
                });
                countSuccess++;
            }
        } catch (err) {
            auditResults.push({
                page: pageName,
                httpStatus: 'ERR',
                consoleErrors: pageConsoleErrors.length + 1,
                cspViolations: 0,
                status: `❌ FAIL (Timeout/Lỗi: ${err.message.substring(0, 30)}...)`
            });
            countFail++;
        } finally {
            await context.close();
        }
    }

    console.log(`\n--- BẢNG KẾT QUẢ KIỂM THỬ PLAYWRIGHT E2E CSP (29 TRANG) ---`);
    console.table(auditResults);

    // In chi tiết Console Errors nếu có
    if (allConsoleErrorsDetail.length > 0) {
        console.log(`\n======================================================================`);
        console.log(`⚠️ CHI TIẾT CONSOLE / PAGE ERRORS PHÁT HIỆN (${allConsoleErrorsDetail.length} lỗi):`);
        console.log(`======================================================================`);
        allConsoleErrorsDetail.forEach((e, idx) => {
            console.log(`[#${idx + 1}] Trang: ${e.page}`);
            console.log(`     Lỗi:      ${e.text}`);
            console.log(`     URL:      ${e.url || '(không xác định)'}`);
            console.log(`     Vị trí:   Dòng ${e.lineNumber}, Cột ${e.columnNumber}`);
            if (e.stack) {
                console.log(`     Stack:    ${e.stack.split('\n').slice(0, 5).join('\n               ')}`);
            }
            console.log('');
        });
        console.table(allConsoleErrorsDetail.map(e => ({
            Page: e.page,
            Error: e.text.substring(0, 100),
            URL: e.url.substring(0, 60),
            Line: e.lineNumber
        })));
    } else {
        console.log(`\n✅ Không có Console Error nào được ghi nhận.`);
    }

    // 2. Chạy test chuyên sâu cho Admin nếu có token
    let adminTabResults = [];
    if (adminLoginRes.success) {
        adminTabResults = await runAdminTabAudit(browser, adminLoginRes.data);
    } else {
        console.log(`\n⏭️ Bỏ qua kiểm thử chuyên sâu từng tab Admin (chưa có tài khoản ADMIN đăng nhập thành công).`);
    }

    await browser.close();

    // Lưu toàn bộ vào tests/e2e/report.json
    const reportPath = path.join(__dirname, 'report.json');
    const fullReport = {
        timestamp: new Date().toISOString(),
        baseUrl: BASE_URL,
        summary: {
            totalPages: allPages.length,
            pass: countSuccess,
            skipped: countSkipped,
            fail: countFail,
            violation: countViolation,
            totalViolationEvents: allViolationsDetail.length,
            totalConsoleErrors: allConsoleErrorsDetail.length
        },
        pageResults: auditResults,
        consoleErrorsDetail: allConsoleErrorsDetail,
        adminTabResults: adminTabResults,
        allViolationsDetail: allViolationsDetail
    };

    fs.writeFileSync(reportPath, JSON.stringify(fullReport, null, 2), 'utf8');
    console.log(`\n📁 Đã lưu toàn bộ báo cáo chi tiết vào: ${reportPath}`);

    console.log(`\n======================================================================`);
    console.log(`TỔNG KẾT KIỂM THỬ E2E CSP (29 TRANG):`);
    console.log(`  - Số trang thành công (PASS) : ${countSuccess}`);
    console.log(`  - Số trang bỏ qua (SKIPPED)  : ${countSkipped}`);
    console.log(`  - Số trang lỗi mạng (FAIL)   : ${countFail}`);
    console.log(`  - Số trang có vi phạm (VIOL) : ${countViolation}`);
    console.log(`  - Tổng số vi phạm CSP        : ${allViolationsDetail.length}`);
    console.log(`  - Tổng số console errors     : ${allConsoleErrorsDetail.length}`);
    console.log(`======================================================================\n`);
}

runCspAudit().catch(err => {
    console.error('Lỗi ngoại lệ thực thi kiểm thử Playwright:', err);
    process.exit(1);
});
