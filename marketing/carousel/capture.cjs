// Final captures at 2x for the carousel. Run: node capture.cjs <file with "Code: ...">  (code optional; console shots need it)
const path = require('path')
const fs = require('fs')
process.env.PLAYWRIGHT_BROWSERS_PATH = process.env.PLAYWRIGHT_BROWSERS_PATH || path.join(process.env.LOCALAPPDATA, 'ms-playwright')
const { chromium } = require(path.join(process.env.LOCALAPPDATA, 'npm-cache/_npx/9833c18b2d85bc59/node_modules/playwright'))
const SIM = 'https://qb-demo-northwind.azurewebsites.net'
const HUB = 'https://qb-demo-hub.azurewebsites.net'
const OUT = path.join(__dirname, 'shots')
fs.mkdirSync(OUT, { recursive: true })
const wait = (ms) => new Promise((r) => setTimeout(r, ms))
const shot = (p, name) => p.screenshot({ path: path.join(OUT, name + '.png') })

async function ctxFor(browser, person) {
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, deviceScaleFactor: 2 })
  if (person) await ctx.addCookies([{ name: 'nw-person', value: person, domain: 'qb-demo-northwind.azurewebsites.net', path: '/', secure: true, httpOnly: true }])
  const page = await ctx.newPage()
  page.setDefaultTimeout(120000)
  return { ctx, page }
}
async function launch(page) {
  await page.goto(SIM + '/open-querybuilder')
  await page.waitForURL(/qb-demo-hub/)
  await wait(2500)
}
async function openDashboard(page) {
  await page.goto(HUB + '/querybuilder/dashboards')
  await page.getByText('Program overview', { exact: true }).first().click()
  await page.getByRole('region', { name: 'Enrolments per program' }).getByText('Loaded').waitFor({ state: 'attached' }).catch(() => {})
  await wait(5000)
}

;(async () => {
  const browser = await chromium.launch({ executablePath: path.join(process.env.LOCALAPPDATA, 'ms-playwright/chromium-1243/chrome-win64/chrome.exe') })

  // Northwind as Mary, with the switcher open-looking state not needed: plain page.
  for (const p of ['mary', 'raj', 'alex', 'lee']) {
    const { ctx, page } = await ctxFor(browser, p)
    await page.goto(SIM + '/')
    await shot(page, `${p}-northwind`)
    await launch(page)
    await shot(page, `${p}-hub-landing`)
    await openDashboard(page)
    await page.getByRole('region', { name: 'Enrolments per program' }).screenshot({ path: path.join(OUT, `${p}-tile-enrolments.png`) })
    await page.getByRole('region', { name: 'Budget by program and year' }).screenshot({ path: path.join(OUT, `${p}-tile-budget.png`) })
    await shot(page, `${p}-dashboard`)
    if (p === 'mary' || p === 'alex') {
      await page.goto(HUB + '/querybuilder/')
      await page.getByText('Enrolments by program', { exact: true }).first().click()
      await wait(3000)
      await shot(page, `${p}-builder`)
      await page.getByRole('button', { name: 'Run' }).click()
      await wait(4000)
      await shot(page, `${p}-results`)
      await page.getByRole('button', { name: 'Show query' }).click()
      await wait(2500)
      await shot(page, `${p}-sql`)
    }
    await ctx.close()
  }

  // Super admin console (needs a one-time code).
  const codeFile = process.argv[2]
  if (codeFile && fs.existsSync(codeFile)) {
    const code = /Code:\s*(\S+)/.exec(fs.readFileSync(codeFile, 'utf8'))[1]
    const { ctx, page } = await ctxFor(browser, null)
    await page.goto(HUB + '/querybuilder/signin')
    await page.locator('input').first().fill(code)
    await page.getByRole('button', { name: /code/i }).click()
    await wait(4000)
    await page.goto(HUB + '/querybuilder/hub/apps')
    await wait(3000)
    await shot(page, 'super-apps')
    await page.getByText('Northwind Programs').first().click()
    await wait(3000)
    await shot(page, 'super-app-detail')
    await page.evaluate(() => window.scrollBy(0, 700))
    await wait(1000)
    await shot(page, 'super-app-detail-2')
    await ctx.close()
  }
  await browser.close()
  console.log('done', fs.readdirSync(OUT).filter((f) => f.endsWith('.png')).length, 'shots')
})().catch((e) => { console.error(e); process.exit(1) })
