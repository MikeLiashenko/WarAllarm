const { chromium } = require('playwright');
(async () => {
  const b = await chromium.launch();
  const ctx = await b.newContext({ viewport: { width: 540, height: 960 }, deviceScaleFactor: 2 });
  const p = await ctx.newPage();
  await p.clock.install({ time: new Date('2026-12-06T00:14:00+02:00') });
  await p.goto('http://127.0.0.1:8765/?demo', { waitUntil: 'load' });
  await p.clock.runFor(3000);
  // легенду згорнути — карта чистіша
  await p.evaluate(() => { const l = document.getElementById('legend'); if (l) l.classList.add('min'); });
  for (let i = 0; i < 150; i++) {
    await p.clock.runFor(33);
    await p.screenshot({ path: `${process.argv[2]}/map/${String(i).padStart(4, '0')}.png` });
  }
  await b.close();
})();
