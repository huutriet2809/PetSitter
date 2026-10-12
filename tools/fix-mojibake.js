// tools/fix-mojibake.js  (ASCII only on purpose)
// node tools/fix-mojibake.js           -> chay thu, chi bao cao
// node tools/fix-mojibake.js --write   -> backup roi sua that
const fs = require('fs');
const path = require('path');

const ROOT = path.resolve(__dirname, '..');
const EXTS = new Set(['.cs', '.html', '.js', '.css', '.json', '.md', '.txt', '.cshtml']);
const SKIP = new Set(['node_modules', 'bin', 'obj', '.git', '.vs', '.mojibake-backup', 'uploads']);
const WRITE = process.argv.includes('--write');

const dec = new TextDecoder('windows-1252');
const REV = new Map();
for (let b = 0; b < 256; b++) REV.set(dec.decode(Uint8Array.of(b)), b);
const strict = new TextDecoder('utf-8', { fatal: true });
const NON_ASCII = /[^\x00-\x7F]/;
const MOJI = /\u00E1\u00BB|\u00E1\u00BA|\u00E2\u20AC|[\u00C3\u00C4][\u0080-\u00BF\u0152\u0153\u0160\u0161\u0178\u017D\u017E\u0192\u02C6\u02DC\u2013-\u203A\u20AC\u2122]/;

function tryRun(s) {
    const bytes = [];
    for (const ch of s) {
        const b = REV.get(ch);
        if (b === undefined) return null;
        bytes.push(b);
    }
    try { return strict.decode(Uint8Array.from(bytes)); } catch { return null; }
}

function fixSegment(seg) {
    if (!NON_ASCII.test(seg)) return seg;
    const whole = tryRun(seg);
    if (whole !== null) return whole;
    return seg.split(/(\s+)/).map(tok => {
        if (!NON_ASCII.test(tok)) return tok;
        const t = tryRun(tok);
        return t !== null ? t : tok;
    }).join('');
}

function fixLine(line) {
    let out = '', run = '';
    for (const ch of line) {
        if (REV.has(ch)) run += ch;
        else { out += fixSegment(run) + ch; run = ''; }
    }
    return out + fixSegment(run);
}

function fixText(text) {
    let cur = text;
    for (let i = 0; i < 3; i++) {          // phong truong hop bi hong 2 lan
        const next = cur.split('\n').map(fixLine).join('\n');
        if (next === cur) break;
        cur = next;
    }
    return cur;
}

function* walk(dir) {
    for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
        if (SKIP.has(e.name)) continue;
        const p = path.join(dir, e.name);
        if (e.isDirectory()) yield* walk(p);
        else if (EXTS.has(path.extname(e.name).toLowerCase())) yield p;
    }
}

let changed = 0;
const leftovers = [];
const backupDir = path.join(ROOT, '.mojibake-backup');

for (const file of walk(ROOT)) {
    const buf = fs.readFileSync(file);
    const hadBom = buf.length >= 3 && buf[0] === 0xEF && buf[1] === 0xBB && buf[2] === 0xBF;
    const original = buf.toString('utf8').replace(/^\uFEFF/, '');
    if (!NON_ASCII.test(original)) continue;
    const fixed = fixText(original);
    const rel = path.relative(ROOT, file);
    if (fixed !== original) {
        changed++;
        console.log((WRITE ? 'FIXED     ' : 'WOULD FIX ') + rel);
        if (WRITE) {
            const bk = path.join(backupDir, rel);
            fs.mkdirSync(path.dirname(bk), { recursive: true });
            fs.writeFileSync(bk, buf);
            fs.writeFileSync(file, (hadBom ? '\uFEFF' : '') + fixed, 'utf8');
        }
    }
    fixed.split('\n').forEach((ln, i) => {
        if (MOJI.test(ln) || ln.includes('\uFFFD'))
            leftovers.push(rel + ':' + (i + 1) + ': ' + ln.trim().slice(0, 100));
    });
}

console.log('\nFiles ' + (WRITE ? 'fixed' : 'to fix') + ': ' + changed);
console.log('Lines still suspicious: ' + leftovers.length);
leftovers.slice(0, 50).forEach(l => console.log('  ' + l));