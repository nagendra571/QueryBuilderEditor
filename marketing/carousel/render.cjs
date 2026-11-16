// Renders carousel.html to a PDF (one 1080×1350 page per slide) and PNG previews. Run: node render.cjs
const path = require('path')
const fs = require('fs')
process.env.PLAYWRIGHT_BROWSERS_PATH = process.env.PLAYWRIGHT_BROWSERS_PATH || path.join(process.env.LOCALAPPDATA, 'ms-playwright')
const { chromium } = require(path.join(process.env.LOCALAPPDATA, 'npm-cache/_npx/9833c18b2d85bc59/node_modules/playwright'))

;(async () => {
  const browser = await chromium.launch({ executablePath: path.join(process.env.LOCALAPPDATA, 'ms-playwright/chromium-1243/chrome-win64/chrome.exe') })
  const page = await browser.newPage({ viewport: { width: 1080, height: 1350 }, deviceScaleFactor: 1 })
  await page.goto('file:///' + path.join(__dirname, 'carousel.html').replace(/\\/g, '/'))
  await page.evaluate(() => document.fonts.ready)
  await page.waitForTimeout(800)
  const out = path.join(__dirname, 'out')
  fs.mkdirSync(path.join(out, 'png'), { recursive: true })
  const n = await page.locator('.slide').count()
  for (let i = 0; i < n; i++) {
    await page.locator('.slide').nth(i).screenshot({ path: path.join(out, 'png', `slide-${String(i + 1).padStart(2, '0')}.png`) })
  }
  await page.pdf({ path: path.join(out, 'QueryBuilder-Hub-carousel.pdf'), width: '1080px', height: '1350px', printBackground: true, preferCSSPageSize: true })
  await browser.close()
  console.log('rendered', n, 'slides')
})().catch((e) => { console.error(e); process.exit(1) })
