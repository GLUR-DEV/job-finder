// Runs the site's automatic watch. The site decides when a scan is due and keeps all the settings.
// Once a minute this asks the site "is a scan due?". If so it scrapes every page of the saved SEEK
// search in a background tab and hands the jobs back. The site saves them and sends the notifications.
importScripts("common.js");

const POLL_ALARM = "watch-poll";
const POLL_MINUTES = 1;
const POST_TRIES = 3;

let running = false;

async function ensureAlarm() {
  const existing = await chrome.alarms.get(POLL_ALARM);
  if (!existing) chrome.alarms.create(POLL_ALARM, { delayInMinutes: 0.2, periodInMinutes: POLL_MINUTES });
}

chrome.runtime.onInstalled.addListener(ensureAlarm);
chrome.runtime.onStartup.addListener(ensureAlarm);
ensureAlarm();

chrome.alarms.onAlarm.addListener(alarm => {
  if (alarm.name === POLL_ALARM) pollWatch();
});

async function pollWatch() {
  if (running) return;
  running = true;
  try {
    let claim;
    try {
      const res = await fetch(`${API_BASE}/api/watch/claim`, { method: "POST" });
      if (!res.ok) return;
      claim = await res.json();
    } catch (e) {
      return;   // the site isn't running, so there is nothing to do
    }
    if (!claim.run) return;
    await runScan(claim);
  } finally {
    running = false;
  }
}

async function runScan({ runId, url }) {
  const scan = await scanAllPages(url);
  const body = JSON.stringify({ runId, jobs: scan.jobs, pages: scan.pages, error: scan.error });

  for (let attempt = 1; attempt <= POST_TRIES; attempt++) {
    try {
      const res = await fetch(`${API_BASE}/api/watch/results`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body
      });
      // 409 means the site has moved on to a newer scan, so there is no point retrying.
      if (res.ok || res.status === 409) return;
    } catch (e) { /* site not reachable, try again */ }
    await sleep(5000);
  }
}

// Same page-by-page rules as the popup's "Scrape all pages", but in its own background tab.
// Returns { jobs, pages, error }. error is empty when the scan ended normally.
async function scanAllPages(startUrl) {
  const seen = new Set();
  const jobs = [];
  let pages = 0, error = "", workTab = null;

  try {
    for (let i = 0; i < MAX_PAGES; i++) {
      const pageNum = i + 1;
      const url = i === 0 ? startUrl : withPage(startUrl, pageNum);

      if (!workTab) workTab = await chrome.tabs.create({ url, active: false });
      else await chrome.tabs.update(workTab.id, { url });
      await waitForLoad(workTab.id);
      const tab = await chrome.tabs.get(workTab.id);

      // SEEK may redirect a too-high page number back to a valid one.
      if (i > 0 && pageOf(tab.url) < pageNum) break;

      const { jobs: found, pager } = await scrapeWhenReady(tab);

      if (!found.length) {
        if (i === 0) error = "No job cards were found on the first page. SEEK may be showing a sign-in or verification page.";
        break;
      }

      // Later pages must be real results pages, and the page SEEK says it is.
      if (i > 0) {
        if (!pager?.pagerFound) break;
        if (pager.current !== null && pager.current !== pageNum) break;
      }

      const fresh = found.filter(j => !seen.has(j.id));
      if (!fresh.length) break;   // repeated earlier results: that was the last page
      fresh.forEach(j => { seen.add(j.id); jobs.push(j); });
      pages++;

      if (!pager?.pagerFound || !pager.hasNext) break;
      if (pager.total && seen.size >= pager.total) break;

      await sleep(PAGE_DELAY_MS);
    }
  } catch (e) {
    error = e.message;
  } finally {
    if (workTab) { try { await chrome.tabs.remove(workTab.id); } catch (e) { /* already closed */ } }
  }

  return { jobs, pages, error };
}
