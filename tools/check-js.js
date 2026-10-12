const fs = require('fs');
const path = require('path');
const { execSync } = require('child_process');
const { Linter } = require('eslint');
const espree = require('espree');

const WWWROOT = path.resolve(__dirname, '../PetSitter/wwwroot');
const TEMP_DIR = path.resolve(__dirname, 'temp');

if (!fs.existsSync(TEMP_DIR)) {
  fs.mkdirSync(TEMP_DIR, { recursive: true });
}

console.log('=== BẮT ĐẦU KIỂM TRA TOÀN DIỆN JS TRONG WWWROOT ===\n');

// ==========================================
// 0. SELF-TEST: XÁC MINH PHÁT HIỆN LỖI BIẾN KHÔNG KHAI BÁO
// ==========================================
if (!process.argv.includes('--sub-check')) {
  console.log('--- BƯỚC 0: TỰ KIỂM TRA (SELF-TEST) ---');
  const selfTestFile = path.join(WWWROOT, 'assets', '__selftest_temp_error__.js');
  try {
    fs.writeFileSync(selfTestFile, 'const testUndef = variableThatDoesNotExist999;\n', 'utf8');
    let caughtExpectedError = false;
    try {
      execSync(`node "${__filename}" --sub-check`, { stdio: 'pipe' });
    } catch (err) {
      if (err.status !== 0) {
        caughtExpectedError = true;
      }
    }

    if (!caughtExpectedError) {
      console.error('[SELF-TEST THẤT BẠI]: Script không báo lỗi khi có biến không khai báo!');
      process.exit(1);
    }
    console.log('[SELF-TEST THÀNH CÔNG]: Script phát hiện chính xác biến không khai báo và thoát mã lỗi > 0.\n');
  } finally {
    if (fs.existsSync(selfTestFile)) {
      try { fs.unlinkSync(selfTestFile); } catch (e) {}
    }
  }
}

// ==========================================
// 1. KIỂM TRA CÚ PHÁP BẰNG NODE --CHECK
// ==========================================
console.log('--- PHẦN 1: KIỂM TRA CÚ PHÁP (node --check) ---');
let totalSyntaxErrors = 0;
const syntaxErrorFiles = [];

// 1.1 Kiểm tra các file .js trong assets
const assetsDir = path.join(WWWROOT, 'assets');
if (fs.existsSync(assetsDir)) {
  const jsFiles = fs.readdirSync(assetsDir).filter(f => f.endsWith('.js'));
  for (const f of jsFiles) {
    const fullPath = path.join(assetsDir, f);
    try {
      execSync(`node --check "${fullPath}"`, { stdio: 'pipe' });
      console.log(`[PASS JS] ${f}`);
    } catch (err) {
      totalSyntaxErrors++;
      syntaxErrorFiles.push(f);
      console.error(`[FAIL JS] ${f}:`);
      console.error(err.stderr ? err.stderr.toString() : err.message);
    }
  }
}

// 1.2 Kiểm tra các inline script trong file .html
const htmlFiles = fs.readdirSync(WWWROOT).filter(f => f.endsWith('.html'));

for (const htmlFile of htmlFiles) {
  const fullHtmlPath = path.join(WWWROOT, htmlFile);
  const content = fs.readFileSync(fullHtmlPath, 'utf8');

  const scriptRegex = /<script(?![^>]*\bsrc\b)[^>]*>([\s\S]*?)<\/script>/gi;
  let match;
  let scriptIndex = 0;
  let fileHasError = false;

  while ((match = scriptRegex.exec(content)) !== null) {
    scriptIndex++;
    const code = match[1];
    if (!code.trim()) continue;

    const beforeScript = content.substring(0, match.index);
    const startLineNumber = beforeScript.split('\n').length;

    if (!fs.existsSync(TEMP_DIR)) fs.mkdirSync(TEMP_DIR, { recursive: true });
    const tempJsPath = path.join(TEMP_DIR, `${htmlFile}_script_${scriptIndex}.js`);
    fs.writeFileSync(tempJsPath, code, 'utf8');

    try {
      execSync(`node --check "${tempJsPath}"`, { stdio: 'pipe' });
    } catch (err) {
      fileHasError = true;
      totalSyntaxErrors++;
      const stderr = err.stderr ? err.stderr.toString() : err.message;
      const lineMatch = stderr.match(/:(\d+)(?::(\d+))?/);
      let relativeLine = lineMatch ? parseInt(lineMatch[1], 10) : 1;
      let actualLine = startLineNumber + relativeLine - 1;

      console.error(`[FAIL HTML] ${htmlFile} (Script #${scriptIndex}, dòng ~${actualLine} trong HTML):`);
      console.error(stderr);
    }
  }

  if (fileHasError) {
    syntaxErrorFiles.push(htmlFile);
  } else {
    console.log(`[PASS HTML] ${htmlFile}`);
  }
}

try {
  fs.rmSync(TEMP_DIR, { recursive: true, force: true });
} catch (e) {}

console.log(`\nTổng số lỗi cú pháp node --check: ${totalSyntaxErrors}\n`);

// ==========================================
// 2. KIỂM TRA BIẾN KHÔNG KHAI BÁO (ESLINT no-undef)
// ==========================================
console.log('--- PHẦN 2: KIỂM TRA BIẾN KHÔNG KHAI BÁO (ESLint no-undef) ---');

const linter = new Linter();

const BROWSER_GLOBALS = {
  window: 'readonly',
  document: 'readonly',
  location: 'readonly',
  localStorage: 'readonly',
  sessionStorage: 'readonly',
  console: 'readonly',
  fetch: 'readonly',
  setTimeout: 'readonly',
  setInterval: 'readonly',
  clearTimeout: 'readonly',
  clearInterval: 'readonly',
  alert: 'readonly',
  confirm: 'readonly',
  prompt: 'readonly',
  URL: 'readonly',
  URLSearchParams: 'readonly',
  FormData: 'readonly',
  Image: 'readonly',
  Date: 'readonly',
  Math: 'readonly',
  JSON: 'readonly',
  Set: 'readonly',
  Map: 'readonly',
  Array: 'readonly',
  Object: 'readonly',
  String: 'readonly',
  Number: 'readonly',
  Boolean: 'readonly',
  RegExp: 'readonly',
  Error: 'readonly',
  parseInt: 'readonly',
  parseFloat: 'readonly',
  encodeURIComponent: 'readonly',
  decodeURIComponent: 'readonly',
  Intl: 'readonly',
  File: 'readonly',
  FileReader: 'readonly',
  Blob: 'readonly',
  Event: 'readonly',
  CustomEvent: 'readonly',
  requestAnimationFrame: 'readonly',
  cancelAnimationFrame: 'readonly',
  navigator: 'readonly',
  history: 'readonly',
  IntersectionObserver: 'readonly'
};

const DATA_GLOBALS = {
  SITTERS_DATA: 'readonly',
  REVIEWS_DATA: 'readonly',
  FAQ_DATA: 'readonly',
  STATS: 'readonly',
  ADDONS: 'writable',
  escapeHtml: 'readonly',
  formatTien: 'readonly',
  renderSao: 'readonly',
  getInitials: 'readonly',
  getReviewsBySitter: 'readonly',
  tinhSoNgay: 'readonly',
  showToast: 'readonly',
  isLoggedIn: 'readonly',
  getCurrentUser: 'readonly',
  setLoggedIn: 'readonly',
  logout: 'readonly',
  getBookingHistory: 'readonly',
  addToBookingHistory: 'readonly'
};

const API_GLOBALS = {
  API_BASE_URL: 'readonly',
  apiFetch: 'readonly',
  isLoggedIn: 'readonly',
  getCurrentUser: 'readonly',
  getMe: 'readonly',
  logout: 'readonly'
};

const COMPONENTS_GLOBALS = {
  NAV_LINKS: 'readonly',
  FOOTER_LINKS: 'readonly',
  getCurrentPage: 'readonly',
  renderNavbar: 'readonly',
  renderFooter: 'readonly',
  renderToast: 'readonly',
  initScrollAnimations: 'readonly',
  initStatsCounters: 'readonly',
  createCarousel: 'readonly'
};

let totalUndefErrors = 0;

// 2.1 Kiểm tra assets/*.js
if (fs.existsSync(assetsDir)) {
  const jsFiles = fs.readdirSync(assetsDir).filter(f => f.endsWith('.js'));
  for (const jsFile of jsFiles) {
    const code = fs.readFileSync(path.join(assetsDir, jsFile), 'utf8');
    const messages = linter.verify(code, {
      languageOptions: {
        ecmaVersion: 2022,
        sourceType: 'script',
        globals: {
          ...BROWSER_GLOBALS,
          ...(jsFile === 'components.js' ? { ...DATA_GLOBALS, ...API_GLOBALS } : {})
        }
      },
      rules: {
        'no-undef': 'error'
      }
    });

    const undefs = messages.filter(m => m.ruleId === 'no-undef');
    if (undefs.length > 0) {
      totalUndefErrors += undefs.length;
      console.error(`[FAIL no-undef] assets/${jsFile}:`);
      undefs.forEach(u => console.error(`  - Dòng ${u.line}: ${u.message}`));
    } else {
      console.log(`[PASS no-undef] assets/${jsFile}`);
    }
  }
}

// 2.2 Kiểm tra từng HTML inline script theo các thư viện đã import
for (const htmlFile of htmlFiles) {
  const content = fs.readFileSync(path.join(WWWROOT, htmlFile), 'utf8');

  const importedScripts = [];
  const srcRegex = /<script[^>]+src=["']([^"']+)["'][^>]*>/gi;
  let srcMatch;
  while ((srcMatch = srcRegex.exec(content)) !== null) {
    importedScripts.push(srcMatch[1]);
  }

  const pageGlobals = { ...BROWSER_GLOBALS };
  if (importedScripts.some(s => s.includes('data.js'))) {
    Object.assign(pageGlobals, DATA_GLOBALS);
  }
  if (importedScripts.some(s => s.includes('api.js'))) {
    Object.assign(pageGlobals, API_GLOBALS);
  }
  if (importedScripts.some(s => s.includes('components.js'))) {
    Object.assign(pageGlobals, COMPONENTS_GLOBALS);
  }

  const scriptRegex = /<script(?![^>]*\bsrc\b)[^>]*>([\s\S]*?)<\/script>/gi;
  let match;
  let scriptIdx = 0;
  let htmlHasUndef = false;

  while ((match = scriptRegex.exec(content)) !== null) {
    scriptIdx++;
    const inlineCode = match[1];
    if (!inlineCode.trim()) continue;

    const beforeScript = content.substring(0, match.index);
    const startLine = beforeScript.split('\n').length;

    const messages = linter.verify(inlineCode, {
      languageOptions: {
        ecmaVersion: 2022,
        sourceType: 'script',
        globals: pageGlobals
      },
      rules: {
        'no-undef': 'error'
      }
    });

    const undefs = messages.filter(m => m.ruleId === 'no-undef');
    if (undefs.length > 0) {
      htmlHasUndef = true;
      totalUndefErrors += undefs.length;
      console.error(`[FAIL no-undef] ${htmlFile} (Script #${scriptIdx}):`);
      undefs.forEach(u => {
        console.error(`  - Dòng ${startLine + u.line - 1}: ${u.message}`);
      });
    }
  }

  if (!htmlHasUndef) {
    console.log(`[PASS no-undef] ${htmlFile}`);
  }
}

console.log(`\nTổng số lỗi biến không khai báo (no-undef): ${totalUndefErrors}\n`);

// ==========================================
// 3. GREP CÁC LỖI ĐẶC THÙ
// ==========================================
console.log('--- PHẦN 3: KIỂM TRA MẪU LỖI CÚ PHÁP ĐẶC THÙ (GREP) ---');

const syntaxGrepPatterns = [
  { name: 'Chuỗi "rn</body>" hoặc "`r`n" dính sau thẻ đóng', regex: /(`r`n|rn<\/body>)/gi },
  { name: 'innerHTML = < (gán HTML không bọc nháy)', regex: /innerHTML\s*=\s*</g },
  { name: 'apiFetch(/ (gọi API không bọc nháy)', regex: /apiFetch\(\s*\//g },
  { name: 'textContent = không có nháy (gán chuỗi tiếng Việt/ký tự trần không phải biến)', regex: /textContent\s*=\s*([A-Za-zÀ-ỹĐđ]+(\s+[A-Za-zÀ-ỹĐđ]+)+|[À-ỹĐđ][A-Za-zÀ-ỹĐđ]*)/g },
  { name: 'Cặp backtick rỗng liên tiếp (`` ``)', regex: /``/g }
];

const grepFindings = {};

function scanFileForPatterns(filePath, relPath) {
  const content = fs.readFileSync(filePath, 'utf8');
  const lines = content.split('\n');

  syntaxGrepPatterns.forEach(p => {
    lines.forEach((line, idx) => {
      if (p.regex.test(line)) {
        if (!grepFindings[p.name]) grepFindings[p.name] = [];
        grepFindings[p.name].push({
          file: relPath,
          line: idx + 1,
          content: line.trim()
        });
      }
      p.regex.lastIndex = 0;
    });
  });
}

function walkDir(dir, baseDir) {
  const files = fs.readdirSync(dir);
  for (const f of files) {
    const full = path.join(dir, f);
    const stat = fs.statSync(full);
    if (stat.isDirectory()) {
      if (f !== 'images' && f !== 'uploads') {
        walkDir(full, baseDir);
      }
    } else if (f.endsWith('.html') || f.endsWith('.js')) {
      scanFileForPatterns(full, path.relative(baseDir, full));
    }
  }
}

walkDir(WWWROOT, WWWROOT);

let totalGrepMatches = 0;
for (const [patternName, findings] of Object.entries(grepFindings)) {
  totalGrepMatches += findings.length;
  console.log(`\n--- [MẪU]: ${patternName} (${findings.length} kết quả) ---`);
  findings.slice(0, 15).forEach(f => {
    console.log(`  ${f.file}:${f.line} -> ${f.content.substring(0, 100)}`);
  });
}

// ==========================================
// 4. KIỂM TRA MOJIBAKE TRÊN TOÀN BỘ REPO
// ==========================================
console.log('--- PHẦN 4: KIỂM TRA MOJIBAKE TOÀN BỘ DỰ ÁN ---');
const ROOT_DIR = path.resolve(__dirname, '..');
const MOJIBAKE_EXTS = new Set(['.cs', '.html', '.js', '.css', '.json', '.md']);
const MOJIBAKE_SKIP_DIRS = new Set(['node_modules', 'bin', 'obj', '.git', '.mojibake-backup']);
const MOJIBAKE_REGEX = /\u00E1\u00BB|\u00E1\u00BA|\u00E2\u20AC|[\u00C3\u00C4][\u0080-\u00BF\u0152\u0153\u0160\u0161\u0178\u017D\u017E\u0192\u02C6\u02DC\u2013-\u203A\u20AC\u2122]|\uFFFD/;

let totalMojibakeErrors = 0;
const mojibakeFindings = [];

function walkMojibake(dir) {
  const entries = fs.readdirSync(dir, { withFileTypes: true });
  for (const entry of entries) {
    if (entry.isDirectory()) {
      if (!MOJIBAKE_SKIP_DIRS.has(entry.name)) {
        walkMojibake(path.join(dir, entry.name));
      }
    } else {
      const ext = path.extname(entry.name).toLowerCase();
      if (MOJIBAKE_EXTS.has(ext)) {
        const full = path.join(dir, entry.name);
        const rel = path.relative(ROOT_DIR, full).replace(/\\/g, '/');
        if (rel === 'tools/fix-mojibake.js') continue;
        const content = fs.readFileSync(full, 'utf8');
        const lines = content.split('\n');
        lines.forEach((line, idx) => {
          if (MOJIBAKE_REGEX.test(line)) {
            totalMojibakeErrors++;
            const location = `${rel}:${idx + 1}`;
            console.error(`[MOJIBAKE] ${location}`);
            mojibakeFindings.push(location);
          }
        });
      }
    }
  }
}

walkMojibake(ROOT_DIR);
console.log(`Tổng số dòng mojibake phát hiện: ${totalMojibakeErrors}\n`);

// ==========================================
// 5. KIỂM TRA XSS TRONG INNERHTML / INSERTADJACENTHTML (Yêu cầu 9a)
// ==========================================
console.log('--- PHẦN 5: KIỂM TRA XSS TRONG INNERHTML / INSERTADJACENTHTML ---');
let totalXssErrors = 0;

const ALLOWED_XSS_FUNCS = new Set([
  'escapeHtml',
  'formatTien',
  'renderSao',
  'formatDate',
  'formatNgayDep',
  'formatNgayDoc',
  'encodeURIComponent',
  'getInitials',
  'Number',
  'parseInt',
  'parseFloat'
]);

function isAllowedXssExpr(node, comments) {
  const nodeStart = node.start;
  const nodeEnd = node.end;

  for (const c of comments) {
    if (c.value.trim() === 'safe') {
      if (c.end <= nodeStart && nodeStart - c.end <= 30) {
        return true;
      }
      if (c.start >= nodeStart && c.end <= nodeEnd) {
        return true;
      }
    }
  }

  if (node.type === 'CallExpression') {
    let name = '';
    if (node.callee.type === 'Identifier') {
      name = node.callee.name;
    } else if (node.callee.type === 'MemberExpression') {
      if (node.callee.object.type === 'Identifier' && node.callee.object.name === 'window' && node.callee.property.type === 'Identifier') {
        name = node.callee.property.name;
      } else if (node.callee.property.type === 'Identifier' && (node.callee.property.name === 'toLocaleDateString' || node.callee.property.name === 'toLocaleString' || node.callee.property.name === 'toLocaleTimeString')) {
        return true;
      } else if (node.callee.property.type === 'Identifier' && node.callee.property.name === 'join') {
        return true;
      }
    }
    if (ALLOWED_XSS_FUNCS.has(name)) {
      return true;
    }
  }

  return false;
}

function checkXssInCode(code, relPath, lineOffset) {
  let ast;
  try {
    ast = espree.parse(code, {
      ecmaVersion: 2022,
      sourceType: 'script',
      loc: true,
      range: true,
      tokens: true,
      comment: true
    });
  } catch (e) {
    return;
  }

  const comments = ast.comments || [];

  function walk(node, parent) {
    if (!node) return;

    if (node.type === 'TemplateLiteral' && node.expressions.length > 0) {
      const hasHtmlTag = node.quasis.some(q => q.value.raw.includes('<'));
      let isInnerHtmlContext = false;
      let curr = parent;
      while (curr) {
        if (curr.type === 'AssignmentExpression') {
          if (curr.left && curr.left.property && curr.left.property.name === 'innerHTML') {
            isInnerHtmlContext = true;
          }
          break;
        }
        if (curr.type === 'CallExpression') {
          if (curr.callee && curr.callee.property && curr.callee.property.name === 'insertAdjacentHTML') {
            isInnerHtmlContext = true;
          }
          break;
        }
        curr = curr.parent;
      }

      if (hasHtmlTag || isInnerHtmlContext) {
        for (const expr of node.expressions) {
          if (!isAllowedXssExpr(expr, comments)) {
            totalXssErrors++;
            const exprCode = code.substring(expr.start, expr.end);
            const line = lineOffset + expr.loc.start.line;
            console.error(`[FAIL XSS] ${relPath}:${line} -> \${${exprCode}}`);
          }
        }
      }
    }

    for (const key of Object.keys(node)) {
      if (key === 'parent') continue;
      const val = node[key];
      if (Array.isArray(val)) {
        val.forEach(c => {
          if (c && typeof c === 'object') {
            c.parent = node;
            walk(c, node);
          }
        });
      } else if (val && typeof val === 'object' && val.type) {
        val.parent = node;
        walk(val, node);
      }
    }
  }

  walk(ast, null);
}

// Kiểm tra XSS trong assets/*.js
if (fs.existsSync(assetsDir)) {
  const jsFiles = fs.readdirSync(assetsDir).filter(f => f.endsWith('.js'));
  for (const f of jsFiles) {
    const full = path.join(assetsDir, f);
    const content = fs.readFileSync(full, 'utf8');
    checkXssInCode(content, `assets/${f}`, 0);
  }
}

// Kiểm tra XSS trong inline scripts của *.html
for (const htmlFile of htmlFiles) {
  const fullHtmlPath = path.join(WWWROOT, htmlFile);
  const content = fs.readFileSync(fullHtmlPath, 'utf8');
  const scriptRegex = /<script(?![^>]*\bsrc\b)[^>]*>([\s\S]*?)<\/script>/gi;
  let match;
  while ((match = scriptRegex.exec(content)) !== null) {
    const before = content.substring(0, match.index);
    const lineOffset = before.split('\n').length - 1;
    checkXssInCode(match[1], htmlFile, lineOffset);
  }
}
console.log(`Tổng số lỗi XSS phát hiện: ${totalXssErrors}\n`);

// ==========================================
// 6. KIỂM TRA META VIEWPORT (Yêu cầu 9b)
// ==========================================
console.log('--- PHẦN 6: KIỂM TRA META VIEWPORT ---');
let totalViewportErrors = 0;
const viewportRegex = /<meta\s+[^>]*name=["']viewport["'][^>]*content=["'][^>]*width=device-width[^>]*["']/i;

for (const htmlFile of htmlFiles) {
  const full = path.join(WWWROOT, htmlFile);
  const content = fs.readFileSync(full, 'utf8');
  if (!viewportRegex.test(content)) {
    totalViewportErrors++;
    console.error(`[FAIL VIEWPORT] ${htmlFile}:1 -> Thiếu thẻ <meta name="viewport" content="width=device-width, initial-scale=1.0">`);
  }
}
console.log(`Tổng số trang thiếu meta viewport: ${totalViewportErrors}\n`);

// ==========================================
// 7. KIỂM TRA THỨ TỰ NẠP SCRIPT (Yêu cầu 9c)
// ==========================================
console.log('--- PHẦN 7: KIỂM TRA THỨ TỰ NẠP SCRIPT (api.js trước components.js) ---');
let totalScriptOrderErrors = 0;

for (const htmlFile of htmlFiles) {
  const full = path.join(WWWROOT, htmlFile);
  const content = fs.readFileSync(full, 'utf8');
  const scriptSrcs = [];
  const scriptRegex = /<script[^>]+src=["']([^"']+)["'][^>]*>/gi;
  let match;
  while ((match = scriptRegex.exec(content)) !== null) {
    const before = content.substring(0, match.index);
    const line = before.split('\n').length;
    scriptSrcs.push({ src: match[1], line });
  }

  const compEntry = scriptSrcs.find(s => s.src.includes('components.js'));
  if (compEntry) {
    const apiEntry = scriptSrcs.find(s => s.src.includes('api.js'));
    if (!apiEntry) {
      totalScriptOrderErrors++;
      console.error(`[FAIL SCRIPT ORDER] ${htmlFile}:${compEntry.line} -> Nạp components.js nhưng không nạp api.js`);
    } else {
      const compIdx = scriptSrcs.indexOf(compEntry);
      const apiIdx = scriptSrcs.indexOf(apiEntry);
      if (apiIdx > compIdx) {
        totalScriptOrderErrors++;
        console.error(`[FAIL SCRIPT ORDER] ${htmlFile}:${compEntry.line} -> components.js nạp trước api.js (dòng ${apiEntry.line})`);
      }
    }
  }
}
console.log(`Tổng số lỗi thứ tự nạp script: ${totalScriptOrderErrors}\n`);

// ==========================================
// 8. KIỂM TRA LIÊN KẾT CHẾT & CONTAINER CŨ (Yêu cầu 9d)
// ==========================================
console.log('--- PHẦN 8: KIỂM TRA LIÊN KẾT CHẾT & CONTAINER CŨ ---');
let totalDeadLinkErrors = 0;

const deadPatterns = [
  { name: 'javascript:void(0)', regex: /javascript:void\(0\)/g },
  { name: 'href="#"', regex: /href\s*=\s*["']#["']/g },
  { name: 'navbar-container', regex: /id\s*=\s*["']navbar-container["']/g },
  { name: 'footer-container', regex: /id\s*=\s*["']footer-container["']/g }
];

function scanDeadLinks(filePath, relPath) {
  const content = fs.readFileSync(filePath, 'utf8');
  const lines = content.split('\n');
  deadPatterns.forEach(p => {
    lines.forEach((line, idx) => {
      if (p.regex.test(line)) {
        totalDeadLinkErrors++;
        console.error(`[FAIL DEAD LINK/CONTAINER] ${relPath}:${idx + 1} (${p.name}) -> ${line.trim()}`);
      }
      p.regex.lastIndex = 0;
    });
  });
}

for (const htmlFile of htmlFiles) {
  scanDeadLinks(path.join(WWWROOT, htmlFile), htmlFile);
}
if (fs.existsSync(assetsDir)) {
  const jsFiles = fs.readdirSync(assetsDir).filter(f => f.endsWith('.js'));
  for (const jsFile of jsFiles) {
    scanDeadLinks(path.join(assetsDir, jsFile), `assets/${jsFile}`);
  }
}
console.log(`Tổng số lỗi liên kết chết / container cũ: ${totalDeadLinkErrors}\n`);

console.log(`\n========================================`);
console.log(`TỔNG SỐ LỖI CÚ PHÁP (node --check): ${totalSyntaxErrors}`);
console.log(`TỔNG SỐ LỖI BIẾN KHÔNG KHAI BÁO (no-undef): ${totalUndefErrors}`);
console.log(`TỔNG SỐ LỖI GREP CÚ PHÁP: ${totalGrepMatches}`);
console.log(`TỔNG SỐ DÒNG MOJIBAKE: ${totalMojibakeErrors}`);
console.log(`TỔNG SỐ LỖI XSS: ${totalXssErrors}`);
console.log(`TỔNG SỐ LỖI VIEWPORT: ${totalViewportErrors}`);
console.log(`TỔNG SỐ LỖI THỨ TỰ SCRIPT: ${totalScriptOrderErrors}`);
console.log(`TỔNG SỐ LỖI LIÊN KẾT CHẾT / CONTAINER CŨ: ${totalDeadLinkErrors}`);

const isAllPassed = (
  totalSyntaxErrors === 0 &&
  totalUndefErrors === 0 &&
  totalGrepMatches === 0 &&
  totalMojibakeErrors === 0 &&
  totalXssErrors === 0 &&
  totalViewportErrors === 0 &&
  totalScriptOrderErrors === 0 &&
  totalDeadLinkErrors === 0
);

if (isAllPassed) {
  console.log('>>> KẾT QUẢ: 0 LỖI TRÊN MỌI HẠNG MỤC KIỂM TRA. TOÀN BỘ FILE HỢP LỆ! <<<');
} else {
  console.log('>>> KẾT QUẢ: VẪN CÒN LỖI CẦN XỬ LÝ! <<<');
}
console.log(`========================================\n`);

process.exit(isAllPassed ? 0 : 1);
