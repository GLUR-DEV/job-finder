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

// Collapses whitespace and drops invisible characters SEEK adds (word joiners, zero-width spaces, object-replacement marks).
function clean(s) {
  return (s || "").replace(/[\u2060\u200b-\u200d\ufeff\ufffc]/g, "").replace(/\s+/g, " ").trim();
}

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

    const job = { source: "seek", id, url };
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


// ---- Job detail page scraping ----

function textOf(selector) {
  const el = document.querySelector(selector);
  return el ? clean(el.textContent) : "";
}

// First text-only <span> whose text matches the pattern (SEEK gives some lines no data-automation hook).
function leafSpanText(pattern) {
  for (const s of document.querySelectorAll("span")) {
    if (s.querySelector("*")) continue;
    const t = clean(s.textContent);
    if (pattern.test(t)) return t;
  }
  return "";
}

// Keeps paragraph and list line breaks; drops non-breaking spaces and SEEK's stray object-replacement characters.
function textBlock(el) {
  const raw = el.innerText || el.textContent || "";
  return raw
    .replace(/\u00a0/g, " ")
    .replace(/\ufffc/g, "")
    .replace(/[ \t]+/g, " ")
    .split("\n").map(l => l.trim()).join("\n")
    .replace(/\n{3,}/g, "\n\n")
    .trim();
}

// SEEK shows contact details partly hidden ("••••@company.com") but keeps the real value in data-contact-match.
function contactsOf(adEl) {
  const emails = new Set(), phones = new Set(), unmask = [];
  adEl.querySelectorAll('[data-contact-match], a[href^="mailto:"], a[href^="tel:"]').forEach(el => {
    const href = el.getAttribute("href") || "";
    let value = el.getAttribute("data-contact-match") || "";
    try {
      if (!value && /^mailto:/i.test(href)) value = decodeURIComponent(href.slice(7).split("?")[0]);
      if (!value && /^tel:/i.test(href)) value = decodeURIComponent(href.slice(4));
    } catch (e) { /* bad escape in href, ignore */ }
    value = clean(value);
    if (!value) return;

    if (value.includes("@")) emails.add(value);
    else if (value.replace(/\D/g, "").length >= 6) phones.add(value);

    const shown = clean(el.textContent);
    if (shown && shown !== value) unmask.push([shown, value]);
  });
  return { emails: [...emails], phones: [...phones], unmask };
}

// Only the skills visible on the page. "Show all" / "+N more" is deliberately not clicked.
function skillsOf() {
  const result = { summary: "", matched: [], suggested: [] };
  const summaryEl = [...document.querySelectorAll("span")]
    .find(s => !s.querySelector("*") && /skills and credentials/i.test(s.textContent));
  if (!summaryEl) return result;

  result.summary = clean(summaryEl.textContent);
  summaryEl.parentElement.querySelectorAll("div[title]").forEach(chip => {
    const name = clean(chip.getAttribute("title"));
    if (!name) return;
    // Chips SEEK suggests adding to your profile have an "Add <skill>" button; matched ones don't.
    (chip.querySelector('button[aria-label^="Add "]') ? result.suggested : result.matched).push(name);
  });
  return result;
}

function employerQuestionsOf() {
  const h2 = [...document.querySelectorAll("h2")].find(h => /^employer questions$/i.test(clean(h.textContent)));
  const section = h2 && h2.closest("section");
  if (!section) return [];
  return [...section.querySelectorAll("li")].map(li => clean(li.textContent)).filter(Boolean);
}

// Reads a job's own page. Never touches the "Report this job advert" form, Featured jobs or the salary teaser.
function scrapeJobDetail() {
  const adEl = document.querySelector('[data-automation="jobAdDetails"]');
  const contacts = adEl ? contactsOf(adEl) : { emails: [], phones: [], unmask: [] };

  let description = adEl ? textBlock(adEl) : "";
  for (const [shown, real] of contacts.unmask) description = description.split(shown).join(real);

  const skills = skillsOf();
  const apply = document.querySelector('a[data-automation="job-detail-apply"]');
  const more = document.querySelector('a[data-automation="job-details-header-more-jobs"]');
  const idMatch = location.pathname.match(/\/job\/(\d+)/);

  const badges = [...document.querySelectorAll('[data-automation="jdv-badges-section"] [data-testid="job-status-badge"]')]
    .map(b => clean(b.textContent)).filter(Boolean);

  return {
    id: idMatch ? idMatch[1] : "",
    url: location.origin + location.pathname,
    title: textOf('[data-automation="job-detail-title"]'),
    advertiser: textOf('[data-automation="advertiser-name"]'),
    advertiserUrl: more ? more.href : "",
    location: textOf('[data-automation="job-detail-location"]'),
    classification: textOf('[data-automation="job-detail-classifications"]'),
    workType: textOf('[data-automation="job-detail-work-type"]'),
    salary: textOf('[data-automation="job-detail-salary"]'),
    posted: leafSpanText(/^Posted\b/i),
    applicationVolume: leafSpanText(/application volume$/i),
    applyType: apply ? clean(apply.textContent) : "",
    applyUrl: apply ? apply.href : "",
    badges: [...new Set(badges)].join("; "),
    skillsSummary: skills.summary,
    skillsMatched: skills.matched.join("; "),
    skillsSuggested: skills.suggested.join("; "),
    employerQuestions: employerQuestionsOf().join("\n"),
    emails: contacts.emails.join("; "),
    phones: contacts.phones.join("; "),
    description,
    scrapedAt: new Date().toISOString()
  };
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

// ---- Trade Me search pages ----
// Trade Me is an Angular app: cards are custom elements and the data hooks are tmid="..." attributes.
// Normal cards are <tm-jobs-search-card>; promoted (paid) cards are <tm-promoted-listing-card> with different
// markup, so each kind has its own reader. If a field comes back empty, inspect the page and update the selectors.
const IS_TRADEME = /(^|\.)trademe\.co\.nz$/.test(location.hostname);

function tmText(root, selector) {
  const el = root.querySelector(selector);
  return el ? clean(el.textContent) : "";
}

// Listing URLs look like /a/jobs/<category>/<sub-category>[/<sub-sub-category>]/<region>/<district>/<work-type>/listing/<id>.
// The category tree is 1 to 3 levels deep (for example "other/...", "healthcare/dentistry/...",
// "healthcare/nursing-midwifery/surgical/..."), but the last three parts are always region, district and work type,
// so the parts are read from the end. The raw path is kept as well for checking.
function tmUrlParts(path) {
  const segs = path.split("/").filter(Boolean);
  const start = segs.indexOf("jobs");
  const end = segs.indexOf("listing");
  const parts = start >= 0 && end > start ? segs.slice(start + 1, end) : [];
  const out = { category: "", subCategory: "", subSubCategory: "", region: "", district: "", workType: "", urlPath: parts.join("/") };
  if (parts.length >= 4) {
    out.workType = parts[parts.length - 1];
    out.district = parts[parts.length - 2];
    out.region = parts[parts.length - 3];
    [out.category = "", out.subCategory = "", out.subSubCategory = ""] = parts.slice(0, -3);
  }
  return out;
}

// First pay-looking figure in free text, e.g. "$40-$45/hr" or "$25 per hour" or "$80k".
const TM_PAY_PATTERN = /\$\s?\d[\d,]*(?:\.\d+)?\s?k?(?:\s?(?:-|–|to)\s?\$?\s?\d[\d,]*(?:\.\d+)?\s?k?)?(?:\s?(?:\/|per\s|p\.?\s?)\s?(?:hr|hour|h|annum|a|year|yr|week|wk|day))?/i;

function tmPayIn(text) {
  const m = (text || "").match(TM_PAY_PATTERN);
  return m ? clean(m[0]) : "";
}

function tmCardLink(card) {
  return card.querySelector('a[href*="/listing/"]');
}

function tmCommon(card, isPromoted) {
  const link = tmCardLink(card);
  const href = link ? link.href : "";
  const url = href.split("?")[0];
  const idMatch = url.match(/\/listing\/(\d+)/);
  const path = url ? new URL(url).pathname : "";

  const timeEl = card.querySelector('time[tmid="startDate"], time');
  const job = {
    source: "trademe",
    id: idMatch ? idMatch[1] : url,
    url,
    isPromoted,
    listed: timeEl ? clean(timeEl.textContent) : "",
    listedAt: timeEl ? (timeEl.getAttribute("datetime") || "") : "",
    location: tmText(card, '[tmid="location"]'),
    quickApply: !!card.querySelector("tm-jobs-quick-apply"),
    ...tmUrlParts(path)
  };
  return job;
}

// Work-type tags such as "Part-time, Contract". The Quick apply tag shares the same item class, so it is skipped.
function tmTagsOf(card) {
  return [...card.querySelectorAll(".tm-job-listing-attribute-tags__item")]
    .filter(el => !el.matches("tm-jobs-quick-apply") && !el.querySelector(".tm-promoted-listing-info__quickApply-tag"))
    .map(el => clean(el.textContent))
    .filter(Boolean)
    .join("; ");
}

function tmScrapeNormal(card) {
  const job = tmCommon(card, false);
  job.title = tmText(card, '[tmid="title"]');
  job.company = tmText(card, '[tmid="company"]');
  job.description = tmText(card, '[tmid="subtitle"]');
  job.bulletPoints = "";
  job.pay = tmText(card, '[tmid="approximatePayRange"]');
  job.tags = tmTagsOf(card);
  job.imageUrl = "";
  job.logoUrl = "";
  return job;
}

function tmScrapePromoted(card) {
  const job = tmCommon(card, true);
  job.title = tmText(card, ".tm-promoted-listing-info__title");
  job.company = "";   // promoted cards show the advertiser's logo instead of a name
  job.description = tmText(card, ".tm-promoted-listing-info__short-description");
  job.bulletPoints = [...card.querySelectorAll(".tm-promoted-listing-info__bullet-point")]
    .map(li => clean(li.textContent)).filter(Boolean).join(" | ");
  job.pay = tmText(card, '[tmid="approximatePayRange"]');
  job.tags = tmTagsOf(card);
  const photo = card.querySelector("img.tm-promoted-listing-card__listing-photo");
  const logo = card.querySelector(".tm-promoted-listing-card__media-container img");
  job.imageUrl = photo ? photo.src : "";
  job.logoUrl = logo ? logo.src : "";
  return job;
}

function scrapeTradeMeJobs() {
  const now = new Date().toISOString();
  const jobs = [];
  const seen = new Set();

  const add = job => {
    if (!job.title || seen.has(job.id)) return;
    seen.add(job.id);
    // Trade Me often has no pay field, so also pull a figure out of the teaser text and bullets.
    job.payInText = tmPayIn([job.description, job.bulletPoints].filter(Boolean).join(" | "));
    job.scrapedAt = now;
    jobs.push(job);
  };

  // Promoted cards first: Trade Me also lists them again in the normal results, and the first copy wins.
  document.querySelectorAll("tm-promoted-listing-card").forEach(card => add(tmScrapePromoted(card)));
  document.querySelectorAll("tm-jobs-search-card").forEach(card => add(tmScrapeNormal(card)));
  return jobs;
}

// Page 1 has no ?page= in its link, and the Next link is only there when another page exists.
function readTradeMePager() {
  const nav = document.querySelector('nav[aria-label="Pagination"]');
  const pager = { pagerFound: !!nav, hasNext: false, current: null, total: null };
  if (nav) {
    pager.hasNext = !!nav.querySelector('a[aria-label^="Next page"]');
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
      sendResponse(IS_TRADEME
        ? { jobs: scrapeTradeMeJobs(), pager: readTradeMePager() }
        : { jobs: scrapeJobs(), pager: readPager() });
    } else if (msg && msg.type === "SCRAPE_DETAIL") {
      sendResponse({ detail: scrapeJobDetail() });
    }
    return true;
  });
}
