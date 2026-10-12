const fs = require('fs');
const path = require('path');

const calls = JSON.parse(fs.readFileSync(path.resolve(__dirname, 'all_api_calls.json'), 'utf8'));

// Phân tích từng call
const analyzed = calls.map(c => {
  let method = 'GET';
  if (/method:\s*['"](POST|PUT|PATCH|DELETE)['"]/i.test(c.context)) {
    method = c.context.match(/method:\s*['"](POST|PUT|PATCH|DELETE)['"]/i)[1].toUpperCase();
  }
  
  let route = '';
  const routeMatch = c.context.match(/apiFetch\(\s*([`'"][^`'"]+[`'"]|[a-zA-Z0-9_]+)/);
  if (routeMatch) {
    route = routeMatch[1];
  }

  let body = 'null';
  const bodyMatch = c.context.match(/body:\s*([^,}]+)/);
  if (bodyMatch) {
    body = bodyMatch[1].trim();
  }

  return {
    file: c.file,
    line: c.line,
    method,
    route,
    body,
    contextSnippet: c.context.substring(0, 180)
  };
});

fs.writeFileSync(path.resolve(__dirname, 'analyzed_calls.json'), JSON.stringify(analyzed, null, 2), 'utf8');
console.log(`Đã phân tích ${analyzed.length} calls.`);
