// Shared by the popup (popup.html) and the background worker (background.js).

const API_BASE = "http://localhost:5000";
const SEEK_URL = /^https:\/\/([^/]+\.)?seek\.(com(\.au)?|co\.nz)\//;
const TRADEME_URL = /^https:\/\/([^/]+\.)?trademe\.co\.nz\//;

const MAX_PAGES = 25;
const PAGE_DELAY_MS = 1000;      // pause between pages to be polite to SEEK
const CARD_WAIT_MS = 8000;       // how long to wait for job cards to appear on a page

const sleep = ms => new Promise(r => setTimeout(r, ms));

function pageOf(url) {
  const p = parseInt(new URL(url).searchParams.get("page"), 10);
  return p > 0 ? p : 1;
}

function withPage(url, n) {
  const u = new URL(url);
  u.searchParams.set("page", n);   // replaces an existing page param instead of duplicating it
  return u.toString();
}

function waitForLoad(tabId) {
  return new Promise(resolve => {
    const done = () => { chrome.tabs.onUpdated.removeListener(listener); clearTimeout(timer); resolve(); };
    const listener = (id, info) => { if (id === tabId && info.status === "complete") done(); };
    const timer = setTimeout(done, 20000);
    chrome.tabs.onUpdated.addListener(listener);
  });
}

async function requestFromTab(tab, type) {
  try {
    return await chrome.tabs.sendMessage(tab.id, { type });
  } catch (e) {
    // Content script isn't there yet (for example the tab was open before the extension loaded).
    await chrome.scripting.executeScript({ target: { tabId: tab.id }, files: ["content.js"] });
    return await chrome.tabs.sendMessage(tab.id, { type });
  }
}

function requestJobs(tab) {
  return requestFromTab(tab, "SCRAPE");
}

// Cards (and the pager) can render after the page "completes", so poll until they show up or we time out.
// Returns { jobs, pager }. pager is undefined if the page still runs an old content script.
async function scrapeWhenReady(tab) {
  const deadline = Date.now() + CARD_WAIT_MS;
  let last = { jobs: [], pager: null };
  while (true) {
    try {
      const res = await requestJobs(tab);
      if (res) {
        last = { jobs: res.jobs || [], pager: res.pager };
        if (last.jobs.length && (res.pager === undefined || res.pager.pagerFound)) return last;
      }
    } catch (e) { /* content script not ready yet */ }
    if (Date.now() > deadline) return last;
    await sleep(500);
  }
}

// Tells the site which SEEK search the automatic watch should rescan.
// Quietly does nothing if the site isn't running; the next scrape will try again.
async function reportSearchUrl(url) {
  try {
    if (!url || !SEEK_URL.test(url) || new URL(url).pathname.startsWith("/job/")) return;
    await fetch(`${API_BASE}/api/watch/url`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ url })
    });
  } catch (e) { /* site not running */ }
}
