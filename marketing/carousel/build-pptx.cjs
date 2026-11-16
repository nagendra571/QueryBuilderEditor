// Builds out/QueryBuilder-Hub-carousel.pptx from the rendered slides (out/png), one full-bleed picture per 4:5 slide,
// so the deck looks exactly like the PDF. Alt text and speaker notes carry each slide's words.
// Run: node build-pptx.cjs <path to a folder where pptxgenjs is installed>
const path = require('path')
const pptxgen = require(path.join(process.argv[2] || '.', 'node_modules', 'pptxgenjs'))

const slides = [
  ['One reporting hub. Every app you run.', 'Multi-tenant, self-service reporting. Each app hands its users over signed in, and every person sees only the rows that are theirs.'],
  ['Every app rebuilds reporting. Every app decides again who sees what.', 'Separate report builders, exports and access rules, each kept in step by hand. One missed check and a user reads another team\'s rows.'],
  ['One hub. Every app a tenant.', 'A super admin registers each app once: its signing key, the addresses it may return users to, its scope keys and a record ceiling.'],
  ['It starts with a button in your own app.', 'Station 1 of 6. People click Reports where they already work; your server decides their role and scope every time. QueryBuilder.Launch: netstandard2.0, no dependencies, .NET Framework 4.7.2 and later.'],
  ['A signed ticket, good for one trip.', 'Station 2 of 6. Launch tokens are signed RS256 or ES256, live at most 120 seconds (60 by default), are accepted once, and must be addressed to this hub. Refusals show one generic page; the reason goes to the audit log; tokens are never logged.'],
  ['Inside, a session that knows its app, its role and its rows.', 'Station 3 of 6. Sessions last 8 hours at most and end after 1 hour without use. Turn an app off, or revoke a signing key, and its sessions end within about 30 seconds.'],
  ['Tenants share a hub. Never a row.', 'Station 4 of 6. A person is the pair (app, user id): the same email in two apps is two people. Another app\'s query, dashboard, share or data source answers 404 and nothing changes.'],
  ['The filter is written into the SQL. On the server.', 'Station 5 of 6. The generated SQL carries WHERE ProgramId IN (@__scope0) with @__scope0 = 10, bound as a parameter, on runs, exports, charts and dashboard tiles. Views no admin has mapped stay hidden from scoped users.'],
  ['Same report. Different rows.', 'Station 6 of 6. Mary (program 10) sees Youth Coding Academy, 20; Raj (program 20) sees Adult Literacy, 20. Alex, launched with no row restriction, sees all five.'],
  ['Self-service, without a line of SQL.', 'Pick a view, columns, totals and filters. Thirteen chart types, parameters, dashboards on a twelve-column grid, CSV and Excel.'],
  ['Viewers get dashboards. Only dashboards.', 'A Viewer can reach exactly eight endpoints: the dashboard list, one dashboard, a tile\'s chart, dashboard parameter options, a query\'s options via a dashboard, a chart render via a dashboard, a chart\'s data download, and their own profile.'],
  ['Governed from one console.', 'Super admins sign in with Microsoft Entra ID, any OpenID Connect provider, or one-time codes issued only on the server. Data sources are locked, moved between apps, with passwords sealed by AES-256-GCM.'],
  ['Built for the estate you already run.', '.NET 6, 8 and 10; .NET Framework 4.7.2 and 4.8; any stack that can sign a JWT. SQL Server and PostgreSQL. A ready-made hub host or the QueryBuilder.Editor package. Automatic migrations or an idempotent SQL script. MIT licensed.'],
  ['Walk the route yourself.', 'Live demo: qb-demo-northwind.azurewebsites.net. NuGet: QueryBuilder.Editor, QueryBuilder.Launch. GitHub: github.com/nagendra571/QueryBuilderEditor.'],
]

;(async () => {
  const pres = new pptxgen()
  pres.defineLayout({ name: 'CAROUSEL_4x5', width: 7.5, height: 9.375 })
  pres.layout = 'CAROUSEL_4x5'
  pres.title = 'QueryBuilder Hub'
  pres.subject = 'One reporting hub. Every app you run.'
  slides.forEach(([title, words], i) => {
    const s = pres.addSlide()
    const file = path.join(__dirname, 'out', 'png', `slide-${String(i + 1).padStart(2, '0')}.png`)
    s.addImage({ path: file, x: 0, y: 0, w: 7.5, h: 9.375, altText: `${title} ${words}`, objectName: `Slide ${i + 1}` })
    s.addNotes(`${title}\n\n${words}`)
  })
  await pres.writeFile({ fileName: path.join(__dirname, 'out', 'QueryBuilder-Hub-carousel.pptx') })
  console.log('written', slides.length, 'slides')
})()
