const fs = require('fs');
const path = require('path');

const adminDir = path.join(__dirname, '..', 'PetSitter', 'wwwroot');
const filesToScan = [
    'admin.html',
    'js/admin/core.js',
    'js/admin/applications.js',
    'js/admin/users.js',
    'js/admin/payments.js',
    'js/admin/withdrawals.js',
    'js/admin/revenue.js',
    'js/admin/reconcile.js',
    'js/admin/suspense.js',
    'js/admin/disputes.js'
];

console.log('=== BÁO CÁO QUÉT CHI TIẾT VI PHẠM TỪNG FILE THEO FILE:DÒNG ===\n');

const summary = [];

filesToScan.forEach(relPath => {
    const fullPath = path.join(adminDir, relPath);
    if (!fs.existsSync(fullPath)) {
        console.log('Không tìm thấy file:', relPath);
        return;
    }
    const lines = fs.readFileSync(fullPath, 'utf8').split('\n');
    console.log('======================================================================');
    console.log(`FILE: ${relPath} (Tổng số dòng: ${lines.length})`);
    console.log('======================================================================');

    let countStyle = 0;
    let countEvent = 0;
    let countInlineTag = 0;

    lines.forEach((line, idx) => {
        const lineNum = idx + 1;
        const trimmed = line.trim();
        // Bỏ qua comment đơn dòng
        if (trimmed.startsWith('//') || trimmed.startsWith('/*')) return;

        // 1. style attribute
        const styleMatches = line.match(/\bstyle\s*=\s*(?:"[^"]*"|'[^']*'|\\"[^\\"]*\\")/gi);
        if (styleMatches) {
            styleMatches.forEach(m => {
                countStyle++;
                console.log(`  ${relPath}:${lineNum} [STYLE_ATTR] ${m}`);
            });
        }

        // 2. event attribute
        const eventMatches = line.match(/\bon[a-z]+\s*=\s*(?:"[^"]*"|'[^']*'|\\"[^\\"]*\\")/gi);
        if (eventMatches) {
            eventMatches.forEach(m => {
                countEvent++;
                console.log(`  ${relPath}:${lineNum} [EVENT_ATTR] ${m}`);
            });
        }

        // 3. inline script / style tag trong HTML
        if (relPath.endsWith('.html')) {
            if (line.match(/<style(?![^>]*\bsrc=)[^>]*>/i)) {
                countInlineTag++;
                console.log(`  ${relPath}:${lineNum} [INLINE_TAG] <style>`);
            }
            if (line.match(/<script(?![^>]*\bsrc=)[^>]*>/i)) {
                countInlineTag++;
                console.log(`  ${relPath}:${lineNum} [INLINE_TAG] <script> (inline)`);
            }
        }
    });

    const fileTotal = countStyle + countEvent + countInlineTag;
    console.log(`=> Tổng vi phạm trong ${relPath}: ${fileTotal} (Styles: ${countStyle}, Events: ${countEvent}, Tags: ${countInlineTag})\n`);

    summary.push({
        file: relPath,
        lines: lines.length,
        styleAttrs: countStyle,
        eventAttrs: countEvent,
        inlineTags: countInlineTag,
        total: fileTotal
    });
});

console.log('======================================================================');
console.log('TỔNG HỢP VI PHẠM KHU VỰC ADMIN HIỆN TẠI:');
console.log('======================================================================');
console.table(summary);

