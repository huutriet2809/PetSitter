const fs = require('fs');
const path = require('path');

const batch1Pages = [
    '404.html',
    'terms.html',
    'privacy.html',
    'faq.html',
    'about.html',
    'how-it-works.html',
    'refund-policy.html',
    'contact.html'
];

const batch2Pages = [
    'login.html',
    'signup.html',
    'forgot-password.html',
    'reset-password.html',
    'profile.html'
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

    // 3. Thuộc tính style="..."
    const styleAttrRegex = /\sstyle=["'][^"']*["']/gi;
    const styleAttrs = [...content.matchAll(styleAttrRegex)];

    // 4. Inline event listeners (on\w+=...)
    const eventAttrRegex = /\son[a-zA-Z]+=["'][^"']*["']/gi;
    const eventAttrs = [...content.matchAll(eventAttrRegex)];

    // 5. javascript: pseudo protocol
    const jsProtocolRegex = /href=["']javascript:[^"']*["']/gi;
    const jsProtocols = [...content.matchAll(jsProtocolRegex)];

    const totalViolations = inlineScripts.length + inlineStyles.length + styleAttrs.length + eventAttrs.length + jsProtocols.length;

    return {
        file: filename,
        inlineScripts: inlineScripts.length,
        inlineStyles: inlineStyles.length,
        styleAttrs: styleAttrs.length,
        eventAttrs: eventAttrs.length,
        jsProtocols: jsProtocols.length,
        totalViolations
    };
}

const batchArg = process.argv[2] || '2';
const targetPages = batchArg === '1' ? batch1Pages : batch2Pages;

console.log(`--- CSP VIOLATION SCANNER (BATCH ${batchArg}) ---`);
let grandTotal = 0;
const results = targetPages.map(p => {
    const res = auditFile(p);
    if (res) grandTotal += res.totalViolations;
    return res;
});

console.table(results);
console.log(`Grand Total Violations for Batch ${batchArg}: ${grandTotal}`);
