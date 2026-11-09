// node preview.js out.png 'name|last message=file.png' ...   — Telegram-like preview: circles at 3 sizes + squircle
const { chromium } = require('playwright');
const fs = require('fs');
(async () => {
  const [out, ...items] = process.argv.slice(2);
  const cards = items.map(it => {
    const i = it.lastIndexOf('='); const [label, msg] = it.slice(0, i).split('|'); const file = it.slice(i + 1);
    const sq = file.replace('_avatar', '_logo');
    const b64 = fs.readFileSync(file).toString('base64');
    const b64sq = fs.existsSync(sq) ? fs.readFileSync(sq).toString('base64') : b64;
    const src = `data:image/png;base64,${b64}`, srcsq = `data:image/png;base64,${b64sq}`;
    return `<div class="card"><img class="c big" src="${src}"><div class="row">
      <img class="c" style="width:54px;height:54px" src="${src}">
      <div class="chat"><b>${label}</b><span>${msg || ''}</span></div></div>
      <div class="row small"><img class="c" style="width:32px;height:32px" src="${src}">
      <img style="width:64px;height:64px" src="${srcsq}"></div></div>`;
  }).join('');
  const html = `<html><body style="margin:0;background:#17212b;font-family:Roboto,Arial,sans-serif;color:#fff">
  <style>.wrap{display:flex;gap:28px;padding:28px}.card{background:#0e1621;border-radius:16px;padding:20px;width:260px}
  .c{border-radius:50%;display:block}.big{width:220px;height:220px;margin:0 auto 18px}
  .row{display:flex;align-items:center;gap:12px;margin-top:10px}.chat{display:flex;flex-direction:column;font-size:15px}
  .chat span{color:#7f91a4;font-size:14px}.small{gap:20px}</style><div class="wrap">${cards}</div></body></html>`;
  const b = await chromium.launch();
  const p = await b.newPage({ viewport: { width: 28 + items.length * 328, height: 420 }, deviceScaleFactor: 1 });
  await p.setContent(html);
  await p.screenshot({ path: out, fullPage: true });
  await b.close();
})();
