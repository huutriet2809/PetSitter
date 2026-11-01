const fs = require('fs');
const path = require('path');

const batch3APages = [
    'sitters.html',
    'sitter-profile.html',
    'my-pets.html',
    'stay-updates.html'
];

const wwwroot = path.join(__dirname, '..', 'PetSitter', 'wwwroot');

function auditFile(filename) {
    const filePath = path.join(wwwroot, filename);
    if (!fs.existsSync(filePath)) {
        console.error(`File not found: ${filePath}`);
        return null;
    }

    const content = fs.readFileSync(filePath, 'utf8');

    // 1. Thẻ script inline (không có src="...")
    const inlineScriptRegex = /<script(?![^>]*\bsrc=)[^>]*>([\s\S]*?)<\/script>/gi;
    const inlineScripts = [...content.matchAll(inlineScriptRegex)];

    // 2. Thẻ style inline
    const inlineStyleRegex = /<style[^>]*>([\s\S]*?)<\/style>/gi;
    const inlineStyles = [...content.matchAll(inlineStyleRegex)];

    // 3. Thuộc tính style="..." trong HTML tĩnh
    const styleAttrRegex = /\sstyle=["'][^"']*["']/gi;
    const styleAttrs = [...content.matchAll(styleAttrRegex)];

    // 4. Inline event listeners (on\w+=...) trong HTML tĩnh
    const eventAttrRegex = /\son[a-zA-Z]+=["'][^"']*["']/gi;
    const eventAttrs = [...content.matchAll(eventAttrRegex)];

    // 5. javascript: pseudo protocol trong HTML tĩnh
    const jsProtocolRegex = /href=["']javascript:[^"']*["']/gi;
    const jsProtocols = [...content.matchAll(jsProtocolRegex)];

    // 6. Kiểm tra các template string sinh động trong thẻ script inline (hoặc JS file liên quan)
    let dynamicInlineEvents = 0;
    let dynamicStyleAttrs = 0;

    for (const match of inlineScripts) {
        const scriptBody = match[1];
        // Tìm onclick= hoặc on\w+= bên trong template string/chuỗi HTML sinh ra
        const dynEvents = scriptBody.match(/on[a-zA-Z]+\s*=\s*\\?["'][^"']*\\?["']/gi) || [];
        dynamicInlineEvents += dynEvents.length;

        const dynStyles = scriptBody.match(/style\s*=\s*\\?["'][^"']*\\?["']/gi) || [];
        dynamicStyleAttrs += dynStyles.length;
    }

    const totalViolations = inlineScripts.length + inlineStyles.length + styleAttrs.length + eventAttrs.length + jsProtocols.length + dynamicInlineEvents + dynamicStyleAttrs;

    return {
        file: filename,
        inlineScripts: inlineScripts.length,
        inlineStyles: inlineStyles.length,
        styleAttrs: styleAttrs.length,
        eventAttrs: eventAttrs.length,
        jsProtocols: jsProtocols.length,
        dynamicInlineEvents,
        dynamicStyleAttrs,
        totalViolations
    };
}

console.log('--- CSP VIOLATION SCANNER (BATCH 3A) ---');
let grandTotal = 0;
const results = batch3APages.map(p => {
    const res = auditFile(p);
    if (res) grandTotal += res.totalViolations;
    return res;
});

console.table(results);
console.log(`Grand Total Violations for Batch 3A: ${grandTotal}`);

