const statusEl = document.getElementById("status");
const COLUMNS = ["id", "title", "company", "location", "salary", "workType",
                 "classification", "subClass", "listed", "description", "url", "scrapedAt"];

async function getSaved() {
  const { jobs = {} } = await chrome.storage.local.get("jobs");
  return jobs;
}

async function updateCount() {
  const jobs = await getSaved();
  statusEl.textContent = `${Object.keys(jobs).length} jobs saved.`;
}

async function requestJobs(tab) {
  try {
    return await chrome.tabs.sendMessage(tab.id, { type: "SCRAPE" });
  } catch (e) {
    // Content script isn't in the page yet (tab opened before install). Inject it.
    await chrome.scripting.executeScript({ target: { tabId: tab.id }, files: ["content.js"] });
    return await chrome.tabs.sendMessage(tab.id, { type: "SCRAPE" });
  }
}

document.getElementById("scrape").addEventListener("click", async () => {
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  try {
    const res = await requestJobs(tab);
    const found = res?.jobs || [];
    if (!found.length) {
      statusEl.textContent = "Reached the page, but no job cards matched. The selectors in content.js may need updating.";
      return;
    }
    const saved = await getSaved();
    let added = 0;
    for (const job of found) {
      if (!saved[job.id]) added++;
      saved[job.id] = job;
    }
    await chrome.storage.local.set({ jobs: saved });
    statusEl.textContent = `Found ${found.length}, ${added} new. Total saved: ${Object.keys(saved).length}.`;
  } catch (e) {
    statusEl.textContent = "Error: " + e.message + " (URL: " + (tab?.url || "unknown") + ")";
  }
});

// ---- Scrape all pages ----
const MAX_PAGES = 25;
const PAGE_DELAY_MS = 1000;      // pause between pages to be polite to SEEK
const CARD_WAIT_MS = 8000;       // how long to wait for job cards to appear on a page
let stopRequested = false;

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

// Cards can render after the page "completes", so poll until some show up or we time out.
async function scrapeWhenReady(tab) {
  const deadline = Date.now() + CARD_WAIT_MS;
  while (true) {
    try {
      const res = await requestJobs(tab);
      if (res?.jobs?.length) return res.jobs;
    } catch (e) { /* content script not ready yet */ }
    if (Date.now() > deadline) return [];
    await sleep(500);
  }
}

async function saveJobs(found) {
  const saved = await getSaved();
  let added = 0;
  for (const job of found) {
    if (!saved[job.id]) added++;
    saved[job.id] = job;
  }
  await chrome.storage.local.set({ jobs: saved });
  return { added, total: Object.keys(saved).length };
}

function setBusy(busy) {
  for (const id of ["scrape", "scrapeAll", "download", "clear"]) document.getElementById(id).disabled = busy;
  document.getElementById("stop").hidden = !busy;
}

document.getElementById("stop").addEventListener("click", () => { stopRequested = true; });

document.getElementById("scrapeAll").addEventListener("click", async () => {
  const [activeTab] = await chrome.tabs.query({ active: true, currentWindow: true });
  if (!activeTab?.url || !/^https:\/\/([^/]+\.)?seek\.(com(\.au)?|co\.nz)\//.test(activeTab.url)) {
    statusEl.textContent = "Open a SEEK search results page first.";
    return;
  }

  stopRequested = false;
  setBusy(true);
  const startPage = pageOf(activeTab.url);
  const seenIds = new Set();
  let totalFound = 0, totalAdded = 0, pagesDone = 0, reason = "";
  let workTab = null;

  try {
    for (let i = 0; i < MAX_PAGES; i++) {
      if (stopRequested) { reason = "Stopped by you."; break; }
      const pageNum = startPage + i;
      statusEl.textContent = `Scraping page ${pageNum} (${i + 1}/${MAX_PAGES})... ${totalFound} jobs found so far.`;

      let tab, found;
      if (i === 0) {
        // Current page: scrape in place, no reload.
        tab = activeTab;
        found = await scrapeWhenReady(tab);
      } else {
        // Later pages: load in a background tab so your current page isn't disturbed.
        const url = withPage(activeTab.url, pageNum);
        if (!workTab) {
          workTab = await chrome.tabs.create({ url, active: false });
        } else {
          await chrome.tabs.update(workTab.id, { url });
        }
        await waitForLoad(workTab.id);
        tab = await chrome.tabs.get(workTab.id);

        // SEEK may redirect a too-high page number back to a valid one.
        if (pageOf(tab.url) < pageNum) { reason = `Page ${pageNum} doesn't exist (redirected).`; break; }
        found = await scrapeWhenReady(tab);
      }

      if (!found.length) { reason = `Page ${pageNum} has no jobs, so that was the last page.`; break; }

      const fresh = found.filter(j => !seenIds.has(j.id));
      if (!fresh.length) { reason = `Page ${pageNum} repeated earlier results, so that was the last page.`; break; }
      fresh.forEach(j => seenIds.add(j.id));

      const { added } = await saveJobs(found);   // saved per page, so nothing is lost if the popup closes
      totalFound += found.length;
      totalAdded += added;
      pagesDone++;

      if (i === MAX_PAGES - 1) { reason = `Reached the ${MAX_PAGES}-page limit.`; break; }
      await sleep(PAGE_DELAY_MS);
    }
  } catch (e) {
    reason = "Error: " + e.message;
  } finally {
    if (workTab) { try { await chrome.tabs.remove(workTab.id); } catch (e) {} }
    setBusy(false);
  }

  const saved = await getSaved();
  statusEl.textContent = `${reason} Scraped ${pagesDone} page(s), ${totalFound} jobs found, ${totalAdded} new. Total saved: ${Object.keys(saved).length}.`;
});

function csvCell(value) {
  return `"${String(value ?? "").replace(/"/g, '""')}"`;
}

document.getElementById("download").addEventListener("click", async () => {
  const jobs = Object.values(await getSaved());
  if (!jobs.length) { statusEl.textContent = "Nothing to download yet."; return; }

  const rows = [COLUMNS.map(csvCell).join(",")];
  jobs.forEach(j => rows.push(COLUMNS.map(c => csvCell(j[c])).join(",")));

  // BOM so Excel reads UTF-8 correctly
  const blob = new Blob(["\ufeff" + rows.join("\r\n")], { type: "text/csv;charset=utf-8" });
  const a = document.createElement("a");
  a.href = URL.createObjectURL(blob);
  a.download = `seek-jobs-${new Date().toISOString().slice(0, 10)}.csv`;
  a.click();
  URL.revokeObjectURL(a.href);
  statusEl.textContent = `Downloaded ${jobs.length} jobs.`;
});

document.getElementById("clear").addEventListener("click", async () => {
  await chrome.storage.local.remove("jobs");
  updateCount();
});

updateCount();