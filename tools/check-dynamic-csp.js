const fs = require('fs');
const path = require('path');

const jsFiles = ['sitters.js', 'sitter-profile.js', 'my-pets.js', 'stay-updates.js'];
const jsDir = path.join(__dirname, '..', 'PetSitter', 'wwwroot', 'js');

jsFiles.forEach(f => {
    const fullPath = path.join(jsDir, f);
    const content = fs.readFileSync(fullPath, 'utf8');

    // Tìm các chuỗi onclick=, onchange= trong chuỗi template (bắt đầu bằng ranh giới từ hoặc khoảng trắng)
    const onEvents = [...content.matchAll(/\bon[a-zA-Z]+\s*=\s*\\?["'][^"']*\\?["']/gi)];
    // Tìm các chuỗi style="..."
    const styles = [...content.matchAll(/style\s*=\s*\\?["'][^"']*\\?["']/gi)];

    console.log(`${f}: ${onEvents.length} inline events, ${styles.length} inline styles`);
    if (onEvents.length > 0) {
        onEvents.forEach(m => console.log('  Event match:', m[0]));
    }
    if (styles.length > 0) {
        styles.forEach(m => console.log('  Style match:', m[0]));
    }
});
