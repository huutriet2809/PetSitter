const fs = require('fs');
const lines = fs.readFileSync('PetSitter/wwwroot/admin.html', 'utf8').split('\n');

const segments = [
    { group: 'Core & Shell', name: 'HTML Shell, Sidebar & Subtabs', start: 1, end: 55 },
    { group: 'Core & Shell', name: 'Core JS Routing (switchTab, hash, init)', start: 136, end: 246 },
    { group: 'Đợt 4B', name: 'Tab Sitter Applications (Duyệt Sitter)', start: 247, end: 349 },
    { group: 'Đợt 4B', name: 'Tab Users (Quản lý Người dùng & Bộ lọc)', start: 1478, end: 1908 },
    { group: 'Đợt 4C', name: 'Tab Payments & Refunds (Thanh toán & Hoàn tiền)', start: 457, end: 561 },
    { group: 'Đợt 4C', name: 'Tab Withdrawals (Rút tiền & Giải mã ngân hàng)', start: 562, end: 774 },
    { group: 'Đợt 4C', name: 'Tab Revenue (Doanh thu & Quỹ sweep)', start: 775, end: 913 },
    { group: 'Đợt 4D', name: 'Tab Bank Reconciliation (Đối soát ngân hàng 3 bên)', start: 914, end: 1183 },
    { group: 'Đợt 4D', name: 'Tab Suspense (Giao dịch SUSPENSE & Gán/Hoàn)', start: 1184, end: 1477 },
    { group: 'Đợt 4D', name: 'Tab Disputes & Modal Hòa giải SPLIT', start: 56, end: 135 },
    { group: 'Đợt 4D', name: 'Tab Disputes JS Logic (Hòa giải & Phân xử)', start: 350, end: 456 }
];

function scanChunk(text) {
    const inlineStyles = (text.match(/<style[^>]*>[\s\S]*?<\/style>/gi) || []).length;
    const inlineScripts = (text.match(/<script(?![^>]*\bsrc=)[^>]*>[\s\S]*?<\/script>/gi) || []).length;
    
    // Style attributes tĩnh và trong template string
    const styleAttrs = (text.match(/\bstyle\s*=\s*(?:"[^"]*"|'[^']*'|\\"[^\\"]*\\")/gi) || []).length;
    
    // Event attributes tĩnh và trong template string
    const eventAttrs = (text.match(/\bon[a-z]+\s*=\s*(?:"[^"]*"|'[^']*'|\\"[^\\"]*\\")/gi) || []).length;
    
    return { inlineStyles, inlineScripts, styleAttrs, eventAttrs };
}

let grandTotal = 0;
const groupSummary = {};

const detailedRows = segments.map(seg => {
    const chunk = lines.slice(seg.start - 1, seg.end).join('\n');
    const sc = scanChunk(chunk);
    const total = sc.inlineStyles + sc.inlineScripts + sc.styleAttrs + sc.eventAttrs;
    grandTotal += total;

    if (!groupSummary[seg.group]) {
        groupSummary[seg.group] = { styleAttrs: 0, eventAttrs: 0, inlineStyles: 0, inlineScripts: 0, total: 0 };
    }
    groupSummary[seg.group].styleAttrs += sc.styleAttrs;
    groupSummary[seg.group].eventAttrs += sc.eventAttrs;
    groupSummary[seg.group].inlineStyles += sc.inlineStyles;
    groupSummary[seg.group].inlineScripts += sc.inlineScripts;
    groupSummary[seg.group].total += total;

    return {
        group: seg.group,
        segment: seg.name,
        lines: `${seg.start}-${seg.end}`,
        styleAttrs: sc.styleAttrs,
        eventAttrs: sc.eventAttrs,
        total
    };
});

console.log('\n--- BẢNG CHI TIẾT VI PHẠM THEO PHÂN ĐOẠN NGHIỆP VỤ ---');
console.table(detailedRows);

console.log('\n--- BẢNG TỔNG HỢP VI PHẠM THEO ĐỢT TRIỂN KHAI ---');
console.table(Object.keys(groupSummary).map(k => ({
    Dot: k,
    styleAttrs: groupSummary[k].styleAttrs,
    eventAttrs: groupSummary[k].eventAttrs,
    inlineTags: groupSummary[k].inlineStyles + groupSummary[k].inlineScripts,
    total: groupSummary[k].total
})));

console.log(`\nTổng số vi phạm toàn bộ admin.html: ${grandTotal}\n`);

