using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MCPhappey.Simplicate.CRM;

public static partial class SimplicateCRM
{

    [Description("Please fill in the person details")]
    public class SimplicateNewPerson
    {
        [JsonPropertyName("initials")]
        [Required]
        [Description("The person's initials.")]
        public string? Initials { get; set; }

        [JsonPropertyName("first_name")]
        [Required]
        [Description("The person's first name.")]
        public string? FirstName { get; set; }

        [JsonPropertyName("family_name")]
        [Required]
        [Description("The person's family name or surname.")]
        public string? FamilyName { get; set; }

        [JsonPropertyName("note")]
        [Description("A note or comment about the person.")]
        public string? Note { get; set; }

        [JsonPropertyName("email")]
        [EmailAddress]
        [Description("The person's primary email address.")]
        public string? Email { get; set; }

        [JsonPropertyName("phone")]
        [Description("The person's phone number.")]
        public string? Phone { get; set; }

        [JsonPropertyName("website_url")]
        [Description("The person's website URL, if available.")]
        public Uri? WebsiteUrl { get; set; }

        [JsonPropertyName("linkedin_url")]
        [Description("LinkedIn url.")]
        public Uri? LinkedInUrl { get; set; }

        [JsonPropertyName("is_active")]
        [Description("Organization active.")]
        public bool? IsActive { get; set; }
    }


    [Description("Please fill in the person details")]
    public class SimplicatePerson
    {
        [JsonPropertyName("id")]
        [Required]
        public string Id { get; set; } = null!;

        [JsonPropertyName("initials")]
        [Required]
        [Description("The person's initials.")]
        public string? Initials { get; set; }

        [JsonPropertyName("first_name")]
        [Required]
        [Description("The person's first name.")]
        public string? FirstName { get; set; }

        [JsonPropertyName("family_name")]
        [Required]
        [Description("The person's family name or surname.")]
        public string? FamilyName { get; set; }

        [JsonPropertyName("note")]
        [Description("A note or comment about the person.")]
        public string? Note { get; set; }

        [JsonPropertyName("email")]
        [EmailAddress]
        [Description("The person's primary email address.")]
        public string? Email { get; set; }

        [JsonPropertyName("phone")]
        [Description("The person's phone number.")]
        public string? Phone { get; set; }

        [JsonPropertyName("website_url")]
        [Description("The person's website URL, if available.")]
        public Uri? WebsiteUrl { get; set; }

        [JsonPropertyName("linkedin_url")]
        [Description("LinkedIn url.")]
        public Uri? LinkedInUrl { get; set; }

        [JsonPropertyName("linked_as_contact_to_organization")]
        [Description("Organizations this person is linked to as contact.")]
        public List<SimplicatePersonOrganizationContact>? LinkedAsContactToOrganization { get; set; }

        [JsonPropertyName("is_active")]
        [Description("Person active.")]
        public bool? IsActive { get; set; }
    }

    [Description("Organization contact link for a person")]
    public class SimplicatePersonOrganizationContact
    {
        [JsonPropertyName("work_function")]
        [Description("Job title / work function of the contact person in the organization.")]
        public string? WorkFunction { get; set; }

        [JsonPropertyName("work_email")]
        [EmailAddress]
        [Description("Work email address for this organization contact link.")]
        public string? WorkEmail { get; set; }

        [JsonPropertyName("work_mobile")]
        [Description("Work mobile phone for this organization contact link.")]
        public string? WorkMobile { get; set; }

        [JsonPropertyName("organization_id")]
        [Required]
        [Description("The organization id this person should be linked to as a contact.")]
        public string? OrganizationId { get; set; }
    }

    [Description("Organization contact link details to confirm before linking a person to an organization")]
    public class SimplicatePersonOrganizationContactInput
    {
        [JsonPropertyName("organization_id")]
        [Required]
        [Description("The organization id this person should be linked to as a contact.")]
        public string? OrganizationId { get; set; }

        [JsonPropertyName("work_function")]
        [Description("Job title / work function of the contact person in the organization.")]
        public string? WorkFunction { get; set; }

        [JsonPropertyName("work_email")]
        [EmailAddress]
        [Description("Work email address for this organization contact link.")]
        public string? WorkEmail { get; set; }

        [JsonPropertyName("work_mobile")]
        [Description("Work mobile phone for this organization contact link.")]
        public string? WorkMobile { get; set; }
    }


    [Description("Please fill in the organization details")]
    public class SimplicateNewOrganization
    {
        [JsonPropertyName("name")]
        [Required]
        [Description("The full name of the organization.")]
        public string? Name { get; set; }

        [JsonPropertyName("note")]
        [Description("A note or description about the organization.")]
        public string? Note { get; set; }

        [JsonPropertyName("email")]
        [EmailAddress]
        [Description("The primary email address for the organization.")]
        public string? Email { get; set; }

        [JsonPropertyName("phone")]
        [Description("Phone number.")]
        public string? Phone { get; set; }

        [JsonPropertyName("url")]
        [Description("The main website URL of the organization.")]
        public Uri? Url { get; set; }

        [JsonPropertyName("linkedin_url")]
        [Description("LinkedIn url.")]
        public Uri? LinkedInUrl { get; set; }

        [JsonPropertyName("coc_code")]
        [Description("Coc code.")]
        public string? CocCode { get; set; }

        [JsonPropertyName("vat_number")]
        [Description("VAT number.")]
        public string? VatNumber { get; set; }

        [JsonPropertyName("industry.id")]
        [Description("Industry id")]
        public string? IndustryId { get; set; }

        [JsonPropertyName("relation_manager.id")]
        [Description("Relation manager id")]
        public string? RelationManagerId { get; set; }

        [JsonPropertyName("is_active")]
        [Description("Organization active.")]
        public bool? IsActive { get; set; }
    }


    public class SimplicateMyOrganizationProfile
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = null!;

        [JsonPropertyName("organization_id")]
        public string OrganizationId { get; set; } = null!;

        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        [JsonPropertyName("coc_code")]
        public string? CocCode { get; set; }

        [JsonPropertyName("vat_number")]
        public string? VatNumber { get; set; }

        [JsonPropertyName("bank_account")]
        public string? BankAccount { get; set; }

        [JsonPropertyName("blocked")]
        public bool? Blocked { get; set; }

        [JsonPropertyName("main_profile")]
        public bool? MainProfile { get; set; }


    }

    public class SimplicateOrganization
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = null!;

        [JsonPropertyName("name")]
        [Description("Name")]
        public string? Name { get; set; }

        [JsonPropertyName("phone")]
        [Description("Phone number")]
        public string? Phone { get; set; }

        [JsonPropertyName("url")]
        [Description("Website url")]
        public string? Url { get; set; }

        [JsonPropertyName("email")]
        [Description("Email")]
        public string? Email { get; set; }

        [JsonPropertyName("linkedin_url")]
        [Description("LinkedIn url")]
        public string? LinkedinUrl { get; set; }

        [JsonPropertyName("coc_code")]
        public string? CocCode { get; set; }

        [JsonPropertyName("vat_number")]
        public string? VatNumber { get; set; }

        [JsonPropertyName("note")]
        [Description("Notes")]
        public string? Note { get; set; }

        [JsonPropertyName("industry")]
        public SimplicateIndustry? Industry { get; set; }

        [JsonPropertyName("relation_type")]
        public SimplicateRelationType? RelationType { get; set; }

        [JsonPropertyName("relation_manager")]
        public SimplicateRelationManager? RelationManager { get; set; }

        [JsonPropertyName("is_active")]
        [Description("Organization active.")]
        public bool? IsActive { get; set; }
    }

    public class SimplicateRelationManager
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = null!;

        [JsonPropertyName("name")]
        public string? Name { get; set; } = null!;

    }

    public class SimplicateIndustry
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = null!;

        [JsonPropertyName("name")]
        public string? Name { get; set; } = null!;

    }

    public class SimplicateRelationType
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = null!;

        [JsonPropertyName("label")]
        public string? Label { get; set; } = null!;

        [JsonPropertyName("color")]
        public string Color { get; set; } = null!;

    }

}

