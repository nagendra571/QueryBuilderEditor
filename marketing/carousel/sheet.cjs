// Contact sheets of the rendered slides for review: out/sheet-1.png (slides 1-6), out/sheet-2.png (7-12).
const path = require('path')
process.env.PLAYWRIGHT_BROWSERS_PATH = process.env.PLAYWRIGHT_BROWSERS_PATH || path.join(process.env.LOCALAPPDATA, 'ms-playwright')
const { chromium } = require(path.join(process.env.LOCALAPPDATA, 'npm-cache/_npx/9833c18b2d85bc59/node_modules/playwright'))
;(async () => {
  const browser = await chromium.launch({ executablePath: path.join(process.env.LOCALAPPDATA, 'ms-playwright/chromium-1243/chrome-win64/chrome.exe') })
  const page = await browser.newPage({ viewport: { width: 1500, height: 940 } })
  for (const [n, from, cnt] of [[1, 1, 6], [2, 7, 6], [3, 13, 2]]) {
    const imgs = Array.from({ length: cnt }, (_, k) => {
      const f = 'file:///' + path.join(__dirname, 'out', 'png', `slide-${String(from + k).padStart(2, '0')}.png`).replace(/\\/g, '/')
      return `<img src="${f}" style="width:360px;height:450px;margin:8px;outline:1px solid #999">`
    }).join('')
    require('fs').writeFileSync(path.join(__dirname, 'out', 'sheet.html'), `<body style="margin:0;background:#666;display:flex;flex-wrap:wrap;width:1500px">${imgs}</body>`); await page.goto('file:///' + path.join(__dirname, 'out', 'sheet.html').replace(/\\/g, '/'))
    await page.waitForTimeout(500)
    await page.screenshot({ path: path.join(__dirname, 'out', `sheet-${n}.png`) })
  }
  await browser.close()
})()
