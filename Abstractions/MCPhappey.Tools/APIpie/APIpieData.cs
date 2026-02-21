using System.ComponentModel;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.APIpie;

public static class APIpieData
{
    [Description("Search for jobs using APIpie Data Job Search endpoint.")]
    [McpServerTool(Title = "APIpie data job search", Name = "apipie_data_job_search", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> APIpie_Data_Job_Search(
        [Description("Free-form jobs search query. Include job title and location for best results.")] string query,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Page to return (1-50).")]
        int? page = 1,
        [Description("Number of pages to return, starting from page (1-50).")]
        int? num_pages = 1,
        [Description("Country code for job postings.")]
        string? country = "us",
        [Description("Language code for job postings.")]
        string? language = null,
        [Description("Filter jobs by posting date: all, today, 3days, week, or month.")]
        string? date_posted = "all",
        [Description("Only return work from home / remote jobs.")]
        bool? work_from_home = false,
        [Description("Comma-delimited employment types, e.g. FULLTIME,CONTRACTOR,PARTTIME,INTERN.")]
        string? employment_types = null,
        [Description("Comma-delimited job requirements, e.g. under_3_years_experience,no_degree.")]
        string? job_requirements = null,
        [Description("Return jobs within a certain distance from location (in km).")]
        int? radius = null,
        [Description("Comma-separated list of job publishers to exclude.")]
        string? exclude_job_publishers = null,
        [Description("Comma-separated list of fields to include in the response.")]
        string? fields = null,
        [Description("User identifier for billing observability purposes.")]
        string? user = null,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var client = serviceProvider.GetRequiredService<APIpieClient>();

                var body = new
                {
                    query,
                    page,
                    num_pages,
                    country,
                    language,
                    date_posted,
                    work_from_home,
                    employment_types,
                    job_requirements,
                    radius,
                    exclude_job_publishers,
                    fields,
                    user
                };

                return await client.PostAsync("v1/data/jsearch", body, cancellationToken)
                    ?? throw new Exception("APIpie returned no response.");
            }));

    [Description("Search real estate properties by location using APIpie Data.")]
    [McpServerTool(Title = "APIpie data real estate search by location", Name = "apipie_data_real_estate_search_location", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> APIpie_Data_Real_Estate_Search_Location(
        [Description("Location details such as county, neighborhood, or zip code.")] string location,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Page to return (1-100).")]
        int? page = 1,
        [Description("Property status: FOR_SALE, FOR_RENT, or RECENTLY_SOLD.")]
        string? home_status = "FOR_SALE",
        [Description("Comma-separated property types, e.g. HOUSES,CONDOS_COOPS.")]
        string? home_type = null,
        [Description("Space type (works for FOR_RENT), e.g. ENTIRE_PLACE.")]
        string? space_type = null,
        [Description("Sort order: DEFAULT, VERIFIED_SOURCE, PRICE_HIGH_LOW, PRICE_LOW_HIGH, NEWEST, BEDROOMS, BATHROOMS, SQUARE_FEET, or LOT_SIZE.")]
        string? sort = "DEFAULT",
        [Description("Minimum price.")]
        decimal? min_price = 0,
        [Description("Maximum price.")]
        decimal? max_price = 0,
        [Description("Minimum monthly payment (FOR_SALE only).")]
        decimal? min_monthly_payment = 0,
        [Description("Maximum monthly payment (FOR_SALE only).")]
        decimal? max_monthly_payment = 0,
        [Description("Minimum number of bedrooms.")]
        decimal? min_bedrooms = 0,
        [Description("Maximum number of bedrooms.")]
        decimal? max_bedrooms = 0,
        [Description("Minimum number of bathrooms.")]
        decimal? min_bathrooms = 0,
        [Description("Maximum number of bathrooms.")]
        decimal? max_bathrooms = 0,
        [Description("Minimum square footage.")]
        decimal? min_sqft = 0,
        [Description("Maximum square footage.")]
        decimal? max_sqft = 0,
        [Description("Minimum lot size in square feet.")]
        decimal? min_lot_size = 0,
        [Description("Maximum lot size in square feet.")]
        decimal? max_lot_size = 0,
        [Description("Listing type: BY_AGENT or BY_OWNER_OTHER.")]
        string? listing_type = "BY_AGENT",
        [Description("Include properties listed by agents.")]
        bool? for_sale_by_agent = true,
        [Description("Include properties listed by owners.")]
        bool? for_sale_by_owner = true,
        [Description("Include new construction properties.")]
        bool? for_sale_is_new_construction = true,
        [Description("Include foreclosure properties.")]
        bool? for_sale_is_foreclosure = true,
        [Description("Include auction properties.")]
        bool? for_sale_is_auction = true,
        [Description("Include already foreclosed properties.")]
        bool? for_sale_is_foreclosed = false,
        [Description("Include pre-foreclosure properties.")]
        bool? for_sale_is_preforeclosure = false,
        [Description("Maximum HOA fee.")]
        decimal? max_hoa_fee = 0,
        [Description("Include homes with incomplete HOA data.")]
        bool? includes_homes_no_hoa_data = true,
        [Description("User identifier for billing observability purposes.")]
        string? user = null,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var client = serviceProvider.GetRequiredService<APIpieClient>();

                var body = new
                {
                    location,
                    page,
                    home_status,
                    home_type,
                    space_type,
                    sort,
                    min_price,
                    max_price,
                    min_monthly_payment,
                    max_monthly_payment,
                    min_bedrooms,
                    max_bedrooms,
                    min_bathrooms,
                    max_bathrooms,
                    min_sqft,
                    max_sqft,
                    min_lot_size,
                    max_lot_size,
                    listing_type,
                    for_sale_by_agent,
                    for_sale_by_owner,
                    for_sale_is_new_construction,
                    for_sale_is_foreclosure,
                    for_sale_is_auction,
                    for_sale_is_foreclosed,
                    for_sale_is_preforeclosure,
                    max_hoa_fee,
                    includes_homes_no_hoa_data,
                    user
                };

                return await client.PostAsync("v1/data/real-estate/search", body, cancellationToken)
                    ?? throw new Exception("APIpie returned no response.");
            }));

    [Description("Search real estate properties by coordinates using APIpie Data.")]
    [McpServerTool(Title = "APIpie data real estate search by coordinates", Name = "apipie_data_real_estate_search_coordinates", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> APIpie_Data_Real_Estate_Search_Coordinates(
        [Description("Latitude coordinate.")] double lat,
        [Description("Longitude coordinate.")] double @long,
        [Description("Diameter in miles for the search area.")] double diameter,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Property status: FOR_SALE, FOR_RENT, or RECENTLY_SOLD.")]
        string? home_status = "FOR_SALE",
        [Description("Comma-separated property types, e.g. HOUSES,CONDOS_COOPS.")]
        string? home_type = null,
        [Description("Space type (works for FOR_RENT), e.g. ENTIRE_PLACE.")]
        string? space_type = null,
        [Description("Sort order: DEFAULT, VERIFIED_SOURCE, PRICE_HIGH_LOW, PRICE_LOW_HIGH, NEWEST, BEDROOMS, BATHROOMS, SQUARE_FEET, or LOT_SIZE.")]
        string? sort = "DEFAULT",
        [Description("Minimum price.")]
        decimal? min_price = 0,
        [Description("Maximum price.")]
        decimal? max_price = 0,
        [Description("Minimum monthly payment (FOR_SALE only).")]
        decimal? min_monthly_payment = 0,
        [Description("Maximum monthly payment (FOR_SALE only).")]
        decimal? max_monthly_payment = 0,
        [Description("Minimum number of bedrooms.")]
        decimal? min_bedrooms = 0,
        [Description("Maximum number of bedrooms.")]
        decimal? max_bedrooms = 0,
        [Description("Minimum number of bathrooms.")]
        decimal? min_bathrooms = 0,
        [Description("Maximum number of bathrooms.")]
        decimal? max_bathrooms = 0,
        [Description("Minimum square footage.")]
        decimal? min_sqft = 0,
        [Description("Maximum square footage.")]
        decimal? max_sqft = 0,
        [Description("Minimum lot size in square feet.")]
        decimal? min_lot_size = 0,
        [Description("Maximum lot size in square feet.")]
        decimal? max_lot_size = 0,
        [Description("Listing type: BY_AGENT or BY_OWNER_OTHER.")]
        string? listing_type = "BY_AGENT",
        [Description("Include properties listed by agents.")]
        bool? for_sale_by_agent = true,
        [Description("Include properties listed by owners.")]
        bool? for_sale_by_owner = true,
        [Description("Include new construction properties.")]
        bool? for_sale_is_new_construction = true,
        [Description("Include foreclosure properties.")]
        bool? for_sale_is_foreclosure = true,
        [Description("Include auction properties.")]
        bool? for_sale_is_auction = true,
        [Description("Include already foreclosed properties.")]
        bool? for_sale_is_foreclosed = false,
        [Description("Include pre-foreclosure properties.")]
        bool? for_sale_is_preforeclosure = false,
        [Description("Maximum HOA fee.")]
        decimal? max_hoa_fee = 0,
        [Description("Include homes with incomplete HOA data.")]
        bool? includes_homes_no_hoa_data = true,
        [Description("User identifier for billing observability purposes.")]
        string? user = null,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var client = serviceProvider.GetRequiredService<APIpieClient>();

                var body = new
                {
                    lat,
                    @long,
                    diameter,
                    home_status,
                    home_type,
                    space_type,
                    sort,
                    min_price,
                    max_price,
                    min_monthly_payment,
                    max_monthly_payment,
                    min_bedrooms,
                    max_bedrooms,
                    min_bathrooms,
                    max_bathrooms,
                    min_sqft,
                    max_sqft,
                    min_lot_size,
                    max_lot_size,
                    listing_type,
                    for_sale_by_agent,
                    for_sale_by_owner,
                    for_sale_is_new_construction,
                    for_sale_is_foreclosure,
                    for_sale_is_auction,
                    for_sale_is_foreclosed,
                    for_sale_is_preforeclosure,
                    max_hoa_fee,
                    includes_homes_no_hoa_data,
                    user
                };

                return await client.PostAsync("v1/data/real-estate/coordinates", body, cancellationToken)
                    ?? throw new Exception("APIpie returned no response.");
            }));

    [Description("Search real estate properties by polygon area using APIpie Data.")]
    [McpServerTool(Title = "APIpie data real estate search by polygon", Name = "apipie_data_real_estate_search_polygon", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> APIpie_Data_Real_Estate_Search_Polygon(
        [Description("Polygon coordinates string for the search area.")] string polygon,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Property status: FOR_SALE, FOR_RENT, or RECENTLY_SOLD.")]
        string? home_status = "FOR_SALE",
        [Description("Comma-separated property types, e.g. HOUSES,CONDOS_COOPS.")]
        string? home_type = null,
        [Description("Space type (works for FOR_RENT), e.g. ENTIRE_PLACE.")]
        string? space_type = null,
        [Description("Sort order: DEFAULT, VERIFIED_SOURCE, PRICE_HIGH_LOW, PRICE_LOW_HIGH, NEWEST, BEDROOMS, BATHROOMS, SQUARE_FEET, or LOT_SIZE.")]
        string? sort = "DEFAULT",
        [Description("Minimum price.")]
        decimal? min_price = 0,
        [Description("Maximum price.")]
        decimal? max_price = 0,
        [Description("Minimum monthly payment (FOR_SALE only).")]
        decimal? min_monthly_payment = 0,
        [Description("Maximum monthly payment (FOR_SALE only).")]
        decimal? max_monthly_payment = 0,
        [Description("Minimum number of bedrooms.")]
        decimal? min_bedrooms = 0,
        [Description("Maximum number of bedrooms.")]
        decimal? max_bedrooms = 0,
        [Description("Minimum number of bathrooms.")]
        decimal? min_bathrooms = 0,
        [Description("Maximum number of bathrooms.")]
        decimal? max_bathrooms = 0,
        [Description("Minimum square footage.")]
        decimal? min_sqft = 0,
        [Description("Maximum square footage.")]
        decimal? max_sqft = 0,
        [Description("Minimum lot size in square feet.")]
        decimal? min_lot_size = 0,
        [Description("Maximum lot size in square feet.")]
        decimal? max_lot_size = 0,
        [Description("Listing type: BY_AGENT or BY_OWNER_OTHER.")]
        string? listing_type = "BY_AGENT",
        [Description("Include properties listed by agents.")]
        bool? for_sale_by_agent = true,
        [Description("Include properties listed by owners.")]
        bool? for_sale_by_owner = true,
        [Description("Include new construction properties.")]
        bool? for_sale_is_new_construction = true,
        [Description("Include foreclosure properties.")]
        bool? for_sale_is_foreclosure = true,
        [Description("Include auction properties.")]
        bool? for_sale_is_auction = true,
        [Description("Include already foreclosed properties.")]
        bool? for_sale_is_foreclosed = false,
        [Description("Include pre-foreclosure properties.")]
        bool? for_sale_is_preforeclosure = false,
        [Description("Maximum HOA fee.")]
        decimal? max_hoa_fee = 0,
        [Description("Include homes with incomplete HOA data.")]
        bool? includes_homes_no_hoa_data = true,
        [Description("User identifier for billing observability purposes.")]
        string? user = null,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var client = serviceProvider.GetRequiredService<APIpieClient>();

                var body = new
                {
                    polygon,
                    home_status,
                    home_type,
                    space_type,
                    sort,
                    min_price,
                    max_price,
                    min_monthly_payment,
                    max_monthly_payment,
                    min_bedrooms,
                    max_bedrooms,
                    min_bathrooms,
                    max_bathrooms,
                    min_sqft,
                    max_sqft,
                    min_lot_size,
                    max_lot_size,
                    listing_type,
                    for_sale_by_agent,
                    for_sale_by_owner,
                    for_sale_is_new_construction,
                    for_sale_is_foreclosure,
                    for_sale_is_auction,
                    for_sale_is_foreclosed,
                    for_sale_is_preforeclosure,
                    max_hoa_fee,
                    includes_homes_no_hoa_data,
                    user
                };

                return await client.PostAsync("v1/data/real-estate/polygon", body, cancellationToken)
                    ?? throw new Exception("APIpie returned no response.");
            }));

    [Description("Get detailed property information by address using APIpie Data.")]
    [McpServerTool(Title = "APIpie data property details by address", Name = "apipie_data_property_details_address", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> APIpie_Data_Property_Details_Address(
        [Description("Full property address.")] string address,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("User identifier for billing observability purposes.")]
        string? user = null,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var client = serviceProvider.GetRequiredService<APIpieClient>();

                var body = new
                {
                    address,
                    user
                };

                return await client.PostAsync("v1/data/real-estate/address", body, cancellationToken)
                    ?? throw new Exception("APIpie returned no response.");
            }));
}

