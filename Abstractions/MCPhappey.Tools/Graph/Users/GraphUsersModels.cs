using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Common.Models;
using Microsoft.Graph.Beta.Models;

namespace MCPhappey.Tools.Graph.Users;

[Description("Please fill in the user details.")]
public class GraphNewUser
{
    [JsonPropertyName("givenName")]
    [Required]
    [Description("The users's given name.")]
    public string GivenName { get; set; } = default!;

    [JsonPropertyName("displayName")]
    [Required]
    [Description("The users's display name.")]
    public string DisplayName { get; set; } = default!;

    [JsonPropertyName("userPrincipalName")]
    [Required]
    [EmailAddress]
    [Description("The users's principal name.")]
    public string UserPrincipalName { get; set; } = default!;

    [JsonPropertyName("mailNickname")]
    [Required]
    [Description("The users's mail nickname.")]
    public string MailNickname { get; set; } = default!;

    [JsonPropertyName("jobTitle")]
    [Required]
    [Description("The users's job title.")]
    public string JobTitle { get; set; } = default!;


    [JsonPropertyName("accountEnabled")]
    [Required]
    [DefaultValue(true)]
    [Description("Account enabled.")]
    public bool AccountEnabled { get; set; }

    [JsonPropertyName("forceChangePasswordNextSignIn")]
    [Required]
    [DefaultValue(true)]
    [Description("Force password change.")]
    public bool ForceChangePasswordNextSignIn { get; set; }

    [JsonPropertyName("password")]
    [Required]
    [Description("The users's password.")]
    public string Password { get; set; } = default!;

    [JsonPropertyName("department")]
    [Description("The users's department.")]
    public string? Department { get; set; }

    [JsonPropertyName("companyName")]
    [Description("The users's company name.")]
    public string? CompanyName { get; set; }

    [JsonPropertyName("mobilePhone")]
    [Description("The users's mobile phone number.")]
    public string? MobilePhone { get; set; }

    [JsonPropertyName("businessPhone")]
    [Description("The users's business phone number.")]
    public string? BusinessPhone { get; set; }

    [JsonPropertyName("officeLocation")]
    [Description("The users's office location.")]
    public string? OfficeLocation { get; set; }

    [JsonPropertyName("state")]
    [Description("The users's state.")]
    public string? State { get; set; }

    [JsonPropertyName("country")]
    [Description("The users's country.")]
    public string? Country { get; set; }

    [JsonPropertyName("postalCode")]
    [Description("The users's postal code.")]
    public string? PostalCode { get; set; }

    [JsonPropertyName("city")]
    [Description("The users's city.")]
    public string? City { get; set; }
}

[Description("Please provide the user and group to link.")]
public class GraphAddUserToGroup
{
    [Required]
    [JsonPropertyName("userId")]
    [Description("The unique ID of the user to add.")]
    public string UserId { get; set; } = default!;

    [Required]
    [JsonPropertyName("groupId")]
    [Description("The unique ID of the group to which the user will be added.")]
    public string GroupId { get; set; } = default!;
}

[Description("Please fill in the user details.")]
public class GraphUpdateUser
{
    [Required]
    [JsonPropertyName("givenName")]
    [Description("The users's given name.")]
    public string GivenName { get; set; } = default!;

    [JsonPropertyName("displayName")]
    [Required]
    [Description("The users's display name.")]
    public string DisplayName { get; set; } = default!;

    [JsonPropertyName("jobTitle")]
    [Required]
    [Description("The users's job title.")]
    public string JobTitle { get; set; } = default!;

    [JsonPropertyName("accountEnabled")]
    [Required]
    [DefaultValue(true)]
    [Description("Account enabled.")]
    public bool AccountEnabled { get; set; }

    [JsonPropertyName("department")]
    [Description("The users's department.")]
    public string? Department { get; set; }

    [JsonPropertyName("companyName")]
    [Description("The users's company name.")]
    public string? CompanyName { get; set; }

    [JsonPropertyName("mobilePhone")]
    [Description("The users's mobile phone number.")]
    public string? MobilePhone { get; set; }

    [JsonPropertyName("businessPhone")]
    [Description("The users's business phone number.")]
    public string? BusinessPhone { get; set; }

    [JsonPropertyName("officeLocation")]
    [Description("The users's office location.")]
    public string? OfficeLocation { get; set; }

    [JsonPropertyName("state")]
    [Description("The users's state.")]
    public string? State { get; set; }

    [JsonPropertyName("country")]
    [Description("The users's country.")]
    public string? Country { get; set; }

    [JsonPropertyName("postalCode")]
    [Description("The users's postal code.")]
    public string? PostalCode { get; set; }

    [JsonPropertyName("city")]
    [Description("The users's city.")]
    public string? City { get; set; }
}



[Description("Please fill in the user name: {0}")]
public class GraphDeleteUser : IHasName
{
    [JsonPropertyName("name")]
    [Description("Name of the user.")]
    public string Name { get; set; } = default!;
}