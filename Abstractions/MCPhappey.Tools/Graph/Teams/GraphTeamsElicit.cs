using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.Graph.Beta.Models;

namespace MCPhappey.Tools.Graph.Teams;

[Description(@"Set a Teams status message. 
You can provide an optional expiry date/time. Leave expiry empty for no automatic removal.")]
public class GraphSetStatusMessage
{
    [JsonPropertyName("message")]
    [Required]
    [Description("The status message text. Example: 'Hey I am currently in a meeting.'")]
    public string Message { get; set; } = default!;

    [JsonPropertyName("bodyType")]
    [Required]
    [Description("Body type")]
    public BodyType BodyType { get; set; } = BodyType.Text;

    [JsonPropertyName("expiryDateTime")]
    [Description("Expiry date/time in ISO8601 (e.g. 2025-07-26T12:00:00). Optional.")]
    public string? ExpiryDateTime { get; set; }

    [JsonPropertyName("timeZone")]
    [Description("Timezone for expiry (e.g. 'W. Europe Standard Time', 'Pacific Standard Time'). Optional, default is UTC.")]
    public string? TimeZone { get; set; }
}

[Description("Please provide the presence status to set for Teams.")]
public class GraphSetPresence
{
    [JsonPropertyName("availability")]
    [Required]
    [Description("Base presence status. Examples: Available, Busy, Away, DoNotDisturb, Offline.")]
    public string Availability { get; set; } = default!;

    [JsonPropertyName("activity")]
    [Required]
    [Description("Supplemental activity. Examples: Available, InACall, InAConferenceCall, Presenting, Away.")]
    public string Activity { get; set; } = default!;

    [JsonPropertyName("expirationDuration")]
    [Description("Expiration in ISO8601 duration format (e.g. PT1H for 1 hour).")]
    public string? ExpirationDuration { get; set; }
}

[Description("Please fill in the Team details.")]
public class GraphNewTeam
{
    [JsonPropertyName("displayName")]
    [Required]
    [Description("The team display name.")]
    public string DisplayName { get; set; } = default!;

    [JsonPropertyName("description")]
    [Description("The team description.")]
    public string? Description { get; set; }

    [JsonPropertyName("firstChannelName")]
    [Description("The team first channel name.")]
    public string? FirstChannelName { get; set; }

    [JsonPropertyName("visibility")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    [Description("The team visibility.")]
    public TeamVisibilityType Visibility { get; set; }

    [JsonPropertyName("allowMembersCreateUpdateChannels")]
    [Description("If members are allowed to create and update channels.")]
    [DefaultValue(true)]
    public bool AllowCreateUpdateChannels { get; set; } = true;

}

[Description("Please fill in the Team channel details.")]
public class GraphNewTeamChannel
{
    [JsonPropertyName("displayName")]
    [Required]
    [Description("The team channel display name.")]
    public string DisplayName { get; set; } = default!;

    [JsonPropertyName("description")]
    [Description("The team channel description.")]
    public string? Description { get; set; }

    [JsonPropertyName("membershipType")]
    [Required]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    [Description("The team channel membership type.")]
    public ChannelMembershipType MembershipType { get; set; }
}

[Description("Please fill in the Team channel message details.")]
public class GraphNewChannelMessage
{
    [JsonPropertyName("subject")]
    [Required]
    [Description("Subject of the channel message.")]
    public string? Subject { get; set; }

    [JsonPropertyName("content")]
    [Required]
    [Description("Content of the channel message.")]
    public string? Content { get; set; }

    [JsonPropertyName("importance")]
    [Required]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    [Description("Importance of the channel message.")]
    public ChatMessageImportance? Importance { get; set; }

}

/// <summary>
/// Data for creating a calendar event.
/// </summary>
[Description("Fill in the details for the new calendar event.")]
public class GraphCreateCalendarEvent
{
    [JsonPropertyName("subject")]
    [Required]
    [Description("Title or subject of the event.")]
    public string Subject { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    [Description("Description or body of the event.")]
    public string? Body { get; set; }

    [JsonPropertyName("bodyType")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    [Description("Type of the body content (html or text).")]
    public BodyType? BodyType { get; set; }

    [JsonPropertyName("startDateTime")]
    [Required]
    [Description("Start date and time of the event (yyyy-MM-ddTHH:mm:ss format, e.g., 2025-07-05T13:30:00).")]
    public string StartDateTime { get; set; } = string.Empty;

    [JsonPropertyName("endDateTime")]
    [Required]
    [Description("End date and time of the event (yyyy-MM-ddTHH:mm:ss format, e.g., 2025-07-05T14:30:00).")]
    public string EndDateTime { get; set; } = string.Empty;

    [JsonPropertyName("timeZone")]
    [Description("Time zone for the event (e.g., 'W. Europe Standard Time', 'UTC'). Defaults to UTC.")]
    public string? TimeZone { get; set; }

    [JsonPropertyName("location")]
    [Description("Location or meeting room for the event.")]
    public string? Location { get; set; }

    [JsonPropertyName("attendees")]
    [Description("E-mail addresses of attendees. Use a comma separated list for multiple recipients.")]
    public string? Attendees { get; set; }
}



[Description("Fill in the properties to edit on the Microsoft Team.")]
public class GraphEditTeam
{
    [JsonPropertyName("teamId")]
    [Required]
    [Description("ID of the Team to edit.")]
    public string TeamId { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    [Description("New display name of the Team. Leave empty to keep the current display name.")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("description")]
    [Description("New description of the Team. Leave null to keep the current description.")]
    public string? Description { get; set; }

    [JsonPropertyName("visibility")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    [Description("New visibility of the Team.")]
    public TeamVisibilityType? Visibility { get; set; }
}

[Description("Please confirm the Microsoft Team and Microsoft Entra user to manage.")]
public class GraphTeamUserMembership
{
    [JsonPropertyName("teamId")]
    [Required]
    [Description("ID of the Microsoft Team.")]
    public string TeamId { get; set; } = string.Empty;

    [JsonPropertyName("userId")]
    [Required]
    [Description("Microsoft Entra user ID to add, remove, promote, or demote.")]
    public string UserId { get; set; } = string.Empty;
}

[Description("Please type the exact Microsoft Team ID to confirm permanent deletion: {0}")]
public class GraphDeleteTeam : MCPhappey.Common.Models.IHasName
{
    [JsonPropertyName("name")]
    [Required]
    [Description("The exact Microsoft Team ID to delete.")]
    public string Name { get; set; } = string.Empty;
}
