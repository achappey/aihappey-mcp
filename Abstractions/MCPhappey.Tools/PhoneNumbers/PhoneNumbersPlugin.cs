using PhoneNumbers;
using System.ComponentModel;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.PhoneNumbers;

public static class PhoneNumberPlugin
{
    private static readonly PhoneNumberUtil PhoneUtil = PhoneNumberUtil.GetInstance();

    [McpServerTool(ReadOnly = true)]
    public static object PhoneNumbers_ParsePhoneNumber(
        [Description("Phone number string to parse, e.g. '+14156667777'")] string phoneNumber,
        [Description("Region code, e.g. 'US' (can be null)")] string? regionCode = null)
    {
        var parsed = PhoneUtil.Parse(phoneNumber, regionCode);
        return new
        {
            parsed.CountryCode,
            parsed.NationalNumber,
            Extension = parsed.HasExtension ? parsed.Extension : null,
            RegionCode = PhoneUtil.GetRegionCodeForNumber(parsed)
        };
    }

    [McpServerTool(ReadOnly = true)]
    public static object PhoneNumbers_FormatPhoneNumber(
        [Description("Phone number string to parse, e.g. '+14156667777'")] string phoneNumber,
        [Description("Region code, e.g. 'US'")] string regionCode)
    {
        var parsed = PhoneUtil.Parse(phoneNumber, regionCode);
        return new
        {
            E164 = PhoneUtil.Format(parsed, PhoneNumberFormat.E164),
            International = PhoneUtil.Format(parsed, PhoneNumberFormat.INTERNATIONAL),
            National = PhoneUtil.Format(parsed, PhoneNumberFormat.NATIONAL),
            RFC3966 = PhoneUtil.Format(parsed, PhoneNumberFormat.RFC3966)
        };
    }

    [McpServerTool(ReadOnly = true)]
    public static object PhoneNumbers_IsValidPhoneNumber(
        [Description("Phone number string to check")] string phoneNumber,
        [Description("Region code, e.g. 'US'")] string regionCode)
    {
        var parsed = PhoneUtil.Parse(phoneNumber, regionCode);
        return new
        {
            IsValid = PhoneUtil.IsValidNumber(parsed),
            Possible = PhoneUtil.IsPossibleNumber(parsed)
        };
    }

    [McpServerTool(ReadOnly = true)]
    public static string PhoneNumbers_GetPhoneNumberType(
        [Description("Phone number string to check")] string phoneNumber,
        [Description("Region code, e.g. 'US'")] string regionCode)
    {
        var parsed = PhoneUtil.Parse(phoneNumber, regionCode);
        return PhoneUtil.GetNumberType(parsed).ToString();
    }

    [McpServerTool(ReadOnly = true)]
    public static string PhoneNumbers_GetPhoneRegion(
        [Description("Phone number string to check")] string phoneNumber,
        [Description("Optional region code for parsing")] string? regionCode = null)
    {
        var parsed = PhoneUtil.Parse(phoneNumber, regionCode);
        return PhoneUtil.GetRegionCodeForNumber(parsed);
    }
}
