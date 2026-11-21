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
