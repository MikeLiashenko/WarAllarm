const { chromium } = require('playwright');
(async () => {
  const [dir, mode] = process.argv.slice(2);
  const b = await chromium.launch({ args: ['--allow-file-access-from-files'] });
  const p = await b.newPage({ viewport: { width: 540, height: 960 }, deviceScaleFactor: 2 });
  const errs = []; p.on('pageerror', e => errs.push(e.message));
  await p.goto('file://' + dir + '/trailer.html'); await p.evaluate(() => window.ready());
  const FPS = 30, N = 35 * FPS;
  const times = mode === 'preview' ? [2.6, 5.2, 9.2, 11.8, 15.0, 18.8, 21.8, 24.8, 28.6, 34.5] : null;
  const fs = require('fs'); fs.mkdirSync(dir + '/out', { recursive: true });
  if (times) { for (const t of times) { await p.evaluate(t => render(t), t); await p.screenshot({ path: `${dir}/out/p_${t}.png` }); } }
  else for (let i = 0; i < N; i++) { await p.evaluate(t => render(t), i / FPS); await p.screenshot({ path: `${dir}/out/f${String(i).padStart(5, '0')}.png` }); }
  console.log('errs', errs); await b.close();
})();
