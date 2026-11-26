const { chromium } = require('playwright');
(async () => {
  const b = await chromium.launch();
  const ctx = await b.newContext({ viewport: { width: 960, height: 540 }, deviceScaleFactor: 2 });
  const p = await ctx.newPage();
  await p.goto('http://127.0.0.1:8765/', { waitUntil: 'load' });
  await p.waitForTimeout(8000);
  for (let i = 0; i < 150; i++) {
    await p.screenshot({ path: `${process.argv[2]}/live/${String(i).padStart(4, '0')}.png` });
    await p.waitForTimeout(60);
  }
  console.log(await p.$eval('#sub', e => e.textContent), '|', await p.$eval('#upd', e => e.textContent), '|', await p.$eval('#err', e => e.textContent));
  await b.close();
})();
