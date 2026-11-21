// Runs on SEEK pages. Reads job cards from the DOM when the popup asks.

// SEEK uses data-automation attributes. These are the most stable hooks,
// but SEEK can change them. If a field comes back empty, inspect the page
// (right click > Inspect) and update the selectors below.
const CARD_SELECTORS = [
  'article[data-automation="normalJob"]',
  'article[data-automation="premiumJob"]',
  'article[data-testid="job-card"]',
  '[data-automation="jobCard"]'
];

const FIELDS = {
  title:          ['a[data-automation="jobTitle"]', '[data-automation="jobTitle"]'],
  company:        ['a[data-automation="jobCompany"]', '[data-automation="jobCompany"]'],
  location:       ['a[data-automation="jobLocation"]', '[data-automation="jobLocation"]'],
  salary:         ['span[data-automation="jobSalary"]', '[data-automation="jobSalary"]'],
  workType:       ['[data-automation="jobWorkType"]'],
  classification: ['a[data-automation="jobClassification"]', '[data-automation="jobClassification"]'],
  subClass:       ['a[data-automation="jobSubClassification"]', '[data-automation="jobSubClassification"]'],
  listed:         ['span[data-automation="jobListingDate"]', '[data-automation="jobListingDate"]'],
  description:    ['span[data-automation="jobShortDescription"]', '[data-automation="jobShortDescription"]']
};

function firstText(card, selectors) {
  for (const sel of selectors) {
    const el = card.querySelector(sel);
    if (el && el.textContent.trim()) return el.textContent.trim().replace(/\s+/g, " ");
  }
  return "";
}

function scrapeJobs() {
  const cards = new Set();
  CARD_SELECTORS.forEach(sel => document.querySelectorAll(sel).forEach(c => cards.add(c)));

  const now = new Date().toISOString();
  const jobs = [];

  cards.forEach(card => {
    const linkEl = card.querySelector(FIELDS.title.join(","));
    const url = linkEl && linkEl.href ? linkEl.href.split("?")[0] : "";
    const idMatch = url.match(/\/job\/(\d+)/);
    const id = card.getAttribute("data-job-id") || (idMatch ? idMatch[1] : url);

    const job = { id, url };
    for (const [key, selectors] of Object.entries(FIELDS)) {
      job[key] = firstText(card, selectors);
    }
    job.scrapedAt = now;

    if (job.title) jobs.push(job);
  });

  return jobs;
}

if (!window.__seekScraperLoaded) {
  window.__seekScraperLoaded = true;
  chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
    if (msg && msg.type === "SCRAPE") {
      sendResponse({ jobs: scrapeJobs() });
    }
    return true;
  });
}
