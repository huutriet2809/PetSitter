const fs = require('fs');
const path = require('path');

const wwwroot = path.join(__dirname, '..', 'PetSitter', 'wwwroot');
function getFiles(dir) {
    let results = [];
    const list = fs.readdirSync(dir);
    list.forEach(file => {
        const fullPath = path.join(dir, file);
        const stat = fs.statSync(fullPath);
        if (stat && stat.isDirectory()) {
            results = results.concat(getFiles(fullPath));
        } else if (file.endsWith('.html') || file.endsWith('.js') || file.endsWith('.css')) {
            results.push(fullPath);
        }
    });
    return results;
}

const files = getFiles(wwwroot);
const origins = {};

for (const file of files) {
    const rel = path.relative(wwwroot, file);
    const content = fs.readFileSync(file, 'utf8');
    const matches = content.match(/https?:\/\/[a-zA-Z0-9.\-_:]+/g) || [];
    for (const m of matches) {
        try {
            const u = new URL(m);
            if (!origins[u.origin]) origins[u.origin] = [];
            if (!origins[u.origin].includes(rel)) origins[u.origin].push(rel);
        } catch (e) {}
    }
}

console.log('--- DANH SÁCH NGUỒN NGOÀI TRONG WWWROOT ---');
for (const [origin, list] of Object.entries(origins)) {
    console.log(`\nOrigin: ${origin}`);
    console.log(`Used in: ${list.slice(0, 5).join(', ')}${list.length > 5 ? ' ... (' + list.length + ' files)' : ''}`);
}

