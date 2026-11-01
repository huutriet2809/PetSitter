const fs = require('fs');
const path = require('path');

const wwwroot = path.join(__dirname, '..', 'PetSitter', 'wwwroot');

// Danh sách các trang
const pagesToScan = [
    'about.html',
    'contact.html',
    'faq.html',
    'how-it-works.html',
    'privacy.html',
    'terms.html',
    'refund-policy.html',
    '404.html',
    'login.html',
    'signup.html',
    'forgot-password.html',
    'reset-password.html',
    'profile.html',
    'sitters.html',
    'sitter-profile.html',
    'my-pets.html',
    'stay-updates.html',
    'booking.html',
    'payment.html',
    'confirmation.html',
    'my-bookings.html',
    'become-sitter.html',
    'owner-dashboard.html',
    'sitter-dashboard.html',
    'sitter-edit.html',
    'sitter-calendar.html',
    'admin-messages.html',
    'index.html',
    'admin.html'
];

function auditPageComprehensive(filename) {
    const htmlPath = path.join(wwwroot, filename);
    if (!fs.existsSync(htmlPath)) return null;

    const htmlContent = fs.readFileSync(htmlPath, 'utf8');

    // 1. Script inline trong chính file HTML
    const inlineScripts = [...htmlContent.matchAll(/<script(?![^>]*\bsrc=)[^>]*>([\s\S]*?)<\/script>/gi)];

    // 2. Style inline trong chính file HTML
    const inlineStyles = [...htmlContent.matchAll(/<style[^>]*>([\s\S]*?)<\/style>/gi)];

    // 3. Style attribute trong HTML tĩnh (loại bỏ vùng bên trong <script>)
    const htmlWithoutScripts = htmlContent.replace(/<script(?![^>]*\bsrc=)[^>]*>([\s\S]*?)<\/script>/gi, '');
    const staticStyleAttrs = [...htmlWithoutScripts.matchAll(/\bstyle\s*=\s*(?:"[^"]*"|'[^']*'|\\"[^\\"]*\\")/gi)];

    // 4. Event handler inline trong HTML tĩnh
    const staticEventAttrs = [...htmlWithoutScripts.matchAll(/\bon[a-z]+\s*=\s*(?:"[^"]*"|'[^']*'|\\"[^\\"]*\\")/gi)];

    // 5. Thẻ <link rel="stylesheet"> trỏ ra ngoài domain 'self' (bất kể thứ tự href và rel)
    const externalStylesheets = [...htmlContent.matchAll(/<link(?=[^>]*\bhref=["'](https?:\/\/[^"']+)["'])(?=[^>]*\brel=["']stylesheet["'])[^>]*>/gi)];

    // 6. Tìm tất cả các file script cục bộ mà trang này nạp vào
    const scriptSrcMatches = [...htmlContent.matchAll(/<script[^>]*\bsrc=["']([^"']+)["'][^>]*>/gi)];
    let sharedScriptsStyleAttrs = 0;
    let sharedScriptsEventAttrs = 0;
    let pageSpecificDynamicStyles = 0;
    let pageSpecificDynamicEvents = 0;

    const scannedScripts = new Set();

    scriptSrcMatches.forEach(m => {
        let src = m[1].split('?')[0]; // bỏ query string ?v=...
        if (src.startsWith('/')) src = src.substring(1);
        const scriptFullPath = path.join(wwwroot, src);

        if (!fs.existsSync(scriptFullPath) || scannedScripts.has(src)) return;
        scannedScripts.add(src);

        const scriptContent = fs.readFileSync(scriptFullPath, 'utf8');
        const sMatches = [...scriptContent.matchAll(/\bstyle\s*=\s*(?:"[^"]*"|'[^']*'|\\"[^\\"]*\\")/gi)];
        const eMatches = [...scriptContent.matchAll(/\bon[a-z]+\s*=\s*(?:"[^"]*"|'[^']*'|\\"[^\\"]*\\")/gi)];

        if (src.includes('components.js') || src.includes('api.js') || src.includes('data.js')) {
            sharedScriptsStyleAttrs += sMatches.length;
            sharedScriptsEventAttrs += eMatches.length;
        } else {
            pageSpecificDynamicStyles += sMatches.length;
            pageSpecificDynamicEvents += eMatches.length;
        }
    });

    const totalViolations = inlineScripts.length 
        + inlineStyles.length 
        + staticStyleAttrs.length 
        + staticEventAttrs.length 
        + externalStylesheets.length
        + sharedScriptsStyleAttrs 
        + sharedScriptsEventAttrs 
        + pageSpecificDynamicStyles 
        + pageSpecificDynamicEvents;

    return {
        file: filename,
        inlineScripts: inlineScripts.length,
        inlineStyles: inlineStyles.length,
        staticStyles: staticStyleAttrs.length,
        staticEvents: staticEventAttrs.length,
        extStylesheets: externalStylesheets.length,
        sharedStyles: sharedScriptsStyleAttrs,
        sharedEvents: sharedScriptsEventAttrs,
        pageDynamicStyles: pageSpecificDynamicStyles,
        pageDynamicEvents: pageSpecificDynamicEvents,
        total: totalViolations
    };
}

module.exports = { auditPageComprehensive, pagesToScan };

if (require.main === module) {
    console.log('\n================================================================================================================');
    console.log(`--- CSP COMPREHENSIVE SCANNER (MỚI: QUÉT CẢ SCRIPT CHÙNG & TÀI NGUYÊN BÊN NGOÀI) ---`);
    console.log('================================================================================================================\n');

    let grandTotal = 0;
    const rows = pagesToScan.map(page => {
        const res = auditPageComprehensive(page);
        if (res) grandTotal += res.total;
        return res;
    }).filter(Boolean);

    console.table(rows);
    console.log(`\nTổng số vi phạm toàn diện trên cả 29 trang: ${grandTotal}\n`);
}
