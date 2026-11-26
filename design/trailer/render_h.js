const { chromium } = require('playwright');
(async () => {
  const [dir, mode] = process.argv.slice(2);
  const b = await chromium.launch({ args: ['--allow-file-access-from-files'] });
  const p = await b.newPage({ viewport: { width: 960, height: 540 }, deviceScaleFactor: 2 });
  const errs = []; p.on('pageerror', e => errs.push(e.message));
  await p.goto('file://' + dir + '/trailer_h.html'); await p.evaluate(() => window.ready());
  const FPS = 30, N = 60 * FPS;
  const times = mode === 'preview' ? [3.5, 7.5, 12.0, 17.0, 23.5, 27.8, 32.5, 37.0, 42.0, 47.8, 52.0, 59.0] : null;
  const fs = require('fs'); fs.mkdirSync(dir + '/outh', { recursive: true });
  if (times) { for (const t of times) { await p.evaluate(t => render(t), t); await p.screenshot({ path: `${dir}/outh/p_${t}.png` }); } }
  else for (let i = 0; i < N; i++) { await p.evaluate(t => render(t), i / FPS); await p.screenshot({ path: `${dir}/outh/f${String(i).padStart(5, '0')}.png` }); }
  console.log('errs', errs); await b.close();
})();
