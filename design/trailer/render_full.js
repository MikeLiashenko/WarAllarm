const { chromium } = require('playwright');
(async () => {
  const [dir, mode, from, to] = process.argv.slice(2);
  const b = await chromium.launch({ args: ['--allow-file-access-from-files'] });
  const p = await b.newPage({ viewport: { width: 960, height: 540 }, deviceScaleFactor: 2 });
  const errs = []; p.on('pageerror', e => errs.push(e.message));
  await p.goto('file://' + dir + '/trailer_full.html'); await p.evaluate(() => window.ready());
  const FPS = 30, N = Math.round(174.5 * FPS);
  const fs = require('fs'); fs.mkdirSync(dir + '/outf', { recursive: true });
  if (mode === 'preview') {
    const B = k => 0.117 + k * 0.49923, ks = [[0,16],[16,32],[32,48],[48,60],[60,72],[72,84],[84,96],[96,108],[108,112],[112,124],[124,136],[136,148],[148,160],[160,172],[172,184],[184,196],[196,208],[208,220],[220,236],[236,252],[252,268],[268,272],[272,288],[288,312],[312,349]];
    let i = 0; for (const [a, c] of ks) { const t = B(a) + (B(c) - B(a)) * .8; await p.evaluate(t => render(t), t); await p.screenshot({ path: `${dir}/outf/p_${String(i++).padStart(2,'0')}.png` }); }
  } else for (let i = +(from || 0); i < +(to || N); i++) { await p.evaluate(t => render(t), i / FPS); await p.screenshot({ path: `${dir}/outf/f${String(i).padStart(5, '0')}.png` }); }
  console.log('errs', errs); await b.close();
})();
