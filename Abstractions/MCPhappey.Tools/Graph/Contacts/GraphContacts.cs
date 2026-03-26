using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.Contacts;

public static class GraphContacts
{
    [Description("Create a new Outlook contact for the signed-in user")]
    [McpServerTool(Title = "Create Outlook contact", Destructive = false, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphContacts_CreateContact(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional contact folder id. If provided, contact is created in that folder.")] string? contactFolderId = null,
        [Description("Given name.")] string? givenName = null,
        [Description("Middle name.")] string? middleName = null,
        [Description("Surname / family name.")] string? surname = null,
        [Description("Display name.")] string? displayName = null,
        [Description("Company name.")] string? companyName = null,
        [Description("Job title.")] string? jobTitle = null,
        [Description("Department.")] string? department = null,
        [Description("Office location.")] string? officeLocation = null,
        [Description("Assistant name.")] string? assistantName = null,
        [Description("Primary mobile phone.")] string? mobilePhone = null,
        [Description("Business phones as comma-separated values.")] string? businessPhonesCsv = null,
        [Description("Home phones as comma-separated values.")] string? homePhonesCsv = null,
        [Description("Email addresses as comma-separated values.")] string? emailAddressesCsv = null,
        [Description("Instant messaging addresses as comma-separated values.")] string? imAddressesCsv = null,
        [Description("Categories as comma-separated values.")] string? categoriesCsv = null,
        [Description("Websites as comma-separated values. First value is stored as businessHomePage.")] string? websitesCsv = null,
        [Description("Birthday date.")] DateTime? birthday = null,
        [Description("Personal notes.")] string? notes = null,
        [Description("Business street.")] string? businessStreet = null,
        [Description("Business city.")] string? businessCity = null,
        [Description("Business state/province.")] string? businessState = null,
        [Description("Business postal code.")] string? businessPostalCode = null,
        [Description("Business country/region.")] string? businessCountryOrRegion = null,
        [Description("Home street.")] string? homeStreet = null,
        [Description("Home city.")] string? homeCity = null,
        [Description("Home state/province.")] string? homeState = null,
        [Description("Home postal code.")] string? homePostalCode = null,
        [Description("Home country/region.")] string? homeCountryOrRegion = null,
        [Description("Other street.")] string? otherStreet = null,
        [Description("Other city.")] string? otherCity = null,
        [Description("Other state/province.")] string? otherState = null,
        [Description("Other postal code.")] string? otherPostalCode = null,
        [Description("Other country/region.")] string? otherCountryOrRegion = null,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new GraphContactsUpsertContactInput
                {
                    ContactFolderId = contactFolderId,
                    GivenName = givenName,
                    MiddleName = middleName,
                    Surname = surname,
                    DisplayName = displayName,
                    CompanyName = companyName,
                    JobTitle = jobTitle,
                    Department = department,
                    OfficeLocation = officeLocation,
                    AssistantName = assistantName,
                    MobilePhone = mobilePhone,
                    BusinessPhonesCsv = businessPhonesCsv,
                    HomePhonesCsv = homePhonesCsv,
                    EmailAddressesCsv = emailAddressesCsv,
                    ImAddressesCsv = imAddressesCsv,
                    CategoriesCsv = categoriesCsv,
                    WebsitesCsv = websitesCsv,
                    Birthday = birthday,
                    Notes = notes,
                    BusinessStreet = businessStreet,
                    BusinessCity = businessCity,
                    BusinessState = businessState,
                    BusinessPostalCode = businessPostalCode,
                    BusinessCountryOrRegion = businessCountryOrRegion,
                    HomeStreet = homeStreet,
                    HomeCity = homeCity,
                    HomeState = homeState,
                    HomePostalCode = homePostalCode,
                    HomeCountryOrRegion = homeCountryOrRegion,
                    OtherStreet = otherStreet,
                    OtherCity = otherCity,
                    OtherState = otherState,
                    OtherPostalCode = otherPostalCode,
                    OtherCountryOrRegion = otherCountryOrRegion,
                },
                cancellationToken);

            if (notAccepted != null)
                return default(Contact);

            var contact = ToGraphContact(typed ?? new GraphContactsUpsertContactInput());
            if (!string.IsNullOrWhiteSpace(typed?.ContactFolderId))
            {
                return await client.Me.ContactFolders[typed.ContactFolderId]
                    .Contacts
                    .PostAsync(contact, cancellationToken: cancellationToken);
            }

            return await client.Me.Contacts.PostAsync(contact, cancellationToken: cancellationToken);
        })));

    [Description("Update an existing Outlook contact")]
    [McpServerTool(Title = "Update Outlook contact", Destructive = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphContacts_UpdateContact(
        [Description("The contact id.")] string contactId,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Given name.")] string? givenName = null,
        [Description("Middle name.")] string? middleName = null,
        [Description("Surname / family name.")] string? surname = null,
        [Description("Display name.")] string? displayName = null,
        [Description("Company name.")] string? companyName = null,
        [Description("Job title.")] string? jobTitle = null,
        [Description("Department.")] string? department = null,
        [Description("Office location.")] string? officeLocation = null,
        [Description("Assistant name.")] string? assistantName = null,
        [Description("Primary mobile phone.")] string? mobilePhone = null,
        [Description("Business phones as comma-separated values.")] string? businessPhonesCsv = null,
        [Description("Home phones as comma-separated values.")] string? homePhonesCsv = null,
        [Description("Email addresses as comma-separated values.")] string? emailAddressesCsv = null,
        [Description("Instant messaging addresses as comma-separated values.")] string? imAddressesCsv = null,
        [Description("Categories as comma-separated values.")] string? categoriesCsv = null,
        [Description("Websites as comma-separated values. First value is stored as businessHomePage.")] string? websitesCsv = null,
        [Description("Birthday date.")] DateTime? birthday = null,
        [Description("Personal notes.")] string? notes = null,
        [Description("Business street.")] string? businessStreet = null,
        [Description("Business city.")] string? businessCity = null,
        [Description("Business state/province.")] string? businessState = null,
        [Description("Business postal code.")] string? businessPostalCode = null,
        [Description("Business country/region.")] string? businessCountryOrRegion = null,
        [Description("Home street.")] string? homeStreet = null,
        [Description("Home city.")] string? homeCity = null,
        [Description("Home state/province.")] string? homeState = null,
        [Description("Home postal code.")] string? homePostalCode = null,
        [Description("Home country/region.")] string? homeCountryOrRegion = null,
        [Description("Other street.")] string? otherStreet = null,
        [Description("Other city.")] string? otherCity = null,
        [Description("Other state/province.")] string? otherState = null,
        [Description("Other postal code.")] string? otherPostalCode = null,
        [Description("Other country/region.")] string? otherCountryOrRegion = null,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var existing = await client.Me.Contacts[contactId].GetAsync(cancellationToken: cancellationToken);
            var existingInput = ToUpsertInput(existing);
            var incoming = new GraphContactsUpsertContactInput
            {
                GivenName = givenName,
                MiddleName = middleName,
                Surname = surname,
                DisplayName = displayName,
                CompanyName = companyName,
                JobTitle = jobTitle,
                Department = department,
                OfficeLocation = officeLocation,
                AssistantName = assistantName,
                MobilePhone = mobilePhone,
                BusinessPhonesCsv = businessPhonesCsv,
                HomePhonesCsv = homePhonesCsv,
                EmailAddressesCsv = emailAddressesCsv,
                ImAddressesCsv = imAddressesCsv,
                CategoriesCsv = categoriesCsv,
                WebsitesCsv = websitesCsv,
                Birthday = birthday,
                Notes = notes,
                BusinessStreet = businessStreet,
                BusinessCity = businessCity,
                BusinessState = businessState,
                BusinessPostalCode = businessPostalCode,
                BusinessCountryOrRegion = businessCountryOrRegion,
                HomeStreet = homeStreet,
                HomeCity = homeCity,
                HomeState = homeState,
                HomePostalCode = homePostalCode,
                HomeCountryOrRegion = homeCountryOrRegion,
                OtherStreet = otherStreet,
                OtherCity = otherCity,
                OtherState = otherState,
                OtherPostalCode = otherPostalCode,
                OtherCountryOrRegion = otherCountryOrRegion,
            };

            var elicitSeed = OverlayProvidedFields(existingInput, incoming, requestContext);

            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                elicitSeed,
                cancellationToken);

            if (notAccepted != null)
                return default(Contact);

            var contact = ToGraphContact(typed ?? elicitSeed);
            return await client.Me.Contacts[contactId].PatchAsync(contact, cancellationToken: cancellationToken);
        })));

    [Description("Delete an Outlook contact")]
    [McpServerTool(Title = "Delete Outlook contact", Destructive = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphContacts_DeleteContact(
        [Description("The contact id to delete.")] string contactId,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.ConfirmAndDeleteAsync<GraphDeleteContact>(
            contactId,
            async _ => await client.Me.Contacts[contactId].DeleteAsync(cancellationToken: cancellationToken),
            "Contact deleted.",
            cancellationToken));

    [Description("Create a new Outlook contact folder")]
    [McpServerTool(Title = "Create Outlook contact folder", Destructive = false, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphContacts_CreateContactFolder(
        [Description("Display name of the contact folder.")] string displayName,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional parent folder id. If provided, a child folder is created.")] string? parentFolderId = null,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new GraphContactsUpsertFolderInput
                {
                    DisplayName = displayName,
                    ParentFolderId = parentFolderId
                },
                cancellationToken);

            if (notAccepted != null)
                return default(ContactFolder);

            var folder = new ContactFolder
            {
                DisplayName = typed?.DisplayName
            };

            if (!string.IsNullOrWhiteSpace(typed?.ParentFolderId))
            {
                return await client.Me.ContactFolders[typed.ParentFolderId]
                    .ChildFolders
                    .PostAsync(folder, cancellationToken: cancellationToken);
            }

            return await client.Me.ContactFolders.PostAsync(folder, cancellationToken: cancellationToken);
        })));

    [Description("Update an Outlook contact folder")]
    [McpServerTool(Title = "Update Outlook contact folder", Destructive = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphContacts_UpdateContactFolder(
        [Description("The contact folder id.")] string folderId,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Updated display name.")] string? displayName = null,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var existing = await client.Me.ContactFolders[folderId].GetAsync(cancellationToken: cancellationToken);
            var existingInput = new GraphContactsUpsertFolderInput
            {
                DisplayName = existing?.DisplayName ?? string.Empty,
                ParentFolderId = null
            };

            var incoming = new GraphContactsUpsertFolderInput
            {
                DisplayName = displayName ?? string.Empty
            };

            var elicitSeed = new GraphContactsUpsertFolderInput
            {
                DisplayName = IsProvided(requestContext, "displayName")
                    ? incoming.DisplayName
                    : existingInput.DisplayName
            };

            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                elicitSeed,
                cancellationToken);

            return await client.Me.ContactFolders[folderId]
                .PatchAsync(new ContactFolder { DisplayName = typed?.DisplayName }, cancellationToken: cancellationToken);
        })));

    [Description("Delete an Outlook contact folder")]
    [McpServerTool(Title = "Delete Outlook contact folder", Destructive = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphContacts_DeleteContactFolder(
        [Description("The contact folder id to delete.")] string folderId,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.ConfirmAndDeleteAsync<GraphDeleteContactFolder>(
            folderId,
            async _ => await client.Me.ContactFolders[folderId].DeleteAsync(cancellationToken: cancellationToken),
            "Contact folder deleted.",
            cancellationToken));

    [Description("Please confirm the contact id to delete: {0}")]
    public class GraphDeleteContact : MCPhappey.Common.Models.IHasName
    {
        [JsonPropertyName("name")]
        [Required]
        [Description("The contact id.")]
        public string Name { get; set; } = default!;
    }

    [Description("Please confirm the contact folder id to delete: {0}")]
    public class GraphDeleteContactFolder : MCPhappey.Common.Models.IHasName
    {
        [JsonPropertyName("name")]
        [Required]
        [Description("The contact folder id.")]
        public string Name { get; set; } = default!;
    }

    [Description("Please fill in the Outlook contact fields")]
    public class GraphContactsUpsertContactInput
    {
        [JsonPropertyName("contactFolderId")]
        [Description("Optional contact folder id for create.")]
        public string? ContactFolderId { get; set; }

        [JsonPropertyName("givenName")]
        [Description("Given name.")]
        public string? GivenName { get; set; }

        [JsonPropertyName("middleName")]
        [Description("Middle name.")]
        public string? MiddleName { get; set; }

        [JsonPropertyName("surname")]
        [Description("Surname / family name.")]
        public string? Surname { get; set; }

        [JsonPropertyName("displayName")]
        [Description("Display name.")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("companyName")]
        [Description("Company name.")]
        public string? CompanyName { get; set; }

        [JsonPropertyName("jobTitle")]
        [Description("Job title.")]
        public string? JobTitle { get; set; }

        [JsonPropertyName("department")]
        [Description("Department.")]
        public string? Department { get; set; }

        [JsonPropertyName("officeLocation")]
        [Description("Office location.")]
        public string? OfficeLocation { get; set; }

        [JsonPropertyName("assistantName")]
        [Description("Assistant name.")]
        public string? AssistantName { get; set; }

        [JsonPropertyName("mobilePhone")]
        [Description("Primary mobile phone.")]
        public string? MobilePhone { get; set; }

        [JsonPropertyName("businessPhonesCsv")]
        [Description("Business phones as comma-separated values.")]
        public string? BusinessPhonesCsv { get; set; }

        [JsonPropertyName("homePhonesCsv")]
        [Description("Home phones as comma-separated values.")]
        public string? HomePhonesCsv { get; set; }

        [JsonPropertyName("emailAddressesCsv")]
        [Description("Email addresses as comma-separated values.")]
        public string? EmailAddressesCsv { get; set; }

        [JsonPropertyName("imAddressesCsv")]
        [Description("Instant messaging addresses as comma-separated values.")]
        public string? ImAddressesCsv { get; set; }

        [JsonPropertyName("categoriesCsv")]
        [Description("Categories as comma-separated values.")]
        public string? CategoriesCsv { get; set; }

        [JsonPropertyName("websitesCsv")]
        [Description("Websites as comma-separated values.")]
        public string? WebsitesCsv { get; set; }

        [JsonPropertyName("birthday")]
        [Description("Birthday date.")]
        public DateTime? Birthday { get; set; }

        [JsonPropertyName("notes")]
        [Description("Personal notes.")]
        public string? Notes { get; set; }

        [JsonPropertyName("businessStreet")]
        [Description("Business street.")]
        public string? BusinessStreet { get; set; }

        [JsonPropertyName("businessCity")]
        [Description("Business city.")]
        public string? BusinessCity { get; set; }

        [JsonPropertyName("businessState")]
        [Description("Business state/province.")]
        public string? BusinessState { get; set; }

        [JsonPropertyName("businessPostalCode")]
        [Description("Business postal code.")]
        public string? BusinessPostalCode { get; set; }

        [JsonPropertyName("businessCountryOrRegion")]
        [Description("Business country/region.")]
        public string? BusinessCountryOrRegion { get; set; }

        [JsonPropertyName("homeStreet")]
        [Description("Home street.")]
        public string? HomeStreet { get; set; }

        [JsonPropertyName("homeCity")]
        [Description("Home city.")]
        public string? HomeCity { get; set; }

        [JsonPropertyName("homeState")]
        [Description("Home state/province.")]
        public string? HomeState { get; set; }

        [JsonPropertyName("homePostalCode")]
        [Description("Home postal code.")]
        public string? HomePostalCode { get; set; }

        [JsonPropertyName("homeCountryOrRegion")]
        [Description("Home country/region.")]
        public string? HomeCountryOrRegion { get; set; }

        [JsonPropertyName("otherStreet")]
        [Description("Other street.")]
        public string? OtherStreet { get; set; }

        [JsonPropertyName("otherCity")]
        [Description("Other city.")]
        public string? OtherCity { get; set; }

        [JsonPropertyName("otherState")]
        [Description("Other state/province.")]
        public string? OtherState { get; set; }

        [JsonPropertyName("otherPostalCode")]
        [Description("Other postal code.")]
        public string? OtherPostalCode { get; set; }

        [JsonPropertyName("otherCountryOrRegion")]
        [Description("Other country/region.")]
        public string? OtherCountryOrRegion { get; set; }
    }

    [Description("Please fill in the Outlook contact folder fields")]
    public class GraphContactsUpsertFolderInput
    {
        [JsonPropertyName("displayName")]
        [Required]
        [Description("Display name of the contact folder.")]
        public string DisplayName { get; set; } = default!;

        [JsonPropertyName("parentFolderId")]
        [Description("Optional parent folder id.")]
        public string? ParentFolderId { get; set; }
    }

    private static Contact ToGraphContact(GraphContactsUpsertContactInput input)
    {
        var businessPhones = SplitCsv(input.BusinessPhonesCsv);
        var homePhones = SplitCsv(input.HomePhonesCsv);
        var imAddresses = SplitCsv(input.ImAddressesCsv);
        var categories = SplitCsv(input.CategoriesCsv);
        var websites = SplitCsv(input.WebsitesCsv);
        var phones = BuildPhones(input.MobilePhone, businessPhones, homePhones);
        var postalAddresses = BuildPostalAddresses(input);
        var emails = SplitCsv(input.EmailAddressesCsv)
            .Select((a, i) => new TypedEmailAddress
            {
                Address = a,
                Type = i == 0
                    ? ParseEnumOrNull<EmailType>("main")
                    : ParseEnumOrNull<EmailType>("other")
            })
            .ToList();

        return new Contact
        {
            GivenName = input.GivenName,
            MiddleName = input.MiddleName,
            Surname = input.Surname,
            DisplayName = input.DisplayName,
            CompanyName = input.CompanyName,
            JobTitle = input.JobTitle,
            Department = input.Department,
            OfficeLocation = input.OfficeLocation,
            AssistantName = input.AssistantName,
            Phones = phones,
            ImAddresses = imAddresses,
            Categories = categories,
            EmailAddresses = emails,
            PrimaryEmailAddress = emails.FirstOrDefault() != null ? new EmailAddress { Address = emails[0].Address } : null,
            SecondaryEmailAddress = emails.Count > 1 ? new EmailAddress { Address = emails[1].Address } : null,
            TertiaryEmailAddress = emails.Count > 2 ? new EmailAddress { Address = emails[2].Address } : null,
            Websites = [.. websites.Select((a, i) => new Website
            {
                Address = a,
                Type = i == 0
                    ? ParseEnumOrNull<WebsiteType>("work")
                    : ParseEnumOrNull<WebsiteType>("other")
            })],
            PersonalNotes = input.Notes,
            Birthday = input.Birthday.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(input.Birthday.Value, DateTimeKind.Utc))
                : null,
            PostalAddresses = postalAddresses
        };
    }

    private static List<string> SplitCsv(string? csv)
        => string.IsNullOrWhiteSpace(csv)
            ? []
            : [.. csv
                .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Distinct(StringComparer.OrdinalIgnoreCase)];

    private static List<Phone> BuildPhones(string? mobilePhone, List<string> businessPhones, List<string> homePhones)
    {
        var phones = new List<Phone>();

        if (!string.IsNullOrWhiteSpace(mobilePhone))
        {
            phones.Add(new Phone
            {
                Number = mobilePhone,
                Type = ParseEnumOrNull<PhoneType>("mobile")
            });
        }

        phones.AddRange(businessPhones.Select(a => new Phone
        {
            Number = a,
            Type = ParseEnumOrNull<PhoneType>("business")
        }));

        phones.AddRange(homePhones.Select(a => new Phone
        {
            Number = a,
            Type = ParseEnumOrNull<PhoneType>("home")
        }));

        return phones;
    }

    private static List<PhysicalAddress> BuildPostalAddresses(GraphContactsUpsertContactInput input)
    {
        var addresses = new List<PhysicalAddress>();

        var business = ToAddress(
            input.BusinessStreet,
            input.BusinessCity,
            input.BusinessState,
            input.BusinessPostalCode,
            input.BusinessCountryOrRegion,
            "business");
        if (business != null) addresses.Add(business);

        var home = ToAddress(
            input.HomeStreet,
            input.HomeCity,
            input.HomeState,
            input.HomePostalCode,
            input.HomeCountryOrRegion,
            "home");
        if (home != null) addresses.Add(home);

        var other = ToAddress(
            input.OtherStreet,
            input.OtherCity,
            input.OtherState,
            input.OtherPostalCode,
            input.OtherCountryOrRegion,
            "other");
        if (other != null) addresses.Add(other);

        return addresses;
    }

    private static PhysicalAddress? ToAddress(
        string? street,
        string? city,
        string? state,
        string? postalCode,
        string? countryOrRegion,
        string type)
    {
        if (string.IsNullOrWhiteSpace(street)
            && string.IsNullOrWhiteSpace(city)
            && string.IsNullOrWhiteSpace(state)
            && string.IsNullOrWhiteSpace(postalCode)
            && string.IsNullOrWhiteSpace(countryOrRegion))
            return null;

        return new PhysicalAddress
        {
            Street = street,
            City = city,
            State = state,
            PostalCode = postalCode,
            CountryOrRegion = countryOrRegion,
            Type = ParseEnumOrNull<PhysicalAddressType>(type)
        };
    }

    private static GraphContactsUpsertContactInput OverlayProvidedFields(
        GraphContactsUpsertContactInput existing,
        GraphContactsUpsertContactInput incoming,
        RequestContext<CallToolRequestParams> requestContext)
    {
        return new GraphContactsUpsertContactInput
        {
            ContactFolderId = Merge("contactFolderId", incoming.ContactFolderId, existing.ContactFolderId, requestContext),
            GivenName = Merge("givenName", incoming.GivenName, existing.GivenName, requestContext),
            MiddleName = Merge("middleName", incoming.MiddleName, existing.MiddleName, requestContext),
            Surname = Merge("surname", incoming.Surname, existing.Surname, requestContext),
            DisplayName = Merge("displayName", incoming.DisplayName, existing.DisplayName, requestContext),
            CompanyName = Merge("companyName", incoming.CompanyName, existing.CompanyName, requestContext),
            JobTitle = Merge("jobTitle", incoming.JobTitle, existing.JobTitle, requestContext),
            Department = Merge("department", incoming.Department, existing.Department, requestContext),
            OfficeLocation = Merge("officeLocation", incoming.OfficeLocation, existing.OfficeLocation, requestContext),
            AssistantName = Merge("assistantName", incoming.AssistantName, existing.AssistantName, requestContext),
            MobilePhone = Merge("mobilePhone", incoming.MobilePhone, existing.MobilePhone, requestContext),
            BusinessPhonesCsv = Merge("businessPhonesCsv", incoming.BusinessPhonesCsv, existing.BusinessPhonesCsv, requestContext),
            HomePhonesCsv = Merge("homePhonesCsv", incoming.HomePhonesCsv, existing.HomePhonesCsv, requestContext),
            EmailAddressesCsv = Merge("emailAddressesCsv", incoming.EmailAddressesCsv, existing.EmailAddressesCsv, requestContext),
            ImAddressesCsv = Merge("imAddressesCsv", incoming.ImAddressesCsv, existing.ImAddressesCsv, requestContext),
            CategoriesCsv = Merge("categoriesCsv", incoming.CategoriesCsv, existing.CategoriesCsv, requestContext),
            WebsitesCsv = Merge("websitesCsv", incoming.WebsitesCsv, existing.WebsitesCsv, requestContext),
            Birthday = Merge("birthday", incoming.Birthday, existing.Birthday, requestContext),
            Notes = Merge("notes", incoming.Notes, existing.Notes, requestContext),
            BusinessStreet = Merge("businessStreet", incoming.BusinessStreet, existing.BusinessStreet, requestContext),
            BusinessCity = Merge("businessCity", incoming.BusinessCity, existing.BusinessCity, requestContext),
            BusinessState = Merge("businessState", incoming.BusinessState, existing.BusinessState, requestContext),
            BusinessPostalCode = Merge("businessPostalCode", incoming.BusinessPostalCode, existing.BusinessPostalCode, requestContext),
            BusinessCountryOrRegion = Merge("businessCountryOrRegion", incoming.BusinessCountryOrRegion, existing.BusinessCountryOrRegion, requestContext),
            HomeStreet = Merge("homeStreet", incoming.HomeStreet, existing.HomeStreet, requestContext),
            HomeCity = Merge("homeCity", incoming.HomeCity, existing.HomeCity, requestContext),
            HomeState = Merge("homeState", incoming.HomeState, existing.HomeState, requestContext),
            HomePostalCode = Merge("homePostalCode", incoming.HomePostalCode, existing.HomePostalCode, requestContext),
            HomeCountryOrRegion = Merge("homeCountryOrRegion", incoming.HomeCountryOrRegion, existing.HomeCountryOrRegion, requestContext),
            OtherStreet = Merge("otherStreet", incoming.OtherStreet, existing.OtherStreet, requestContext),
            OtherCity = Merge("otherCity", incoming.OtherCity, existing.OtherCity, requestContext),
            OtherState = Merge("otherState", incoming.OtherState, existing.OtherState, requestContext),
            OtherPostalCode = Merge("otherPostalCode", incoming.OtherPostalCode, existing.OtherPostalCode, requestContext),
            OtherCountryOrRegion = Merge("otherCountryOrRegion", incoming.OtherCountryOrRegion, existing.OtherCountryOrRegion, requestContext)
        };
    }

    private static GraphContactsUpsertContactInput ToUpsertInput(Contact? contact)
    {
        if (contact == null)
            return new GraphContactsUpsertContactInput();

        var businessAddress = GetAddressByType(contact.PostalAddresses, "business");
        var homeAddress = GetAddressByType(contact.PostalAddresses, "home");
        var otherAddress = GetAddressByType(contact.PostalAddresses, "other");

        return new GraphContactsUpsertContactInput
        {
            ContactFolderId = contact.ParentFolderId,
            GivenName = contact.GivenName,
            MiddleName = contact.MiddleName,
            Surname = contact.Surname,
            DisplayName = contact.DisplayName,
            CompanyName = contact.CompanyName,
            JobTitle = contact.JobTitle,
            Department = contact.Department,
            OfficeLocation = contact.OfficeLocation,
            AssistantName = contact.AssistantName,
            MobilePhone = GetPhoneByType(contact.Phones, "mobile"),
            BusinessPhonesCsv = string.Join(',', GetPhonesByType(contact.Phones, "business")),
            HomePhonesCsv = string.Join(',', GetPhonesByType(contact.Phones, "home")),
            EmailAddressesCsv = string.Join(',', contact.EmailAddresses?.Select(a => a.Address).Where(a => !string.IsNullOrWhiteSpace(a)) ?? []),
            ImAddressesCsv = string.Join(',', contact.ImAddresses ?? []),
            CategoriesCsv = string.Join(',', contact.Categories ?? []),
            WebsitesCsv = string.Join(',', contact.Websites?.Select(a => a.Address).Where(a => !string.IsNullOrWhiteSpace(a)) ?? []),
            Birthday = contact.Birthday?.UtcDateTime,
            Notes = contact.PersonalNotes,
            BusinessStreet = businessAddress?.Street,
            BusinessCity = businessAddress?.City,
            BusinessState = businessAddress?.State,
            BusinessPostalCode = businessAddress?.PostalCode,
            BusinessCountryOrRegion = businessAddress?.CountryOrRegion,
            HomeStreet = homeAddress?.Street,
            HomeCity = homeAddress?.City,
            HomeState = homeAddress?.State,
            HomePostalCode = homeAddress?.PostalCode,
            HomeCountryOrRegion = homeAddress?.CountryOrRegion,
            OtherStreet = otherAddress?.Street,
            OtherCity = otherAddress?.City,
            OtherState = otherAddress?.State,
            OtherPostalCode = otherAddress?.PostalCode,
            OtherCountryOrRegion = otherAddress?.CountryOrRegion
        };
    }

    private static bool IsProvided(RequestContext<CallToolRequestParams> requestContext, string fieldName)
        => requestContext.Params?.Arguments?.ContainsKey(fieldName) == true;

    private static string? Merge(string fieldName, string? incoming, string? existing, RequestContext<CallToolRequestParams> requestContext)
        => IsProvided(requestContext, fieldName) ? incoming : existing;

    private static DateTime? Merge(string fieldName, DateTime? incoming, DateTime? existing, RequestContext<CallToolRequestParams> requestContext)
        => IsProvided(requestContext, fieldName) ? incoming : existing;

    private static PhysicalAddress? GetAddressByType(IEnumerable<PhysicalAddress>? addresses, string type)
        => addresses?.FirstOrDefault(a => string.Equals(a.Type?.ToString(), type, StringComparison.OrdinalIgnoreCase));

    private static string? GetPhoneByType(IEnumerable<Phone>? phones, string type)
        => phones?.FirstOrDefault(a => string.Equals(a.Type?.ToString(), type, StringComparison.OrdinalIgnoreCase))?.Number;

    private static IEnumerable<string> GetPhonesByType(IEnumerable<Phone>? phones, string type)
        => phones?
            .Where(a => string.Equals(a.Type?.ToString(), type, StringComparison.OrdinalIgnoreCase))
            .Select(a => a.Number)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Cast<string>()
            ?? [];

    private static TEnum? ParseEnumOrNull<TEnum>(string value)
        where TEnum : struct, Enum
        => Enum.TryParse<TEnum>(value, true, out var parsed) ? parsed : null;
}

