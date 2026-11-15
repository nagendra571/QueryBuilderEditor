# Marketing video hand-off: QueryBuilder Hub

For the agent making the video. Read this whole file first, then `demo/README.md` (the live demo and its four people). The
product owner has already answered the brief questions below; don't ask them again. Use the HyperFrames
`product-launch-video` workflow: after `hyperframes init`, write `BRIEF.md` from the block in section 1, then work through
the workflow's storyboard review with the product owner.

## 1. BRIEF.md (paste after `hyperframes init`)

```markdown
---
workflow: product-launch-video
flow: automation
storyboard: yes
message: "One reporting hub for every app you run, and every user sees only their own data."
angle: one-hub-every-app
length: 60s
destination: linkedin-feed
aspect: "1:1"
language: en
audience: "Product owners, IT leads and engineering managers at organisations that run several line-of-business apps"
voiceover: generated narrator, restructurable (script in the hand-off is a draft)
captions: yes (burned in; LinkedIn autoplays muted)
capture: live demo app screens via a scripted browser, not a site crawl
---

## Intent
Sell, not tour. Problem → the hub → the scoping moment → call to action. The hero moment is the same chart, opened by
two people from the same app, showing different rows (Mary: program 10 only; Raj: program 20 only).

## Customizations
- Product UI is shown only in tight crops and punch-ins, never as a full screen shrunk into the square (unreadable on a phone).
- On-screen text large enough to read on a phone at 1:1.
- Captured screens from the live demo are the main assets.

## Notes
See demo/VIDEO-BRIEF.md in the QueryBuilderEditor repository for capture steps, the draft script, facts and limits.
```

**Answered by the product owner (2026-11-15):** angle "one hub, every app"; about 60 seconds; LinkedIn feed, 1:1;
voiceover plus captions.
**Inferred defaults (the product owner may correct these at the storyboard review):** `storyboard: yes` (a 60-second,
multi-scene piece benefits from review); `flow: automation`; the audience line; the message wording; English; burned-in
captions.

## 2. The story (about 60 seconds, about 140 spoken words)

| # | Time | Picture | Draft voiceover (restructure freely) |
|---|---|---|---|
| 1 | 0–7s | Several app windows (made-up names), each with a "Reports" button; text: "Every app needs reporting." | "Every app your teams use needs reports. Building them again for each one is slow and costly." |
| 2 | 7–15s | QueryBuilder Hub console: the Apps list; Northwind Programs' signing key and "Turned on"; registering a new app (App B). | "QueryBuilder Hub is one reporting home for all of them. An administrator connects each app once." |
| 3 | 15–22s | Northwind Programs: the user switcher, then "Open reports in QueryBuilder →"; the hub opens with "Northwind Programs" and the zone badge. | "From inside their own app, people click Reports and land straight in QueryBuilder, already signed in." |
| 4 | 22–40s | **Hero.** The same chart, "Enrolments per program", for Mary (only Youth Coding Academy, 20) and then Raj (only Adult Literacy, 20), side by side or as a wipe. The "Your zone: ProgramId 10" badge. Alex sees all five. | "Each person sees only their own data. Mary manages program 10, Raj program 20: same report, different rows, enforced by the server, not by the screen." |
| 5 | 40–50s | Building a query without SQL (columns, a total), the bar chart, the Program overview dashboard; Lee (Viewer) sees only the published dashboard. | "Admins and authors build queries, charts and dashboards without writing SQL. Viewers get the dashboards that are published to them." |
| 6 | 50–60s | Logo-free end card: "QueryBuilder Hub", "One hub. Every app. Each user's own data.", "Try the live demo: qb-demo-northwind.azurewebsites.net". | "QueryBuilder Hub. Try the live demo today." |

## 3. Capturing the screens

The demo is live and free to use. Everything shown is made-up data.

| Where | Address |
|---|---|
| Northwind Programs (start here) | https://qb-demo-northwind.azurewebsites.net/ |
| QueryBuilder Hub | https://qb-demo-hub.azurewebsites.net/querybuilder/ |

1. **Wake it first.** The first request after an idle spell takes up to a minute (free hosting). Open both addresses and wait
   before capturing.
2. **Press Reset demo first** (bottom right of any Northwind page, then **Reset the demo**), so the starting queries and the
   published dashboard are exactly as intended.
3. **Pick a person** with **Signed in as** (top right), then **Open reports in QueryBuilder →**. Switch person on the Northwind
   page and click the button again to change who you are in the hub.
   - Alex Morgan: Admin, all programs.
   - Mary Chen: Admin, program 10 (Youth Coding Academy).
   - Raj Patel: Admin, program 20 (Adult Literacy).
   - Lee Brooks: Viewer, program 10. Sees only Dashboards.
4. **Hero shots (scene 4):** as Mary, then as Raj, open Dashboards, then **Program overview**, then "Enrolments per program".
   Also open the query "Enrolments by program", run it, and use **SQL preview** to show the program filter. Capture the zone
   badge, bottom left.
5. **Scene 2 needs a super admin.** Super admin sign-in uses a one-time code that only the product owner can make (`demo/README.md`,
   "Getting a super admin sign-in code"). Ask for one when you reach this scene. It works once, for up to 60 minutes, and the
   session lasts up to 8 hours, so capture all super-admin shots in one sitting. Register the new app as **App B** (key `app-b`)
   and leave it **turned off**. Afterwards tell the product owner the key you used; they remove it with `demo/sql/remove-app.sql`.
   Never show the code itself in the video or in files.
6. **Capture settings:** a fresh browser context, viewport 1440×900 at device scale 2. Recharts animates, so wait about 4
   seconds before capturing a chart. Light theme by default; the moon icon (top right) switches to dark if it suits the look.
   Short screen recordings of the clicks (switcher, Reports button, chart loading) are better than stills for scenes 3–5.
7. **Leave it tidy.** Press Reset demo when you're done, so prospects find the starting set.

## 4. Look and feel

- The hub's own design is "Zoning Map": a navy left rail, map-grey ground, yellow zone accents, and the Public Sans and Barlow
  Condensed fonts. The local untracked `DESIGN.md` and `.impeccable/design.json` at the repository root describe it (read them
  if present). The video's own graphics should sit comfortably next to that look.
- Northwind Programs is meant to look like an ordinary customer app (navy header, grey menu). Keep it visibly a *different*
  app from QueryBuilder; that difference is the story.
- There is no logo. Use the wordmark "QueryBuilder Hub" in text; don't invent a logo.

## 5. Facts you may state, and limits

True and safe to say (from the product docs: `README.md`, `docs/hub-setup-guide.md`, `src/QueryBuilder.Launch/README.md`):
- One central hub serves many apps; each app hands its users over with a signed launch token (RS256 or ES256). Apps built on
  .NET (including .NET Framework 4.8) and other platforms can do it.
- Roles: Admin, Author, Viewer. Viewers see only dashboards published to their app, never SQL.
- Row-level scoping per user (for example by program), enforced on the server when the SQL is built.
- No SQL needed: query builder, 13 chart types, dashboards, parameters, sharing, CSV and Excel downloads, an audit log.
- SQL Server and PostgreSQL data sources.
- Delivered as a NuGet package (`QueryBuilder.Editor`) or a ready-made hub host.

Don't:
- Claim pricing, customers, certifications, performance numbers or AI features. None are documented.
- Use real people or company names. The demo's names are made up; keep them.
- Show secrets: sign-in codes, keys, connection strings, the Azure portal or Kudu.
- Show anyone as super admin except in scene 2.
- Use Redash names, assets or look. The product was compared with Redash during development but must never resemble it.

## 6. Deliverables

- `qb-hub-linkedin-60s.mp4`: 1080×1080, about 60 seconds, voiceover, burned-in captions.
- The caption file (`.srt`) and the final voiceover script, so the post text can reuse them.
- A 1080×1080 thumbnail from the hero moment (scene 4).
- Approval gates: the product owner reviews the storyboard and sketches on the live board before the full build, and approves
  the render.
