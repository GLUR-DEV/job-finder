namespace JobFinder.Web.Data;

/// <summary>
/// Turns Trade Me's category slugs (from the listing URL, for example "hospitality-tourism/bar-staff-baristas")
/// into the classification and sub-classification names SEEK uses, so both sites share one filter list.
///
/// To change how a Trade Me category is merged, edit <see cref="Paths"/> (one line per Trade Me category path)
/// and re-send the Trade Me jobs from the extension. A path that isn't listed there still works: it uses the
/// SEEK name for its top-level category when <see cref="Categories"/> knows it, and Trade Me's own wording
/// (made readable) for everything else, so a brand new Trade Me category shows up in the filters as its own entry.
///
/// Built from a SEEK CSV and a Trade Me CSV scraped on 2026-11-25. Only SEEK names that appeared in that SEEK CSV
/// (plus a few standard SEEK names) are used as merge targets; Trade Me categories with no SEEK match keep their own
/// name, marked "(new)" in the comments below.
/// </summary>
public static class TradeMeTaxonomy
{
    /// <summary>Trade Me top-level category slug to the SEEK classification name.</summary>
    private static readonly Dictionary<string, string> Categories = new()
    {
        ["accounting"] = "Accounting",
        ["agriculture-fishing-forestry"] = "Farming, Animals & Conservation",
        ["automotive"] = "Automotive",   // (new) SEEK files automotive jobs under Trades & Services
        ["construction-roading"] = "Construction",
        ["customer-service"] = "Call Centre & Customer Service",
        ["education"] = "Education & Training",
        ["engineering"] = "Engineering",
        ["healthcare"] = "Healthcare & Medical",
        ["hospitality-tourism"] = "Hospitality & Tourism",
        ["legal"] = "Legal",
        ["manufacturing-operations"] = "Manufacturing, Transport & Logistics",
        ["office-administration"] = "Administration & Office Support",
        ["property"] = "Real Estate & Property",
        ["retail"] = "Retail & Consumer Products",
        ["sales"] = "Sales",
        ["trades-services"] = "Trades & Services",
        ["transport-logistics"] = "Manufacturing, Transport & Logistics",
        ["other"] = "Other",   // (new)
    };

    /// <summary>"category/sub-category[/sub-sub-category]" to the SEEK classification and sub-classification.</summary>
    private static readonly Dictionary<string, (string Class, string Sub)> Paths = new()
    {
        // Accounting
        ["accounting/accounts-payable"] = ("Accounting", "Accounts Payable"),
        ["accounting/accountants/general-ca"] = ("Accounting", "Accountants - General CA"),   // (new sub)
        ["accounting/accountants/other"] = ("Accounting", "Accountants - Other"),             // (new sub)

        // Farming
        ["agriculture-fishing-forestry/horticulture"] = ("Farming, Animals & Conservation", "Horticulture"),
        ["agriculture-fishing-forestry/other"] = ("Farming, Animals & Conservation", "Other"),

        // Automotive: technicians merge into SEEK's Trades & Services, the rest is its own Trade Me category
        ["automotive/automotive-technician"] = ("Trades & Services", "Automotive Trades"),
        ["automotive/other"] = ("Automotive", "Other"),                                       // (new)
        ["automotive/sales-operations-parts"] = ("Automotive", "Sales, Operations & Parts"),  // (new)

        // Construction
        ["construction-roading/health-safety"] = ("Construction", "Health & Safety"),               // (new sub)
        ["construction-roading/labouring"] = ("Construction", "Labouring"),                         // (new sub)
        ["construction-roading/supervisors-forepersons"] = ("Construction", "Supervisors & Forepersons"),   // (new sub)

        // Customer service
        ["customer-service/call-centre"] = ("Call Centre & Customer Service", "Customer Service - Call Centre"),
        ["customer-service/customer-facing"] = ("Call Centre & Customer Service", "Customer Service - Customer Facing"),

        // Education
        ["education/early-childhood"] = ("Education & Training", "Teaching - Early Childhood"),
        ["education/other"] = ("Education & Training", "Other"),
        ["education/primary"] = ("Education & Training", "Teaching - Primary"),
        ["education/tutoring-training"] = ("Education & Training", "Tutoring"),

        // Engineering
        ["engineering/energy"] = ("Engineering", "Energy"),             // (new sub)
        ["engineering/maintenance"] = ("Engineering", "Maintenance"),   // (new sub)

        // Healthcare
        ["healthcare/administration"] = ("Healthcare & Medical", "Medical Administration"),
        ["healthcare/caregiving"] = ("Community Services & Development", "Aged & Disability Support"),
        ["healthcare/community-social-services"] = ("Community Services & Development", "Community & Social Services"),   // (new sub)
        ["healthcare/dentistry"] = ("Healthcare & Medical", "Dental"),
        ["healthcare/doctors-specialists"] = ("Healthcare & Medical", "Doctors & Specialists"),   // (new sub) SEEK splits GPs and specialists
        ["healthcare/fitness-wellbeing"] = ("Healthcare & Medical", "Fitness & Wellbeing"),       // (new sub)
        ["healthcare/occupational-therapy"] = ("Healthcare & Medical", "Physiotherapy, OT & Rehabilitation"),
        ["healthcare/other"] = ("Healthcare & Medical", "Other"),
        ["healthcare/psychology-counselling"] = ("Healthcare & Medical", "Psychology, Counselling & Social Work"),
        ["healthcare/radiography-sonography"] = ("Healthcare & Medical", "Medical Imaging"),
        ["healthcare/nursing-midwifery/community-primary-care"] = ("Healthcare & Medical", "Nursing - Community, Maternal & Child Health"),
        ["healthcare/nursing-midwifery/medical"] = ("Healthcare & Medical", "Nursing - General Medical & Surgical"),
        ["healthcare/nursing-midwifery/surgical"] = ("Healthcare & Medical", "Nursing - General Medical & Surgical"),
        ["healthcare/nursing-midwifery/theatre-recovery"] = ("Healthcare & Medical", "Nursing - Theatre & Recovery"),
        ["healthcare/nursing-midwifery/mental-health"] = ("Healthcare & Medical", "Nursing - Mental Health"),   // (new sub)
        ["healthcare/nursing-midwifery/paediatrics"] = ("Healthcare & Medical", "Nursing - Paediatrics"),       // (new sub)
        ["healthcare/nursing-midwifery/other"] = ("Healthcare & Medical", "Nursing - Other"),                   // (new sub)

        // Hospitality
        ["hospitality-tourism/bar-staff-baristas"] = ("Hospitality & Tourism", "Bar & Beverage Staff"),
        ["hospitality-tourism/chefs"] = ("Hospitality & Tourism", "Chefs/Cooks"),
        ["hospitality-tourism/housekeeping"] = ("Hospitality & Tourism", "Housekeeping"),
        ["hospitality-tourism/kitchen-staff"] = ("Hospitality & Tourism", "Kitchen & Sandwich Hands"),
        ["hospitality-tourism/management"] = ("Hospitality & Tourism", "Management"),
        ["hospitality-tourism/other"] = ("Hospitality & Tourism", "Other"),
        ["hospitality-tourism/tourism-tour-guides"] = ("Hospitality & Tourism", "Tour Guides"),
        ["hospitality-tourism/waiting-staff"] = ("Hospitality & Tourism", "Waiting Staff"),

        // Legal
        ["legal/private-practice"] = ("Legal", "Private Practice"),   // (new sub)

        // Manufacturing and transport (SEEK has one classification for both)
        ["manufacturing-operations/machine-operation"] = ("Manufacturing, Transport & Logistics", "Machine Operators"),
        ["manufacturing-operations/other"] = ("Manufacturing, Transport & Logistics", "Other"),
        ["manufacturing-operations/process-assembly"] = ("Manufacturing, Transport & Logistics", "Assembly & Process Work"),
        ["manufacturing-operations/storepersons-warehousing"] = ("Manufacturing, Transport & Logistics", "Warehousing, Storage & Distribution"),
        ["transport-logistics/drivers-couriers/couriers"] = ("Manufacturing, Transport & Logistics", "Couriers, Drivers & Postal Services"),
        ["transport-logistics/drivers-couriers/other"] = ("Manufacturing, Transport & Logistics", "Couriers, Drivers & Postal Services"),
        ["transport-logistics/drivers-couriers/truck-ht"] = ("Manufacturing, Transport & Logistics", "Road Transport"),
        ["transport-logistics/other"] = ("Manufacturing, Transport & Logistics", "Other"),
        ["transport-logistics/warehouse-distribution"] = ("Manufacturing, Transport & Logistics", "Warehousing, Storage & Distribution"),

        // Office and administration
        ["office-administration/administration"] = ("Administration & Office Support", "Administrative Assistants"),
        ["office-administration/ea-pa-secretarial"] = ("Administration & Office Support", "PA, EA & Secretarial"),
        ["office-administration/office-management"] = ("Administration & Office Support", "Office Management"),
        ["office-administration/other"] = ("Administration & Office Support", "Other"),

        // Property
        ["property/other"] = ("Real Estate & Property", "Other"),

        // Retail
        ["retail/merchandising"] = ("Retail & Consumer Products", "Merchandisers"),
        ["retail/retail-assistants"] = ("Retail & Consumer Products", "Retail Assistants"),

        // Sales
        ["sales/other"] = ("Sales", "Other"),
        ["sales/sales-reps"] = ("Sales", "Sales Representatives/Consultants"),
        ["sales/sales-support"] = ("Sales", "Sales Support"),   // (new sub)

        // Trades and services
        ["trades-services/beautician"] = ("Trades & Services", "Hair & Beauty Services"),
        ["trades-services/building-carpentry"] = ("Trades & Services", "Building Trades"),
        ["trades-services/cleaning"] = ("Trades & Services", "Cleaning Services"),
        ["trades-services/electrical"] = ("Trades & Services", "Electricians"),
        ["trades-services/gardening-landscaping"] = ("Trades & Services", "Gardening & Landscaping"),
        ["trades-services/hairdressing"] = ("Trades & Services", "Hair & Beauty Services"),
        ["trades-services/handy-persons"] = ("Trades & Services", "Maintenance & Handyperson Services"),
        ["trades-services/other"] = ("Trades & Services", "Other"),
        ["trades-services/painting"] = ("Trades & Services", "Painters & Sign Writers"),
        ["trades-services/security"] = ("Trades & Services", "Security"),   // (new sub)

        // Trade Me's catch-all category has no sub-categories
        ["other"] = ("Other", ""),   // (new)
    };

    /// <summary>The SEEK classification and sub-classification for a Trade Me category (slugs from the listing URL).</summary>
    public static (string Classification, string SubClass) Classify(string? category, string? subCategory, string? subSubCategory)
    {
        var cat = Slug(category);
        if (cat == "") return ("", "");

        var path = string.Join("/", new[] { cat, Slug(subCategory), Slug(subSubCategory) }.Where(s => s != ""));
        if (Paths.TryGetValue(path, out var hit)) return hit;

        // Not in the table: the SEEK name for the category if known, and Trade Me's own wording for the rest.
        var classification = Categories.TryGetValue(cat, out var seekName) ? seekName : Humanize(cat);
        var sub = string.Join(" - ", new[] { Humanize(subCategory), Humanize(subSubCategory) }.Where(s => s != ""));
        return (classification, sub);
    }

    /// <summary>Trade Me's work type slug as SEEK writes it ("part-time" becomes "Part time").</summary>
    public static string WorkType(string? slug) => Slug(slug) switch
    {
        "" => "",
        "part-time" => "Part time",
        "full-time" => "Full time",
        _ => Humanize(slug),
    };

    /// <summary>"manukau-city" becomes "Manukau City".</summary>
    public static string Humanize(string? slug)
    {
        var words = Slug(slug).Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(" ", words.Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
    }

    private static string Slug(string? s) => (s ?? "").Trim().ToLowerInvariant();
}
