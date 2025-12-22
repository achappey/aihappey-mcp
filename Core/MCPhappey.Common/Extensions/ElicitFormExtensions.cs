
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Common.Models;
using ModelContextProtocol.Protocol;

namespace MCPhappey.Common.Extensions;

public static class ElicitFormExtensions
{
    public static string GetJsonPropertyName(this PropertyInfo prop) =>
           prop.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? prop.Name;

    public static string? GetDescription(this PropertyInfo prop) =>
        prop.GetCustomAttribute<DescriptionAttribute>()?.Description;

    public static object? GetDefaultValue(this PropertyInfo prop) =>
        prop.GetCustomAttribute<DefaultValueAttribute>()?.Value;

    public static bool IsEmail(this PropertyInfo prop) =>
        prop.GetCustomAttribute<EmailAddressAttribute>() != null;

    public static List<string> GetRequiredProperties(Type type)
        => [.. type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => Attribute.IsDefined(p, typeof(RequiredAttribute)))
            .Select(p =>
            {
                var jsonAttr = p.GetCustomAttribute<JsonPropertyNameAttribute>();
                return jsonAttr?.Name ?? p.Name;
            })];

    public static string GetElicitDescription<T>(string? message = null)
    {
        var type = typeof(T);
        var descAttr = type.GetCustomAttribute<DescriptionAttribute>();
        string description;

        if (!string.IsNullOrEmpty(descAttr?.Description))
        {
            // Only use string.Format if message is not null/empty
            if (!string.IsNullOrEmpty(message))
                description = string.Format(descAttr.Description, $"<b>{message}</b>");
            else
                description = descAttr.Description;
        }
        else
        {
            description = $"Please fill in the details for {type.Name}";
        }

        return description;
    }

    public static ElicitRequestParams CreateElicitRequestParamsForType<T>(T defaultValues, string? message = null)
    {
        var type = typeof(T);
        var description = GetElicitDescription<T>(message);

        var properties = typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(
                prop => prop.GetJsonPropertyName(),
                prop =>
                {
                    object? defVal = defaultValues == null
                        ? null
                        : prop.GetValue(defaultValues);

                    return prop.ToElicitSchemaDef(defVal);
                });

        var required = GetRequiredProperties(typeof(T));

        return new ElicitRequestParams
        {
            Message = description,
           // ElicitationId = Guid.NewGuid().ToString(),
            RequestedSchema = new ElicitRequestParams.RequestSchema
            {
                Properties = properties,
                Required = required
            }
        };
    }

    public static (double? min, double? max) GetRange(this PropertyInfo prop)
    {
        var rangeAttr = prop.GetCustomAttribute<RangeAttribute>();
        if (rangeAttr != null)
        {
            // These can be double, int, etc.
            double? min = rangeAttr.Minimum as double? ?? Convert.ToDouble(rangeAttr.Minimum);
            double? max = rangeAttr.Maximum as double? ?? Convert.ToDouble(rangeAttr.Maximum);
            return (min, max);
        }
        return (null, null);
    }

    public static int? GetMinLength(this PropertyInfo prop) =>
        prop.GetCustomAttribute<MinLengthAttribute>()?.Length;

    public static int? GetMaxLength(this PropertyInfo prop) =>
        prop.GetCustomAttribute<MaxLengthAttribute>()?.Length;

    public static ElicitRequestParams.PrimitiveSchemaDefinition ToElicitSchemaDef(this PropertyInfo prop, dynamic? defaultValue = null)
    {
        var title = prop.Name;
        var desc = prop.GetDescription();
        var type = prop.PropertyType;

        // Enum detection (works for nullable enums too)
        var enumType = Nullable.GetUnderlyingType(type) ?? (type.IsEnum ? type : null);

        if (enumType != null && enumType.IsEnum)
        {
            var enumFields = enumType.GetFields(BindingFlags.Public | BindingFlags.Static);
            var enumOptions = enumFields
                .Select(field => new ElicitRequestParams.EnumSchemaOption
                {
                    Title = field.GetCustomAttribute<DisplayAttribute>()?.Name ?? field.Name,
                    Const = field.GetCustomAttribute<EnumMemberAttribute>()?.Value ?? field.Name,
                })
                .ToArray();

            // Map default enum value -> its EnumMember const string
            string? defaultConst = null;

            if (defaultValue != null)
            {
                // if someone already passed a string, keep it
                if (defaultValue is string s)
                {
                    defaultConst = s;
                }
                else
                {
                    var enumName = Enum.GetName(enumType, defaultValue);
                    if (enumName != null)
                    {
                        var field = enumType.GetField(enumName);
                        if (field != null)
                        {
                            var enumMemberAttr = (EnumMemberAttribute?)Attribute.GetCustomAttribute(field, typeof(EnumMemberAttribute));
                            defaultConst = enumMemberAttr?.Value ?? enumName;
                        }
                    }
                }
            }

            // Only set enumNames if they differ from enum values
            return new ElicitRequestParams.TitledSingleSelectEnumSchema
            {
                Title = title,
                Description = desc,
                Default = defaultConst,
                OneOf = enumOptions,
            };
        }

        // Use switch for type logic (add more cases as needed)
        return type switch
        {
            Type t when t == typeof(string) => new ElicitRequestParams.StringSchema
            {
                Title = title,
                Format = prop.IsEmail() ? "email" : null,
                Description = desc,
                Default = defaultValue,
                MinLength = prop.GetMinLength(),
                MaxLength = prop.GetMaxLength()
            },
            Type t when t == typeof(int) || t == typeof(int?) ||
                        t == typeof(decimal) || t == typeof(decimal?) ||
                        t == typeof(double) || t == typeof(double?) =>
            new ElicitRequestParams.NumberSchema
            {
                Title = title,
                Description = desc,
                Default = defaultValue,
                Minimum = prop.GetRange().min,
                Maximum = prop.GetRange().max
            },
            Type t when t == typeof(Uri) => new ElicitRequestParams.StringSchema
            {
                Title = title,
                Format = "uri",
                Default = defaultValue?.ToString(),
                Description = desc
            },
            Type t when t == typeof(DateTime) || t == typeof(DateTime?) => new ElicitRequestParams.StringSchema
            {
                Title = title,
                Format = "date-time",
                Default = defaultValue,
                Description = desc
            },
            Type t when t == typeof(bool) || t == typeof(bool?) => new ElicitRequestParams.BooleanSchema
            {
                Title = title,
                Description = desc,
                Default = defaultValue,
                //   Default = prop.GetDefaultValue() as bool?
            },
            // Add more cases if needed (enums, arrays, etc.)
            _ => new ElicitRequestParams.StringSchema
            {
                Title = title,
                Default = defaultValue,
                Description = desc
            }
        };
    }

    public static T MapToObject<T>(this IDictionary<string, JsonElement> dict) where T : new()
    {
        var obj = new T();
        var type = typeof(T);

        foreach (var prop in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            // Pak JSON property name indien aanwezig
            var jsonName = prop.Name;
            var jsonAttr = prop.GetCustomAttribute<JsonPropertyNameAttribute>();
            if (jsonAttr != null)
                jsonName = jsonAttr.Name;

            if (dict.TryGetValue(jsonName, out var el))
            {
                object? value = null;
                var t = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

                try
                {
                    if (t.IsEnum)
                    {
                        if (el.ValueKind == JsonValueKind.String && Enum.TryParse(t, el.GetString(), true, out var enumVal))
                            value = enumVal;
                    }
                    else if (t == typeof(string))
                        value = el.ValueKind == JsonValueKind.String ? el.GetString() : null;
                    else if (t == typeof(int))
                        value = el.ValueKind == JsonValueKind.Number ? el.GetInt32() : default;
                    else if (t == typeof(long))
                        value = el.ValueKind == JsonValueKind.Number ? el.GetInt64() : default;
                    else if (t == typeof(bool))
                        value = el.ValueKind == JsonValueKind.True ? true :
                                el.ValueKind == JsonValueKind.False ? false : (bool?)null;
                    else if (t == typeof(double))
                        value = el.ValueKind == JsonValueKind.Number ? el.GetDouble() : default;
                    else if (t == typeof(decimal))
                        value = el.ValueKind == JsonValueKind.Number ? el.GetDecimal() : default;
                    else if (t == typeof(DateTime))
                        value = el.ValueKind == JsonValueKind.String ? el.GetDateTime() : default;
                    else
                    {
                        // fallback voor complexe types (nested objects/arrays)
                        value = el.Deserialize(t);
                    }
                }
                catch
                {
                    // bij conversiefout, laat property op default
                }

                if (value != null || Nullable.GetUnderlyingType(prop.PropertyType) != null)
                    prop.SetValue(obj, value);
            }
        }
        return obj;
    }

    public static async Task<CallToolResult> ConfirmAndDeleteAsync<TConfirm>(
        this ModelContextProtocol.Server.RequestContext<CallToolRequestParams> ctx,
        string expectedName,
        Func<CancellationToken, Task> deleteAction,
        string successText,
        CancellationToken ct = default)
         where TConfirm : class, IHasName, new()
    {
        var dto = await ctx.Server.GetElicitResponse<TConfirm>(expectedName, ct);

        if (dto?.Action != "accept")
            return JsonSerializer.Serialize(
                            dto,
                            JsonSerializerOptions.Web)
                            .ToErrorCallToolResponse();

        // Parsed DTO
        var typed = dto?.GetTypedResult<TConfirm>() ?? throw new Exception();

        // Name must match exactly (case-insensitive is usually more user-friendly)
        if (!string.Equals(typed.Name?.Trim(), expectedName.Trim(), StringComparison.OrdinalIgnoreCase))
            return $"Confirmation does not match name '{expectedName}'".ToErrorCallToolResponse();

        // All good – run the provided delete delegate and send success
        await deleteAction(ct);
        return successText.ToTextCallToolResponse();
    }

    public static T? GetTypedResult<T>(this ElicitResult elicitResult) where T : new()
        => elicitResult.Content != null ? elicitResult.Content.MapToObject<T>() : default;
}
