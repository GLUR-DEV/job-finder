# Job Finder

A personal job-search toolkit for **SEEK** and **Trade Me Jobs** (New Zealand / Australia). It has two parts that work together:

1. **A Chrome extension** (`extension/`) that reads job cards from SEEK and Trade Me search pages, scrapes full job ads, exports CSVs, and runs scheduled background scans.
2. **A Blazor Server web app** (`web/JobFinder.Web/`) that stores every job in SQLite, lets you filter and browse them, plots them on a map, sends phone notifications for new matches, and writes cover letters with Google Gemini.

Everything runs locally on your own machine. The extension talks to the site at `http://localhost:5000`.

---

## Contents

- [Features](#features)
- [How it fits together](#how-it-fits-together)
- [Requirements](#requirements)
- [Getting started](#getting-started)
- [Using the extension](#using-the-extension)
- [Using the web app](#using-the-web-app)
- [Automatic watch & notifications](#automatic-watch--notifications)
- [Cover letters with Gemini](#cover-letters-with-gemini)
- [HTTP API](#http-api)
- [Data storage](#data-storage)
- [Project structure](#project-structure)
- [Configuration](#configuration)
- [Troubleshooting](#troubleshooting)
- [Responsible use](#responsible-use)

---

## Features

### Chrome extension (Manifest V3, v1.7.0)
- **Scrape this page**: reads every job card on the current SEEK or Trade Me search results page.
- **Scrape all pages**: walks through up to **25 pages** of results in a background tab, with a 1-second pause between pages. It stops when it reaches the last page, sees repeated results, gets redirected, or has collected the total SEEK reports. Results are saved page by page, so closing the popup loses nothing.
- **Send to site**: uploads saved jobs to the web app in batches of 200 (it updates jobs by id or adds new ones).
- **Auto-send after scraping**: an optional toggle.
- **Only send strong applicants**: an optional SEEK-only filter that keeps jobs with a "Strong applicant" / "Very strong applicant" badge. Trade Me jobs are always sent.
- **Scrape queued details**: fetches the queue you built on the site and opens each job's own page to collect the full ad, contacts (emails and phones), skills, employer questions, pay, apply link and more.
- **Download CSV**: separate SEEK and Trade Me exports, UTF-8 with a BOM so Excel reads them correctly.
- **Background watch worker**: asks the site once a minute whether a scheduled scan is due and runs it in a hidden tab.

### Web app (.NET 10, Blazor Server, EF Core + SQLite)
| Page | Route | What it does |
|---|---|---|
| **Jobs** | `/` | Browse every scraped job, one site at a time (SEEK or Trade Me). You can filter by keyword, location (SEEK) or district (Trade Me), work type, classification, sub-classification, listed date (1/3/7/14/30 days) and applicant strength. Filtered jobs can be **queued for a detail scrape**. |
| **Favourites** | `/favourites` | Jobs whose full detail page has been scraped. Adds the **Contact** filter (phone / email / both / none) and the **Hours** filter (whether the ad lists work hours). Also shows detail-page data next to the card data, and the cover letter for each job. |
| **Map** | `/map` | A Leaflet map showing how many matching jobs are in each suburb or district. Auckland suburbs use official boundary outlines (`auckland-suburbs.geojson`), and Trade Me districts use `trademe-districts.geojson`. Other places are looked up once on OpenStreetMap Nominatim and cached. Click an area to open the Jobs or Favourites page filtered to it. |
| **Watch** | `/watch` | Set up the automatic rescans of a saved SEEK and/or Trade Me search. You choose how often they run, which new jobs should notify you, and the ntfy phone notification settings. |
| **Documents** | `/documents` | Upload or paste your CV (.pdf, .docx, .txt, .md), enter your Gemini API key and model, and edit the cover-letter prompt template. |

Other features:
- **Hours detection** (`HoursDetector`): regex rules that spot listed hours in ad text, such as `9am–5pm`, `8.30-5`, `27 hours per week`, `0.8 FTE`, `night shift` and `rostered`.
- **Listed-date parsing** (`ListedParser`): turns text like "3d ago" into an approximate date.
- **Trade Me taxonomy merging** (`TradeMeTaxonomy`): maps Trade Me categories onto SEEK's names, so a shared category has the same name on both sides.
- **Word export**: download any saved cover letter as a `.docx` file.

---

## How it fits together

```
 ┌─────────────────────────┐          ┌──────────────────────────────────────┐
 │  SEEK / Trade Me pages  │          │   JobFinder.Web  (localhost:5000)    │
 │  content.js reads DOM   │          │                                      │
 └───────────┬─────────────┘          │  Minimal API  ──►  jobs.db           │
             │ messages               │   /api/jobs          (Jobs,          │
 ┌───────────▼─────────────┐  HTTP    │   /api/queue          JobDetails,    │
 │ popup.js  (manual use)  ├─────────►│   /api/job-details    QueuedJobs)    │
 │ background.js (watch)   │◄─────────┤   /api/watch/*   ──►  watch.db       │
 │ common.js  (shared)     │          │                                      │
 └─────────────────────────┘          │  WatchService ──► ntfy.sh (push)     │
                                      │  DistrictLookup ─► Nominatim ─► districts.db
                                      │  CoverLetters ──► Gemini ─► documents.db
                                      │  Blazor pages: Jobs, Favourites,     │
                                      │  Map, Watch, Documents               │
                                      └──────────────────────────────────────┘
```

The **site owns all state and scheduling**. The extension only does the parts that need a real browser: reading SEEK and Trade Me pages as you see them.

---

## Requirements

- **.NET 10 SDK** (the project targets `net10.0`)
- **Google Chrome** (or another Chromium browser that supports Manifest V3 extensions)
- Optional: a **Google Gemini API key** for cover letters and reading PDF CVs (free tier key from <https://aistudio.google.com/apikey>)
- Optional: the **ntfy** app on your phone (<https://ntfy.sh>) for watch notifications

---

## Getting started

### 1. Run the web app

```bash
cd web/JobFinder.Web
dotnet run
```

The app listens on **http://localhost:5000** (see `Properties/launchSettings.json`). On first run it creates the SQLite databases (`jobs.db`, `districts.db`, `watch.db`, `documents.db`) in the working directory. It also loads the official Auckland suburb outlines into the district cache.

> The extension's `API_BASE` and `host_permissions` are hard-coded to `http://localhost:5000`. If you change the port, update `extension/common.js` and `extension/manifest.json` too.

### 2. Load the Chrome extension

1. Open `chrome://extensions`.
2. Turn on **Developer mode**.
3. Click **Load unpacked** and select the `extension/` folder.
4. Pin **SEEK Scraper** to the toolbar.

After you update the extension, refresh any SEEK or Trade Me tabs that were already open. Otherwise they keep running the old content script, and the popup will ask you to refresh.

### 3. First scrape

1. Run a search on SEEK (`seek.co.nz` / `seek.com.au`) or Trade Me Jobs (`trademe.co.nz/a/jobs/...`).
2. Open the extension and click **Scrape all pages** (tick **Auto-send after scraping** first, or click **Send to site** afterwards).
3. Open <http://localhost:5000> to browse the jobs.

---

## Using the extension

| Button / option | Behaviour |
|---|---|
| Scrape this page | Reads the cards on the active tab and saves them in `chrome.storage.local` (`jobs` for SEEK, `tmJobs` for Trade Me). |
| Scrape all pages | Starts from the current page number and loads later pages in an inactive tab. Has a **Stop** button. |
| Send to site | Sends all saved SEEK and Trade Me jobs to `POST /api/jobs`. |
| Scrape queued details | Reads `GET /api/queue` and visits each job page (10 s timeout, 1.5 s between jobs). It checks the page shows the expected job id, then posts to `/api/job-details`. Failed jobs stay in the queue. |
| Auto-send after scraping | Sends each scraped page to the site straight away. |
| Only send strong applicants | Skips SEEK jobs that have no applicant-strength badge. |
| Download CSV | Choose a SEEK or Trade Me CSV; the file is named `seek-jobs-YYYY-MM-DD.csv` / `trademe-jobs-YYYY-MM-DD.csv`. |
| Clear saved jobs | Clears the extension's local storage only. Nothing on the site is deleted. |

Each scrape also reports the current search URL to the site (`/api/watch/url`). That way the automatic watch always rescans your latest search.

**CSV columns**

- SEEK: `id, title, company, location, salary, workType, classification, subClass, listed, listingStatus, applicantStrength, badges, adType, section, description, url, scrapedAt`
- Trade Me: `id, title, company, isPromoted, listed, listedAt, location, category, subCategory, subSubCategory, region, district, workType, tags, pay, payInText, description, bulletPoints, quickApply, urlPath, url, imageUrl, logoUrl, scrapedAt`

**Selectors.** `content.js` relies on SEEK's `data-automation` attributes and on Trade Me's page structure. If a field starts coming back empty, the site has probably changed its markup. Inspect the page and update the selectors (the comments in `content.js` point to each one).

---

## Using the web app

### Typical workflow
1. **Collect.** Scrape searches with the extension and send them to the site.
2. **Filter.** On **Jobs**, narrow down the list (for example *Part time*, *listed in last 7 days*, *strong applicant*, a suburb).
3. **Queue.** Click **Queue details scrape** to queue every filtered job, up to a per-batch limit.
4. **Enrich.** In the extension, click **Scrape queued details**.
5. **Shortlist.** Jobs with details appear in **Favourites**. Filter by contact method and whether the hours are listed.
6. **Apply.** Generate a cover letter for a favourite and download it as Word.

### Map
- The Jobs and Map pages always show one site at a time, and filters carry over between the Jobs, Favourites and Map pages through the query string.
- Jobs that only say "Auckland" (or are in a district with no outline) can't be placed. They are listed with a link to view them on the Jobs page instead.
- Nominatim lookups follow its usage policy: at most one request per second, an identifying User-Agent, and each result cached permanently in `districts.db`. You can retry "not found" results from the page.

---

## Automatic watch & notifications

The watch rescans a saved search on a schedule and pushes new matching jobs to your phone.

**How it works**
1. Scraping a search with the extension saves that search's URL for its site (SEEK row or Trade Me row in `watch.db`). The page number is removed, so scans always start at page 1.
2. On the **Watch** page you turn the watch on and set the interval (**2 hours minimum, 7 days maximum**, default 4 hours). You also set filters for which new jobs deserve a notification: keyword, location/district, work type, classification, sub-class, listed days, applicant strength, and **exclude keywords**.
3. The extension's background worker calls `POST /api/watch/claim` every minute. When a scan is due, the site returns a `runId` and URL.
4. The extension scrapes every page in a hidden tab and posts the jobs to `POST /api/watch/results` (3 tries; a `409` means a newer scan has replaced it).
5. The site saves the jobs, works out which are new and match your filters, and publishes them to your **ntfy** topic.

**Phone setup**: install the ntfy app and subscribe to the topic shown on the Watch page (`https://ntfy.sh/<topic>`). The topic is random, but anyone who knows it can read the messages, so keep it private.

> Watch scans only run while Chrome is open with the extension loaded **and** the web app is running.

The on/off switch, interval and topic are shared by both sites. The search URL and filters are set separately for SEEK and Trade Me.

---

## Cover letters with Gemini

1. On **Documents**, save your **Gemini API key** and choose a model (default `gemini-3.5-flash-lite`).
2. Upload your CV. `.docx`, `.txt` and `.md` files are read locally. **PDFs are sent to Gemini** for text extraction. Old `.doc` files aren't supported (max 10 MB). You can also paste the text.
3. Optionally edit the **prompt template**. Placeholders in `{{double_braces}}` are filled in for each job. Empty fields become "(not provided)".

| Placeholder | Meaning |
|---|---|
| `{{job_title}}` | Job title |
| `{{company}}` | Company or advertiser |
| `{{location}}` | Location (and district on Trade Me) |
| `{{work_type}}` | Work type |
| `{{classification}}` | Classification |
| `{{pay}}` | Pay or salary line |
| `{{reference_number}}` | Reference number (Trade Me) |
| `{{contact_person}}` | Contact person (Trade Me) |
| `{{description}}` | The full job ad |
| `{{skills}}` | Skills the employer lists |
| `{{cv}}` | Your CV text |

The full list is shown on the Documents page. If you leave the prompt matching the default, it is stored empty, so future improvements to the default reach you automatically.

4. On **Favourites**, generate a letter for a job (temperature 0.6, with automatic retries when Gemini is busy). It is saved against the job. Download it from `/api/cover-letters/{jobId}/docx`.

---

## HTTP API

All endpoints are served by `Program.cs` and used by the extension. They have no authentication, because they are meant for localhost only.

| Method | Path | Body / response |
|---|---|---|
| `POST` | `/api/jobs` | JSON array of jobs → `{ received, added, updated, skipped }`. Updates jobs by `id`, adds new ones. |
| `GET` | `/api/queue` | → `[{ id, url }]` jobs waiting for a detail scrape, oldest first. |
| `POST` | `/api/job-details` | One job's detail page → `{ id, saved }`. `404` if the job hasn't been sent yet. Removes the job from the queue. |
| `POST` | `/api/watch/url` | `{ url }` → saves the search to watch. `400` if it isn't a SEEK or Trade Me search link. |
| `POST` | `/api/watch/claim` | → `{ run, runId, url, source }`. `run: false` when no scan is due. |
| `POST` | `/api/watch/results` | `{ runId, jobs, pages, error }` → `200`, or `409` if that run is no longer current. |
| `GET` | `/api/cover-letters/{jobId}/docx` | Downloads the saved cover letter as a Word document. |

**Ids**: SEEK jobs use SEEK's job id (or the URL as a fallback). Trade Me ids are stored with a `tm-` prefix so they can never clash with a SEEK id.

---

## Data storage

Four SQLite databases are created next to the app with `EnsureCreated()`. There are no EF migrations, apart from `WatchSchema.Upgrade`, which adds newer columns to an existing `watch.db`.

| File | DbContext | Contents |
|---|---|---|
| `jobs.db` | `JobDbContext` | `Jobs` (card data from both sites), `JobDetails` (full ad, contacts, skills, apply info), `QueuedJobs` |
| `watch.db` | `WatchDbContext` | One `WatchSettings` row per site (SEEK `Id 1`, Trade Me `Id 2`) |
| `districts.db` | `DistrictDbContext` | Cached map outlines and centres per location |
| `documents.db` | `DocumentsDbContext` | CV text, Gemini key/model/prompt, saved `CoverLetters` |

`*.db`, `*.db-shm` and `*.db-wal` are git-ignored. To start over, stop the app and delete the files. **`documents.db` holds your Gemini API key in plain text**, so don't share it.

---

## Project structure

```
job-finder/
├── extension/                    Chrome extension (Manifest V3)
│   ├── manifest.json             Permissions: storage, scripting, activeTab, alarms
│   ├── common.js                 Shared constants & helpers (API_BASE, paging, tab messaging)
│   ├── content.js                DOM scrapers for SEEK/Trade Me cards and job pages
│   ├── popup.html / popup.js     Manual scrape, send, queue and CSV UI
│   └── background.js             Service worker for the automatic watch
└── web/
    └── JobFinder.Web/            ASP.NET Core / Blazor Server app (.NET 10)
        ├── Program.cs            DI, DB creation, minimal API endpoints
        ├── appsettings.json      Connection strings & logging
        ├── Components/
        │   ├── App.razor, Routes.razor, Layout/MainLayout.razor
        │   ├── JobCard.razor, DetailList.razor
        │   └── Pages/            Jobs, Favourites, Map, Watch, Documents
        ├── Data/
        │   ├── Job*.cs           Entities, DTOs, mappers, ingest, queries, filter
        │   ├── Watch*.cs         Watch settings, schema upgrade, scheduling service
        │   ├── NtfyClient.cs     Push notifications
        │   ├── District*.cs, SuburbBoundaries.cs, TradeMeBoundaries.cs   Map outlines
        │   ├── GeminiClient.cs, CoverLetter*.cs, WordDocs.cs, DocumentsDbContext.cs
        │   ├── HoursDetector.cs, ListedParser.cs, ApplicantStrengths.cs, TradeMeTaxonomy.cs
        │   ├── auckland-suburbs.geojson
        │   └── trademe-districts.geojson
        └── wwwroot/              app.css, map.js (Leaflet 1.9.4)
```

---

## Configuration

`appsettings.json` connection strings (any missing one falls back to the default shown):

```json
"ConnectionStrings": {
  "Jobs": "Data Source=jobs.db",
  "Districts": "Data Source=districts.db",
  "Watch": "Data Source=watch.db",
  "Documents": "Data Source=documents.db"
}
```

Extension tunables in `extension/common.js`:

| Constant | Default | Meaning |
|---|---|---|
| `API_BASE` | `http://localhost:5000` | Address of the web app |
| `MAX_PAGES` | `25` | Most pages per run |
| `PAGE_DELAY_MS` | `1000` | Pause between result pages |
| `CARD_WAIT_MS` | `8000` | How long to wait for cards to render |

The watch interval limits (`MinIntervalMinutes = 120`, `MaxIntervalMinutes = 10080`) are in `Data/WatchSettings.cs`.

---

## Troubleshooting

| Symptom | Likely cause / fix |
|---|---|
| "can't reach http://localhost:5000. Is the site running?" | Start the web app with `dotnet run`. |
| "Reached the page, but no job cards matched" | The site's markup changed. Update the selectors in `content.js`. |
| "The extension was updated. Refresh this SEEK tab" | The tab is running an old content script. Reload it. |
| Watch never runs | Check the watch is enabled, a search URL is saved (scrape a search once), and Chrome is open with the extension loaded. The Watch page shows when the extension last checked in. |
| "No job cards were found on the first page" in a watch scan | SEEK or Trade Me showed a sign-in or verification page. Open the site in Chrome and complete it. |
| Map areas missing | Locations are still being looked up (one per second), or Nominatim found nothing. Use the retry option. |
| "Add your Gemini API key on the Documents page first" | Save a key on `/documents`. |

---

## Responsible use

This is a personal tool. It deliberately scrapes slowly (pauses between pages and jobs, a 25-page cap, and a watch interval of at least 2 hours). It reads only what you could see yourself in your own browser session. Respect the terms of use of SEEK, Trade Me, OpenStreetMap Nominatim, ntfy.sh and Google Gemini. Don't use it to redistribute listings or put load on these services.
