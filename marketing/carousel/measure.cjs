// Reports how far down each slide its content reaches; everything must end above the route rule at y 1150.
const path = require('path')
process.env.PLAYWRIGHT_BROWSERS_PATH = path.join(process.env.LOCALAPPDATA, 'ms-playwright')
const { chromium } = require(path.join(process.env.LOCALAPPDATA, 'npm-cache/_npx/9833c18b2d85bc59/node_modules/playwright'))
;(async () => {
  const b = await chromium.launch({ executablePath: path.join(process.env.LOCALAPPDATA, 'ms-playwright/chromium-1243/chrome-win64/chrome.exe') })
  const p = await b.newPage({ viewport: { width: 1080, height: 1350 } })
  await p.goto('file:///' + path.join(__dirname, 'carousel.html').split(path.sep).join('/'))
  await p.evaluate(() => document.fonts.ready)
  await p.waitForTimeout(800)
  const r = await p.evaluate(() => [...document.querySelectorAll('.slide')].map((s, i) => {
    const t = s.getBoundingClientRect().top
    let m = 0
    for (const e of s.querySelectorAll('.body *')) { if (e.closest('.crop') && e.tagName === 'IMG') continue; const q = e.getBoundingClientRect(); if (q.height > 0) m = Math.max(m, q.bottom - t) }
    return `${i + 1}:${Math.round(m)}${m > 1140 ? '!' : ''}`
  }))
  console.log(r.join('  '))
  await b.close()
})()
