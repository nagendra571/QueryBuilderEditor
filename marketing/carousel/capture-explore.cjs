// Exploratory capture: full pages for each person, to choose crops from. Run: node capture-explore.cjs
const path = require('path')
process.env.PLAYWRIGHT_BROWSERS_PATH = process.env.PLAYWRIGHT_BROWSERS_PATH || path.join(process.env.LOCALAPPDATA, 'ms-playwright')
const { chromium } = require(path.join(process.env.LOCALAPPDATA, 'npm-cache/_npx/9833c18b2d85bc59/node_modules/playwright'))
const SIM = 'https://qb-demo-northwind.azurewebsites.net'
const OUT = path.join(__dirname, 'shots', 'explore')
require('fs').mkdirSync(OUT, { recursive: true })

async function as(browser, person) {
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, deviceScaleFactor: 1 })
  await ctx.addCookies([{ name: 'nw-person', value: person, domain: 'qb-demo-northwind.azurewebsites.net', path: '/', secure: true, httpOnly: true }])
  const page = await ctx.newPage()
  page.setDefaultTimeout(120000)
  return { ctx, page }
}
const wait = (ms) => new Promise((r) => setTimeout(r, ms))

;(async () => {
  const browser = await chromium.launch({ executablePath: path.join(process.env.LOCALAPPDATA, 'ms-playwright/chromium-1243/chrome-win64/chrome.exe') })
  // Reset first so the starting set is exact.
  {
    const { ctx, page } = await as(browser, 'alex')
    await page.goto(SIM + '/reset')
    await page.click('text=Reset the demo')
    await page.waitForSelector('text=Done.')
    await ctx.close()
  }
  for (const p of ['mary', 'raj', 'alex', 'lee']) {
    const { ctx, page } = await as(browser, p)
    await page.goto(SIM + '/')
    await page.screenshot({ path: path.join(OUT, `${p}-northwind.png`) })
    await page.goto(SIM + '/open-querybuilder')
    await page.waitForURL(/qb-demo-hub/)
    await wait(3000)
    await page.screenshot({ path: path.join(OUT, `${p}-hub-home.png`) })
    await page.goto('https://qb-demo-hub.azurewebsites.net/querybuilder/dashboards')
    await page.click('text=Program overview')
    await wait(6000)
    await page.screenshot({ path: path.join(OUT, `${p}-dashboard.png`), fullPage: true })
    if (p !== 'lee') {
      await page.goto('https://qb-demo-hub.azurewebsites.net/querybuilder/')
      await page.click('text=Enrolments by program')
      await wait(5000)
      await page.screenshot({ path: path.join(OUT, `${p}-query.png`), fullPage: true })
    }
    await ctx.close()
  }
  await browser.close()
  console.log('done')
})().catch((e) => { console.error(e); process.exit(1) })
