using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.Teams;

public static partial class GraphTeams
{
    [Description(
     "Set the photo of an existing Microsoft Team from an accessible SharePoint or OneDrive image URL. " +
     "The source image is downloaded through Microsoft Graph, converted to JPEG, and applied as the Team photo.")]
    [McpServerTool(
     Title = "Set Microsoft Team photo from URL",
     Name = "graph_teams_set_photo_from_url",
     Destructive = true,
     OpenWorld = false)]
    public static async Task<CallToolResult?> GraphTeams_SetPhotoFromUrl(
     RequestContext<CallToolRequestParams> requestContext,

     [Description(
        "ID of the Microsoft Team whose photo must be replaced.")]
    string? teamId = null,

     [Description(
        "Absolute SharePoint or OneDrive URL of the source jpg image, e.g. " +
        "https://contoso.sharepoint.com/sites/branding/Shared%20Documents/logo.jpg.")]
    string? logoUrl = null,

     CancellationToken cancellationToken = default) =>
         await ModelContextToolExtensions.WithExceptionCheck(async () =>
         await requestContext.WithOboGraphClient(async client =>
         await requestContext.WithStructuredContent(async () =>
         {
             var (typed, notAccepted, _) =
                 await requestContext.Server.TryElicit(
                     new GraphSetTeamPhotoFromUrl
                     {
                         TeamId = teamId,
                         LogoUrl = logoUrl
                     },
                     cancellationToken);

             if (notAccepted is not null)
             {
                 throw new Exception(
                     JsonSerializer.Serialize(notAccepted));
             }

             typed!.Validate();

             var encodedSharingUrl =
                 EncodeGraphSharingUrl(typed.LogoUrl!);

             await using var sourceStream =
                 await client
                     .Shares[encodedSharingUrl]
                     .DriveItem
                     .Content
                     .GetAsync(
                         cancellationToken: cancellationToken);

             if (sourceStream is null)
             {
                 throw new InvalidOperationException(
                     "Microsoft Graph returned no content for the source logo URL.");
             }
        
             var jpegBytes = sourceStream.ToArray();

             const int maximumPhotoSize = 4 * 1024 * 1024;

             if (jpegBytes.Length > maximumPhotoSize)
             {
                 throw new ValidationException(
                     $"The converted Team photo is {jpegBytes.Length:N0} bytes. " +
                     "Microsoft Graph allows a maximum Team photo size of 4 MB.");
             }

             await using var uploadStream =
                 new MemoryStream(
                     jpegBytes,
                     writable: false);

             await client
                 .Teams[typed.TeamId!]
                 .Photo
                 .Content
                 .PutAsync(
                     uploadStream,
                     cancellationToken: cancellationToken);

             return new
             {
                 typed.TeamId,
                 SourceLogoUrl = typed.LogoUrl,
                 EncodedSharingUrl = encodedSharingUrl,
                 SourceFormat = "Automatically detected",
                 UploadedFormat = "image/jpeg",
                 BytesUploaded = jpegBytes.Length,
                 Status = "Microsoft Team photo updated successfully."
             };
         })));

    private static string EncodeGraphSharingUrl(
string sharingUrl)
    {
        var bytes = Encoding.UTF8.GetBytes(sharingUrl);

        var base64 = Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('/', '_')
            .Replace('+', '-');

        return $"u!{base64}";
    }

    [Description(
    "Confirm replacing a Microsoft Team photo with an image from a SharePoint or OneDrive URL.")]
    public class GraphSetTeamPhotoFromUrl
    {
        [JsonPropertyName("teamId")]
        [Required]
        public string? TeamId { get; set; }

        [JsonPropertyName("logoUrl")]
        [Required]
        public string? LogoUrl { get; set; }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(TeamId) ||
                !Guid.TryParse(TeamId, out _))
            {
                throw new ValidationException(
                    "teamId must be a valid Microsoft Team ID.");
            }

            if (!Uri.TryCreate(
                    LogoUrl,
                    UriKind.Absolute,
                    out var logoUri) ||
                logoUri.Scheme != Uri.UriSchemeHttps)
            {
                throw new ValidationException(
                    "logoUrl must be an absolute HTTPS URL.");
            }

            if (!logoUri.Host.EndsWith(
                    ".sharepoint.com",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException(
                    "logoUrl must reference a SharePoint Online or OneDrive for Business URL.");
            }
        }
    }
}