const fs = require('fs');
const path = require('path');

const batch3BPages = [
    'booking.html',
    'payment.html',
    'confirmation.html',
    'my-bookings.html'
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

    // 6. Kiểm tra các template string sinh động trong cả thẻ script inline lẫn file js tách riêng
    let dynamicInlineEvents = 0;
    let dynamicStyleAttrs = 0;

    for (const match of inlineScripts) {
        const scriptBody = match[1];
        const dynEvents = scriptBody.match(/\bon[a-zA-Z]+\s*=\s*\\?["'][^"']*\\?["']/gi) || [];
        dynamicInlineEvents += dynEvents.length;

        const dynStyles = scriptBody.match(/\bstyle\s*=\s*\\?["'][^"']*\\?["']/gi) || [];
        dynamicStyleAttrs += dynStyles.length;
    }

    const jsFileName = filename.replace('.html', '.js');
    const jsFilePath = path.join(wwwroot, 'js', jsFileName);
    if (fs.existsSync(jsFilePath)) {
        const jsContent = fs.readFileSync(jsFilePath, 'utf8');
        const dynEvents = jsContent.match(/\bon[a-zA-Z]+\s*=\s*\\?["'][^"']*\\?["']/gi) || [];
        dynamicInlineEvents += dynEvents.length;

        const dynStyles = jsContent.match(/\bstyle\s*=\s*\\?["'][^"']*\\?["']/gi) || [];
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

console.log('--- CSP VIOLATION SCANNER (BATCH 3B: HTML + JS) ---');
let grandTotal = 0;
const results = batch3BPages.map(p => {
    const res = auditFile(p);
    if (res) grandTotal += res.totalViolations;
    return res;
});

console.table(results);
console.log(`Grand Total Violations for Batch 3B: ${grandTotal}`);
