using System.ComponentModel;
using System.Runtime.Serialization;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MCPhappey.Common.Extensions;
using SharpGLTF.IO;
using System.Text.Json;

namespace MCPhappey.Tools.ModelContext;

public static class ModelContextUtils
{
    public enum ElicitFieldType
    {
        [EnumMember(Value = "String")]
        String,

        [EnumMember(Value = "Email")]
        Email,
        [EnumMember(Value = "Date")]
        Date,
        [EnumMember(Value = "DateTime")]
        DateTime,
        [EnumMember(Value = "Uri")]
        Uri,
        [EnumMember(Value = "Number")]
        Number,
        [EnumMember(Value = "Enum")]
        Enum,
        [EnumMember(Value = "Boolean")]
        Boolean
    }

    [Description("Test MCP logging capabilities by send a custom log message")]
    [McpServerTool(Title = "Send log message",
       ReadOnly = true,
       Idempotent = true,
       OpenWorld = false)]
    public static async Task<CallToolResult?> ModelContextUtils_SendLogMessage(
       RequestContext<CallToolRequestParams> requestContext,
       [Description("Log message")]
        string logMessage,
       [Description("Log level")]
        LoggingLevel loggingLevel,
       CancellationToken cancellationToken = default) =>
       await requestContext.WithExceptionCheck(async () =>
       {
           await requestContext.Server.SendMessageNotificationAsync(logMessage, loggingLevel, cancellationToken);

           return $"Log message sent. \nLevel: {loggingLevel}\n\nMessage:\n{logMessage}".ToTextContentBlock().ToCallToolResult();
       });

    [Description("Test Elicit capabilites by requesting a form with a single field")]
    [McpServerTool(Title = "Elicit single-field form test",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> ModelContextUtils_TestElicit(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Elicit message")]
        string message,
        [Description("Field type")]
        ElicitFieldType fieldType,
        [Description("Field name")]
        string fieldName,
        [Description("Field description")]
        string description,
        [Description("Field required")]
        bool required,
        [Description("Field value")]
        string? defaultValue = null,
        CancellationToken cancellationToken = default) =>
           await requestContext.WithExceptionCheck(async () =>
           //    await requestContext.WithStructuredContent(async () =>
    {
        var propName = string.IsNullOrWhiteSpace(fieldName) ? "value" : fieldName;

        ElicitRequestParams.PrimitiveSchemaDefinition schema = fieldType switch
        {
            ElicitFieldType.String => new ElicitRequestParams.StringSchema
            {
                Title = propName,
                Description = description,
                Default = defaultValue,
            },
            ElicitFieldType.Email => new ElicitRequestParams.StringSchema
            {
                Title = propName,
                Description = description,
                Format = "email"
            },
            ElicitFieldType.Date => new ElicitRequestParams.StringSchema
            {
                Title = propName,
                Description = description,
                Default = defaultValue,
                Format = "date"
            },
            ElicitFieldType.DateTime => new ElicitRequestParams.StringSchema
            {
                Title = propName,
                Description = description,
                Default = defaultValue,
                Format = "date-time"
            },
            ElicitFieldType.Uri => new ElicitRequestParams.StringSchema
            {
                Title = propName,
                Description = description,
                Default = defaultValue,
                Format = "uri"
            },
            ElicitFieldType.Number => new ElicitRequestParams.NumberSchema
            {
                Title = propName,
                Description = description
            },
            ElicitFieldType.Enum => new ElicitRequestParams.TitledSingleSelectEnumSchema
            {
                Title = propName,
                Default = defaultValue,
                Description = description,
                OneOf = [new ElicitRequestParams.EnumSchemaOption() {
                    Title = "Option 1",
                    Const = "Option1",
                }, new ElicitRequestParams.EnumSchemaOption() {
                    Title = "Option 2",
                    Const = "Option2",
                }],
            },
            ElicitFieldType.Boolean => new ElicitRequestParams.BooleanSchema
            {
                Title = propName,
                Description = description
            },
            _ => throw new ArgumentOutOfRangeException(nameof(fieldType), fieldType, null)
        };

        var elicitRequest = new ElicitRequestParams
        {
            RequestedSchema = new ElicitRequestParams.RequestSchema
            {
                Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                {
                    [propName] = schema
                },
                Required = required ? [propName] : []
            },
            Message = message
        };

        var result = await requestContext.Server.ElicitAsync(elicitRequest, cancellationToken: cancellationToken);

        return JsonSerializer.Serialize(result).ToTextCallToolResponse();

    });

}

