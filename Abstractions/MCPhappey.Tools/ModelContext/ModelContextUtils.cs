using System.ComponentModel;
using System.Runtime.Serialization;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Diagnostics;

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

    [Description("Test MCP progress notifications by waiting N seconds for X iterations and reporting progress each tick.")]
    [McpServerTool(Title = "Wait with progress notifications (test)",
          ReadOnly = true,
          Idempotent = true,
          OpenWorld = false)]
    public static async Task<CallToolResult?> ModelContextUtils_WaitWithProgress(
          RequestContext<CallToolRequestParams> requestContext,
          [Description("Seconds to wait per iteration (>= 0).")]
        int waitSeconds,
          [Description("Number of iterations (>= 1).")]
        int times,
          [Description("Optional progress message prefix.")]
        string? messagePrefix = null,
          CancellationToken cancellationToken = default)
          => await requestContext.WithExceptionCheck(async () =>
          {
              // basic guardrails (don’t let a typo hang your server forever)
              var originalWaitSeconds = waitSeconds;
              var originalTimes = times;

              waitSeconds = Math.Clamp(waitSeconds, 0, 3600); // max 1h per tick
              times = Math.Clamp(times, 1, 10_000);

              var prefix = string.IsNullOrWhiteSpace(messagePrefix) ? "Waiting" : messagePrefix.Trim();
              var sw = Stopwatch.StartNew();

              // Optional “start” signal (progress=0)
              await requestContext.Server.SendProgressNotificationAsync(
                  requestContext,
                  progressCounter: 0,
                  message: $"{prefix} (starting)",
                  total: times,
                  cancellationToken: cancellationToken);


              try
              {
                  for (var i = 1; i <= times; i++)
                  {
                      await Task.Delay(TimeSpan.FromSeconds(waitSeconds), cancellationToken);

                      var msg = $"{prefix} ({i}/{times}) - elapsed {sw.Elapsed:mm\\:ss}";
                      await requestContext.Server.SendProgressNotificationAsync(
                          requestContext,
                          progressCounter: i,
                          message: msg,
                          total: times,
                          cancellationToken: cancellationToken);


                  }
              }
              catch (OperationCanceledException)
              {
                  var cancelledMsg = $"{prefix}: cancelled after {sw.Elapsed:mm\\:ss}.";
                  return cancelledMsg.ToTextContentBlock().ToCallToolResult();
              }

              var done = $"{prefix}: done. ({times}x {waitSeconds}s) total elapsed {sw.Elapsed:mm\\:ss}.";

              return done.ToTextContentBlock().ToCallToolResult();
          });
    /*
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
             await requestContext.WithStructuredContent(async () =>
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

         return await requestContext.Server.ElicitAsync(elicitRequest, cancellationToken: cancellationToken);
     }));*/

    /*
        [Description("Test Elicit enum capabilities with configurable options")]
        [McpServerTool(
        Title = "Elicit enum test",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false)]
        public static async Task<CallToolResult?> ModelContextUtils_TestElicitEnum(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Message shown above the enum field")]
        string message,
        [Description("Field name")]
        string fieldName,
        [Description("Field description")]
        string description,
        [Description("Enum options (comma separated, e.g. OptionA,OptionB,OptionC)")]
        string options,
        [Description("Is field required")]
        bool required = true,
        [Description("Is field multi select")]
        bool mutliSelect = false,
        [Description("Default value (must match one of the options)")]
        string? defaultValue = null,
        CancellationToken cancellationToken = default)
        =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var propName = string.IsNullOrWhiteSpace(fieldName)
                ? "selection"
                : fieldName;

            var optionList = options
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(o => new ElicitRequestParams.EnumSchemaOption
                {
                    Title = o,
                    Const = o
                })
                .ToList();

            if (optionList.Count == 0)
                throw new ArgumentException("At least one enum option must be provided.", nameof(options));


            var elicitRequest = new ElicitRequestParams
            {
                Message = message,
                RequestedSchema = new ElicitRequestParams.RequestSchema
                {
                    Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                    {
                        [propName] = mutliSelect ? new ElicitRequestParams.TitledMultiSelectEnumSchema
                        {
                            Title = propName,
                            Description = description,
                            Default = string.IsNullOrEmpty(defaultValue)
                                ? [] : [defaultValue],
                            Items = new ElicitRequestParams.TitledEnumItemsSchema()
                            {
                                AnyOf = optionList
                            }
                        } : new ElicitRequestParams.TitledSingleSelectEnumSchema
                        {
                            Title = propName,
                            Description = description,
                            Default = defaultValue,
                            OneOf = optionList
                        }
                    },
                    Required = required ? [propName] : []
                }
            };

            return await requestContext.Server.ElicitAsync(
                elicitRequest,
                cancellationToken: cancellationToken);
        }));
    */

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
        {
            const string inputKey = "elicit";

            if (requestContext.Params?.InputResponses?.TryGetValue(inputKey, out var inputResponse) is true)
            {
                var result = inputResponse.Deserialize(InputResponse.ElicitResultJsonTypeInfo)
                    ?? throw new InvalidOperationException("Invalid elicitation response.");

                return await requestContext.WithStructuredContent(() =>
                    Task.FromResult(result));
            }

            if (!requestContext.Server.IsMrtrSupported)
            {
                return "This tool requires MRTR/input-required support."
                    .ToTextContentBlock()
                    .ToCallToolResult();
            }

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
                    Default = defaultValue,
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
                    Description = description,
                    Default = defaultValue,
                    OneOf =
                    [
                        new ElicitRequestParams.EnumSchemaOption
                    {
                        Title = "Option 1",
                        Const = "Option1",
                    },
                    new ElicitRequestParams.EnumSchemaOption
                    {
                        Title = "Option 2",
                        Const = "Option2",
                    }
                    ],
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
                Mode = "form",
                Message = message,
                RequestedSchema = new ElicitRequestParams.RequestSchema
                {
                    Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                    {
                        [propName] = schema
                    },
                    Required = required ? [propName] : []
                }
            };

            throw new InputRequiredException(
                inputRequests: new Dictionary<string, InputRequest>
                {
                    [inputKey] = InputRequest.ForElicitation(elicitRequest)
                },
                requestState: "modelcontextutils:test-elicit");
        });

    [Description("Test Elicit enum capabilities with configurable options")]
    [McpServerTool(
        Title = "Elicit enum test",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> ModelContextUtils_TestElicitEnum(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Message shown above the enum field")]
    string message,
        [Description("Field name")]
    string fieldName,
        [Description("Field description")]
    string description,
        [Description("Enum options (comma separated, e.g. OptionA,OptionB,OptionC)")]
    string options,
        [Description("Is field required")]
    bool required = true,
        [Description("Is field multi select")]
    bool mutliSelect = false,
        [Description("Default value (must match one of the options)")]
    string? defaultValue = null,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        {
            const string inputKey = "elicit_enum";

            if (requestContext.Params?.InputResponses?.TryGetValue(inputKey, out var inputResponse) is true)
            {
                var result = inputResponse.Deserialize(InputResponse.ElicitResultJsonTypeInfo)
                    ?? throw new InvalidOperationException("Invalid elicitation response.");

                return await requestContext.WithStructuredContent(() =>
                    Task.FromResult(result));
            }

            if (!requestContext.Server.IsMrtrSupported)
            {
                return "This tool requires MRTR/input-required support."
                    .ToTextContentBlock()
                    .ToCallToolResult();
            }

            var propName = string.IsNullOrWhiteSpace(fieldName)
                ? "selection"
                : fieldName;

            var optionList = options
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(o => new ElicitRequestParams.EnumSchemaOption
                {
                    Title = o,
                    Const = o
                })
                .ToList();

            if (optionList.Count == 0)
                throw new ArgumentException("At least one enum option must be provided.", nameof(options));

            ElicitRequestParams.PrimitiveSchemaDefinition schema = mutliSelect
                ? new ElicitRequestParams.TitledMultiSelectEnumSchema
                {
                    Title = propName,
                    Description = description,
                    Default = string.IsNullOrWhiteSpace(defaultValue)
                        ? []
                        : defaultValue
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .ToList(),
                    Items = new ElicitRequestParams.TitledEnumItemsSchema
                    {
                        AnyOf = optionList
                    }
                }
                : new ElicitRequestParams.TitledSingleSelectEnumSchema
                {
                    Title = propName,
                    Description = description,
                    Default = defaultValue,
                    OneOf = optionList
                };

            var elicitRequest = new ElicitRequestParams
            {
                Mode = "form",
                Message = message,
                RequestedSchema = new ElicitRequestParams.RequestSchema
                {
                    Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                    {
                        [propName] = schema
                    },
                    Required = required ? [propName] : []
                }
            };

            throw new InputRequiredException(
                inputRequests: new Dictionary<string, InputRequest>
                {
                    [inputKey] = InputRequest.ForElicitation(elicitRequest)
                },
                requestState: "modelcontextutils:test-elicit-enum");
        });
}

