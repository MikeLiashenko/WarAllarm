// node render.js in.svg out.png size [transparent]
const { chromium } = require('playwright');
const fs = require('fs');
(async () => {
  const [inp, out, size] = process.argv.slice(2);
  const px = parseInt(size || '1024', 10);
  const svg = fs.readFileSync(inp, 'utf8');
  const b = await chromium.launch();
  const p = await b.newPage({ viewport: { width: px, height: px }, deviceScaleFactor: 1 });
  await p.setContent(`<html><body style="margin:0;background:transparent">${svg.replace(/<\?xml[^>]*>/, '').replace('<svg ', `<svg style="display:block;width:${px}px;height:${px}px" `)}</body></html>`);
  await p.screenshot({ path: out, omitBackground: true, clip: { x: 0, y: 0, width: px, height: px } });
  await b.close();
})();
