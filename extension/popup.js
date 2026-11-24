const statusEl = document.getElementById("status");
const COLUMNS = ["id", "title", "company", "location", "salary", "workType",
                 "classification", "subClass", "listed", "listingStatus", "applicantStrength",
                 "badges", "adType", "section", "description", "url", "scrapedAt"];

async function getSaved() {
  const { jobs = {} } = await chrome.storage.local.get("jobs");
  return jobs;
}

async function updateCount() {
  const jobs = await getSaved();
  statusEl.textContent = `${Object.keys(jobs).length} jobs saved.`;
}

// ---- Send to the Blazor site ----
const API_BASE = "http://localhost:5000";
const SEND_BATCH = 200;

async function isAutoSend() {
  const { autoSend = false } = await chrome.storage.local.get("autoSend");
  return autoSend;
}

async function isStrongOnly() {
  const { strongOnly = false } = await chrome.storage.local.get("strongOnly");
  return strongOnly;
}

async function sendBatch(jobs) {
  let res;
  try {
    res = await fetch(`${API_BASE}/api/jobs`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(jobs)
    });
  } catch (e) {
    throw new Error(`can't reach ${API_BASE}. Is the site running?`);
  }
  if (!res.ok) throw new Error(`the site responded with ${res.status}`);
  return await res.json();
}

// Sends jobs in batches. With "Only send strong applicants" on, other jobs are skipped.
// Returns { added, updated, skipped }.
async function sendJobs(jobs) {
  const toSend = (await isStrongOnly()) ? jobs.filter(job => job.applicantStrength) : jobs;
  const skipped = jobs.length - toSend.length;
  let added = 0, updated = 0;
  for (let i = 0; i < toSend.length; i += SEND_BATCH) {
    const r = await sendBatch(toSend.slice(i, i + SEND_BATCH));
    added += r.added;
    updated += r.updated;
  }
  return { added, updated, skipped };
}

function sentMessage(r) {
  const skipped = r.skipped ? ` ${r.skipped} skipped (not strong applicants).` : "";
  return `Sent to site: ${r.added} new, ${r.updated} updated.${skipped}`;
}

// Same as sendJobs but never throws; returns a status sentence.
async function trySend(jobs) {
  try {
    return sentMessage(await sendJobs(jobs));
  } catch (e) {
    return `Could not send to site: ${e.message}`;
  }
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
    let msg = `Found ${found.length}, ${added} new. Total saved: ${Object.keys(saved).length}.`;
    if (await isAutoSend()) msg += " " + await trySend(found);
    statusEl.textContent = msg;
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
  for (const id of ["scrape", "scrapeAll", "sendAll", "scrapeQueue", "download", "clear"]) document.getElementById(id).disabled = busy;
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
  const autoSend = await isAutoSend();
  let siteAdded = 0, siteUpdated = 0, siteSkipped = 0, sendError = "";

  try {
    for (let i = 0; i < MAX_PAGES; i++) {
      if (stopRequested) { reason = "Stopped by you."; break; }
      const pageNum = startPage + i;
      statusEl.textContent = `Scraping page ${pageNum} (${i + 1}/${MAX_PAGES})... ${totalFound} jobs found so far.`;

      let tab, found, pager;
      if (i === 0) {
        // Current page: scrape in place, no reload.
        tab = activeTab;
        ({ jobs: found, pager } = await scrapeWhenReady(tab));
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
        ({ jobs: found, pager } = await scrapeWhenReady(tab));
      }

      // The tab was opened before the extension was updated, so it can't report pagination yet.
      if (pager === undefined) { reason = "The extension was updated. Refresh this SEEK tab and try again."; break; }

      if (!found.length) { reason = `Page ${pageNum} has no jobs, so that was the last page.`; break; }

      // Belt and braces for later pages: it must be a real results page, and the page SEEK says it is.
      if (i > 0) {
        if (!pager?.pagerFound) { reason = `Page ${pageNum} has no page navigation, so it isn't a real results page.`; break; }
        if (pager.current !== null && pager.current !== pageNum) { reason = `Page ${pageNum} doesn't exist (SEEK showed page ${pager.current}).`; break; }
      }

      const fresh = found.filter(j => !seenIds.has(j.id));
      if (!fresh.length) { reason = `Page ${pageNum} repeated earlier results, so that was the last page.`; break; }
      fresh.forEach(j => seenIds.add(j.id));

      const { added } = await saveJobs(found);   // saved per page, so nothing is lost if the popup closes

      if (autoSend && !sendError) {
        try {
          const r = await sendJobs(found);
          siteAdded += r.added;
          siteUpdated += r.updated;
          siteSkipped += r.skipped;
        } catch (e) {
          sendError = e.message;   // keep scraping; jobs are still saved locally
        }
      }
      totalFound += found.length;
      totalAdded += added;
      pagesDone++;

      // SEEK's own signals that the real results are over.
      if (!pager?.pagerFound) { reason = "That was the only page of results."; break; }
      if (!pager.hasNext) { reason = `Page ${pageNum} is the last page.`; break; }
      if (pager.total && seenIds.size >= pager.total) { reason = `Collected all ${pager.total} jobs.`; break; }

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
  let siteMsg = "";
  if (autoSend) {
    siteMsg = sendError
      ? ` Could not send to site: ${sendError}. Use Send to site to retry.`
      : " " + sentMessage({ added: siteAdded, updated: siteUpdated, skipped: siteSkipped });
  }
  statusEl.textContent = `${reason} Scraped ${pagesDone} page(s), ${totalFound} jobs found, ${totalAdded} new. Total saved: ${Object.keys(saved).length}.${siteMsg}`;
});

document.getElementById("sendAll").addEventListener("click", async () => {
  const jobs = Object.values(await getSaved());
  if (!jobs.length) { statusEl.textContent = "Nothing to send yet."; return; }
  setBusy(true);
  document.getElementById("stop").hidden = true;
  statusEl.textContent = `Sending ${jobs.length} jobs to the site...`;
  statusEl.textContent = await trySend(jobs);
  setBusy(false);
});

const strongOnlyEl = document.getElementById("strongOnly");
isStrongOnly().then(v => { strongOnlyEl.checked = v; });
strongOnlyEl.addEventListener("change", () => chrome.storage.local.set({ strongOnly: strongOnlyEl.checked }));

const autoSendEl = document.getElementById("autoSend");
isAutoSend().then(v => { autoSendEl.checked = v; });
autoSendEl.addEventListener("change", () => chrome.storage.local.set({ autoSend: autoSendEl.checked }));

// ---- Scrape queued job details ----
const SEEK_URL = /^https:\/\/([^/]+\.)?seek\.(com(\.au)?|co\.nz)\//;
const DETAIL_WAIT_MS = 10000;
const DETAIL_DELAY_MS = 1500;   // pause between job pages to be polite to SEEK

async function fetchQueue() {
  let res;
  try {
    res = await fetch(`${API_BASE}/api/queue`);
  } catch (e) {
    throw new Error(`can't reach ${API_BASE}. Is the site running?`);
  }
  if (!res.ok) throw new Error(`the site responded with ${res.status}`);
  return await res.json();
}

async function postDetail(detail) {
  let res;
  try {
    res = await fetch(`${API_BASE}/api/job-details`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(detail)
    });
  } catch (e) {
    throw new Error(`can't reach ${API_BASE}. Is the site running?`);
  }
  if (!res.ok) throw new Error(`the site responded with ${res.status}`);
}

// Job pages render after the tab "completes", so poll until the title and ad text are there.
async function scrapeDetailWhenReady(tab) {
  const deadline = Date.now() + DETAIL_WAIT_MS;
  while (true) {
    try {
      const res = await requestFromTab(tab, "SCRAPE_DETAIL");
      const d = res && res.detail;
      if (d && d.title && d.description) return d;
    } catch (e) { /* content script not ready yet */ }
    if (Date.now() > deadline) return null;
    await sleep(500);
  }
}

document.getElementById("scrapeQueue").addEventListener("click", async () => {
  stopRequested = false;
  setBusy(true);
  let workTab = null;
  let done = 0, total = 0, stopped = false, fatal = "", lastError = "";
  const failed = [];

  try {
    statusEl.textContent = "Reading the queue from the site...";
    const queue = await fetchQueue();
    total = queue.length;

    if (!total) {
      statusEl.textContent = "The queue is empty. On the site, filter your jobs and click Queue details scrape.";
      return;
    }

    for (let i = 0; i < queue.length; i++) {
      if (stopRequested) { stopped = true; break; }
      const item = queue[i];
      statusEl.textContent = `Scraping job ${i + 1}/${total}... ${done} saved, ${failed.length} failed.`;

      try {
        if (!item.url || !SEEK_URL.test(item.url)) throw new Error("not a SEEK job URL");

        if (!workTab) workTab = await chrome.tabs.create({ url: item.url, active: false });
        else await chrome.tabs.update(workTab.id, { url: item.url });
        await waitForLoad(workTab.id);

        const tab = await chrome.tabs.get(workTab.id);
        const detail = await scrapeDetailWhenReady(tab);
        if (!detail) throw new Error("the job page couldn't be read (it may have expired)");
        if (detail.id && detail.id !== item.id) throw new Error("SEEK showed a different job");

        detail.id = item.id;
        await postDetail(detail);
        done++;
      } catch (e) {
        failed.push(item.id);
        lastError = e.message;
        // If the site itself is down there's no point carrying on.
        if (/can't reach/.test(e.message)) { fatal = e.message; break; }
      }

      await sleep(DETAIL_DELAY_MS);
    }
  } catch (e) {
    fatal = e.message;
  } finally {
    if (workTab) { try { await chrome.tabs.remove(workTab.id); } catch (e) {} }
    setBusy(false);
  }

  if (!total) return;
  let msg = `Saved details for ${done} of ${total} jobs.`;
  if (failed.length) msg += ` ${failed.length} failed (last error: ${lastError}). They stay in the queue.`;
  if (stopped) msg = "Stopped by you. " + msg;
  if (fatal) msg = `Could not continue: ${fatal}. ` + msg;
  statusEl.textContent = msg;
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
