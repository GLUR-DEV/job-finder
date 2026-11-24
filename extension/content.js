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
  classification: ['a[data-automation="jobClassification"]', '[data-automation="jobClassification"]'],
  subClass:       ['a[data-automation="jobSubClassification"]', '[data-automation="jobSubClassification"]']
};
const DESCRIPTION_SELECTORS = ['span[data-automation="jobShortDescription"]', '[data-automation="jobShortDescription"]'];

const STRONG = "Strong applicant";
const VERY_STRONG = "Very strong applicant";

function clean(s) { return (s || "").replace(/\s+/g, " ").trim(); }

function firstText(card, selectors) {
  for (const sel of selectors) {
    const el = card.querySelector(sel);
    if (el && clean(el.textContent)) return clean(el.textContent);
  }
  return "";
}

// Bullet points in the teaser are joined with " | " so they stay readable in one CSV cell.
function descriptionOf(card) {
  for (const sel of DESCRIPTION_SELECTORS) {
    const el = card.querySelector(sel);
    if (!el) continue;
    const raw = el.innerText || el.textContent || "";
    const text = raw.split(/\n+/).map(clean).filter(Boolean).join(" | ");
    if (text) return text;
  }
  return "";
}

// SEEK shows work type as a sentence: "This is a Part time job".
function workTypeOf(card) {
  for (const p of card.querySelectorAll("p")) {
    const m = clean(p.textContent).match(/^This is an? (.+) job$/i);
    if (m) return m[1];
  }
  return "";
}

// The listing date element holds "11h ago", then a bullet, then extras like "Viewed" or "Expiring".
function listedOf(card) {
  const el = card.querySelector('[data-automation="jobListingDate"]');
  if (!el) return { listed: "", status: "" };
  let parts = [...el.querySelectorAll("span")].map(s => clean(s.textContent)).filter(t => t && t !== "•");
  parts = [...new Set(parts)];
  if (!parts.length) parts = [clean(el.textContent)].filter(Boolean);
  return { listed: parts[0] || "", status: parts.slice(1).join("; ") };
}

// All badge texts on the card (Strong applicant, New to you, Be an early applicant, ...).
function badgesOf(card) {
  const box = card.querySelector('[data-testid="job-card-badges"]');
  if (!box) return [];
  const texts = [...box.querySelectorAll("span")]
    .filter(s => !s.querySelector("*"))
    .map(s => clean(s.textContent))
    .filter(Boolean);
  return [...new Set(texts)];
}

function applicantStrengthOf(card, badges) {
  if (card.querySelector('[data-automation="veryStrongApplicantAdBadge"]')) return VERY_STRONG;
  if (card.querySelector('[data-automation="strongApplicantAdBadge"]')) return STRONG;
  const hit = badges.find(b => /strong applicant/i.test(b));
  if (!hit) return "";
  return /^very/i.test(hit) ? VERY_STRONG : STRONG;
}

// The wrapper around each card carries SEEK's own metadata (ad type, section).
function metaOf(card) {
  const raw = card.parentElement && card.parentElement.getAttribute("data-search-sol-meta");
  if (!raw) return {};
  try { return JSON.parse(raw); } catch (e) { return {}; }
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
    job.classification = job.classification.replace(/^\(\s*|\s*\)$/g, "");   // SEEK wraps it in brackets
    job.workType = workTypeOf(card);
    job.description = descriptionOf(card);

    const listed = listedOf(card);
    job.listed = listed.listed;
    job.listingStatus = listed.status;

    const badges = badgesOf(card);
    job.badges = badges.join("; ");
    job.applicantStrength = applicantStrengthOf(card, badges);

    const meta = metaOf(card);
    job.adType = meta.jobAdType || "";
    job.section = meta.section || "";

    job.scrapedAt = now;

    if (job.title) jobs.push(job);
  });

  return jobs;
}

// Reads SEEK's pagination and result counter so the popup knows when the real results end.
// Page 3 of 3 has no <a rel="next"> (Next is a dead <span>), and the current page is aria-current="page".
function readPager() {
  const nav = document.querySelector('nav[aria-label="Pagination of results"]');
  const counter = document.querySelector('[data-automation="jobCounter"]');
  const total = counter ? parseInt(counter.textContent.replace(/[^\d]/g, ""), 10) : NaN;

  const pager = {
    pagerFound: !!nav,
    hasNext: false,
    current: null,
    total: Number.isFinite(total) ? total : null
  };
  if (nav) {
    pager.hasNext = !!nav.querySelector('a[rel~="next"]');
    const cur = nav.querySelector('a[aria-current="page"]');
    const n = cur ? parseInt(cur.textContent, 10) : NaN;
    pager.current = Number.isFinite(n) ? n : null;
  }
  return pager;
}

if (!window.__seekScraperLoaded) {
  window.__seekScraperLoaded = true;
  chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
    if (msg && msg.type === "SCRAPE") {
      sendResponse({ jobs: scrapeJobs(), pager: readPager() });
    }
    return true;
  });
}