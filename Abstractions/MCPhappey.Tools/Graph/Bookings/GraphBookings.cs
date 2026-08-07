using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.Bookings;

public static class GraphBookings
{
    [Description("Create an appointment in a Microsoft Bookings business.")]
    [McpServerTool(Title = "Create Bookings appointment", Destructive = true,
        OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(BookingAppointment))]
    public static async Task<CallToolResult?> GraphBookings_CreateAppointment(
        [Description("Booking business ID or SMTP address.")] string businessId,
        [Description("Booking service ID.")] string serviceId,
        [Description("Appointment start date and time.")] DateTimeOffset start,
        [Description("Appointment end date and time.")] DateTimeOffset end,
        [Description("IANA or Windows time-zone name used for start and end.")] string timeZone,
        [Description("Customer display name.")] string customerName,
        [Description("Customer email address.")] string customerEmailAddress,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Customer phone number.")] string? customerPhone = null,
        [Description("Customer ID, when the customer already exists.")] string? customerId = null,
        [Description("Comma-separated Bookings staff member IDs.")] string? staffMemberIdsCsv = null,
        [Description("Service notes for this appointment.")] string? serviceNotes = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (input, notAccepted, _) = await requestContext.Server.TryElicit(
                new AppointmentInput
                {
                    ServiceId = serviceId, Start = start, End = end, TimeZone = timeZone,
                    CustomerName = customerName, CustomerEmailAddress = customerEmailAddress,
                    CustomerPhone = customerPhone, CustomerId = customerId,
                    StaffMemberIdsCsv = staffMemberIdsCsv, ServiceNotes = serviceNotes
                }, cancellationToken);
            if (notAccepted is not null || input is null) return default(BookingAppointment);

            return await client.Solutions.BookingBusinesses[businessId].Appointments.PostAsync(
                ToAppointment(input), cancellationToken: cancellationToken);
        })));

    [Description("Update an appointment in a Microsoft Bookings business. Only supplied values are changed.")]
    [McpServerTool(Title = "Update Bookings appointment", Destructive = true,
        OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(BookingAppointment))]
    public static async Task<CallToolResult?> GraphBookings_UpdateAppointment(
        [Description("Booking business ID or SMTP address.")] string businessId,
        [Description("Booking appointment ID.")] string appointmentId,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Booking service ID.")] string? serviceId = null,
        [Description("Appointment start date and time.")] DateTimeOffset? start = null,
        [Description("Appointment end date and time.")] DateTimeOffset? end = null,
        [Description("IANA or Windows time-zone name used for start and end.")] string? timeZone = null,
        [Description("Customer display name.")] string? customerName = null,
        [Description("Customer email address.")] string? customerEmailAddress = null,
        [Description("Customer phone number.")] string? customerPhone = null,
        [Description("Customer ID.")] string? customerId = null,
        [Description("Comma-separated Bookings staff member IDs.")] string? staffMemberIdsCsv = null,
        [Description("Service notes for this appointment.")] string? serviceNotes = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (input, notAccepted, _) = await requestContext.Server.TryElicit(
                new AppointmentPatchInput
                {
                    ServiceId = serviceId, Start = start, End = end, TimeZone = timeZone,
                    CustomerName = customerName, CustomerEmailAddress = customerEmailAddress,
                    CustomerPhone = customerPhone, CustomerId = customerId,
                    StaffMemberIdsCsv = staffMemberIdsCsv, ServiceNotes = serviceNotes
                }, cancellationToken);
            if (notAccepted is not null || input is null) return default(BookingAppointment);

            return await client.Solutions.BookingBusinesses[businessId].Appointments[appointmentId]
                .PatchAsync(ToAppointmentPatch(input), cancellationToken: cancellationToken);
        })));

    [Description("Delete an appointment from a Microsoft Bookings business.")]
    [McpServerTool(Title = "Delete Bookings appointment", Destructive = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphBookings_DeleteAppointment(
        [Description("Booking business ID or SMTP address.")] string businessId,
        [Description("Booking appointment ID.")] string appointmentId,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.ConfirmAndDeleteAsync<DeleteAppointment>(appointmentId,
            async ct => await client.Solutions.BookingBusinesses[businessId].Appointments[appointmentId]
                .DeleteAsync(cancellationToken: ct),
            "Bookings appointment deleted.", cancellationToken));

    [Description("Create a service in a Microsoft Bookings business.")]
    [McpServerTool(Title = "Create Bookings service", Destructive = true,
        OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(BookingService))]
    public static async Task<CallToolResult?> GraphBookings_CreateService(
        [Description("Booking business ID or SMTP address.")] string businessId,
        [Description("Service display name.")] string displayName,
        [Description("Default service duration in minutes.")][Range(1, 10080)] int defaultDurationMinutes,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Service description.")] string? description = null,
        [Description("Internal service notes.")] string? notes = null,
        [Description("Whether customers should not see the service.")] bool isHiddenFromCustomers = false,
        [Description("Whether this is an online service.")] bool isLocationOnline = false,
        [Description("Comma-separated Bookings staff member IDs.")] string? staffMemberIdsCsv = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (input, notAccepted, _) = await requestContext.Server.TryElicit(new ServiceInput
            {
                DisplayName = displayName, DefaultDurationMinutes = defaultDurationMinutes,
                Description = description, Notes = notes, IsHiddenFromCustomers = isHiddenFromCustomers,
                IsLocationOnline = isLocationOnline, StaffMemberIdsCsv = staffMemberIdsCsv
            }, cancellationToken);
            if (notAccepted is not null || input is null) return default(BookingService);

            return await client.Solutions.BookingBusinesses[businessId].Services.PostAsync(
                ToService(input), cancellationToken: cancellationToken);
        })));

    [Description("Update a service in a Microsoft Bookings business. Only supplied values are changed.")]
    [McpServerTool(Title = "Update Bookings service", Destructive = true,
        OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(BookingService))]
    public static async Task<CallToolResult?> GraphBookings_UpdateService(
        [Description("Booking business ID or SMTP address.")] string businessId,
        [Description("Booking service ID.")] string serviceId,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Service display name.")] string? displayName = null,
        [Description("Default service duration in minutes.")][Range(1, 10080)] int? defaultDurationMinutes = null,
        [Description("Service description.")] string? description = null,
        [Description("Internal service notes.")] string? notes = null,
        [Description("Whether customers should not see the service.")] bool? isHiddenFromCustomers = null,
        [Description("Whether this is an online service.")] bool? isLocationOnline = null,
        [Description("Comma-separated Bookings staff member IDs.")] string? staffMemberIdsCsv = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (input, notAccepted, _) = await requestContext.Server.TryElicit(new ServicePatchInput
            {
                DisplayName = displayName, DefaultDurationMinutes = defaultDurationMinutes,
                Description = description, Notes = notes, IsHiddenFromCustomers = isHiddenFromCustomers,
                IsLocationOnline = isLocationOnline, StaffMemberIdsCsv = staffMemberIdsCsv
            }, cancellationToken);
            if (notAccepted is not null || input is null) return default(BookingService);

            return await client.Solutions.BookingBusinesses[businessId].Services[serviceId]
                .PatchAsync(ToServicePatch(input), cancellationToken: cancellationToken);
        })));

    [Description("Delete a service from a Microsoft Bookings business.")]
    [McpServerTool(Title = "Delete Bookings service", Destructive = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphBookings_DeleteService(
        [Description("Booking business ID or SMTP address.")] string businessId,
        [Description("Booking service ID.")] string serviceId,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.ConfirmAndDeleteAsync<DeleteService>(serviceId,
            async ct => await client.Solutions.BookingBusinesses[businessId].Services[serviceId]
                .DeleteAsync(cancellationToken: ct),
            "Bookings service deleted.", cancellationToken));

    [Description("Create a customer in a Microsoft Bookings business.")]
    [McpServerTool(Title = "Create Bookings customer", Destructive = true,
        OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(BookingCustomer))]
    public static async Task<CallToolResult?> GraphBookings_CreateCustomer(
        [Description("Booking business ID or SMTP address.")] string businessId,
        [Description("Customer display name.")] string displayName,
        [Description("Customer email address.")] string emailAddress,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (input, notAccepted, _) = await requestContext.Server.TryElicit(
                new CustomerInput { DisplayName = displayName, EmailAddress = emailAddress }, cancellationToken);
            if (notAccepted is not null || input is null) return default(BookingCustomer);

            return await client.Solutions.BookingBusinesses[businessId].Customers.PostAsync(
                new BookingCustomer { DisplayName = input.DisplayName, EmailAddress = input.EmailAddress },
                cancellationToken: cancellationToken);
        })));

    [Description("Update a customer in a Microsoft Bookings business. Only supplied values are changed.")]
    [McpServerTool(Title = "Update Bookings customer", Destructive = true,
        OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(BookingCustomer))]
    public static async Task<CallToolResult?> GraphBookings_UpdateCustomer(
        [Description("Booking business ID or SMTP address.")] string businessId,
        [Description("Booking customer ID.")] string customerId,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Customer display name.")] string? displayName = null,
        [Description("Customer email address.")] string? emailAddress = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (input, notAccepted, _) = await requestContext.Server.TryElicit(
                new CustomerPatchInput { DisplayName = displayName, EmailAddress = emailAddress }, cancellationToken);
            if (notAccepted is not null || input is null) return default(BookingCustomer);

            return await client.Solutions.BookingBusinesses[businessId].Customers[customerId].PatchAsync(
                new BookingCustomer { DisplayName = input.DisplayName, EmailAddress = input.EmailAddress },
                cancellationToken: cancellationToken);
        })));

    [Description("Delete a customer from a Microsoft Bookings business.")]
    [McpServerTool(Title = "Delete Bookings customer", Destructive = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphBookings_DeleteCustomer(
        [Description("Booking business ID or SMTP address.")] string businessId,
        [Description("Booking customer ID.")] string customerId,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.ConfirmAndDeleteAsync<DeleteCustomer>(customerId,
            async ct => await client.Solutions.BookingBusinesses[businessId].Customers[customerId]
                .DeleteAsync(cancellationToken: ct),
            "Bookings customer deleted.", cancellationToken));

    private static BookingAppointment ToAppointment(AppointmentInput input) => new()
    {
        ServiceId = input.ServiceId,
        Start = ToDateTimeTimeZone(input.Start, input.TimeZone),
        End = ToDateTimeTimeZone(input.End, input.TimeZone),
        StaffMemberIds = SplitCsv(input.StaffMemberIdsCsv),
        ServiceNotes = input.ServiceNotes,
        Customers = [new BookingCustomerInformation
        {
            CustomerId = input.CustomerId, Name = input.CustomerName,
            EmailAddress = input.CustomerEmailAddress, Phone = input.CustomerPhone,
            TimeZone = input.TimeZone
        }]
    };

    private static BookingAppointment ToAppointmentPatch(AppointmentPatchInput input)
    {
        var result = new BookingAppointment
        {
            ServiceId = input.ServiceId, ServiceNotes = input.ServiceNotes,
            StaffMemberIds = input.StaffMemberIdsCsv is null ? null : SplitCsv(input.StaffMemberIdsCsv)
        };
        if (input.Start.HasValue) result.Start = ToDateTimeTimeZone(input.Start.Value, input.TimeZone ?? "UTC");
        if (input.End.HasValue) result.End = ToDateTimeTimeZone(input.End.Value, input.TimeZone ?? "UTC");
        if (input.CustomerName is not null || input.CustomerEmailAddress is not null || input.CustomerPhone is not null || input.CustomerId is not null)
            result.Customers = [new BookingCustomerInformation
            {
                CustomerId = input.CustomerId, Name = input.CustomerName,
                EmailAddress = input.CustomerEmailAddress, Phone = input.CustomerPhone,
                TimeZone = input.TimeZone
            }];
        return result;
    }

    private static BookingService ToService(ServiceInput input) => new()
    {
        DisplayName = input.DisplayName, DefaultDuration = TimeSpan.FromMinutes(input.DefaultDurationMinutes),
        Description = input.Description, Notes = input.Notes,
        IsHiddenFromCustomers = input.IsHiddenFromCustomers, IsLocationOnline = input.IsLocationOnline,
        StaffMemberIds = SplitCsv(input.StaffMemberIdsCsv)
    };

    private static BookingService ToServicePatch(ServicePatchInput input) => new()
    {
        DisplayName = input.DisplayName,
        DefaultDuration = input.DefaultDurationMinutes.HasValue ? TimeSpan.FromMinutes(input.DefaultDurationMinutes.Value) : null,
        Description = input.Description, Notes = input.Notes,
        IsHiddenFromCustomers = input.IsHiddenFromCustomers, IsLocationOnline = input.IsLocationOnline,
        StaffMemberIds = input.StaffMemberIdsCsv is null ? null : SplitCsv(input.StaffMemberIdsCsv)
    };

    private static DateTimeTimeZone ToDateTimeTimeZone(DateTimeOffset value, string timeZone) => new()
    {
        DateTime = value.ToString("yyyy-MM-ddTHH:mm:ss.fffffff"), TimeZone = timeZone
    };

    private static List<string> SplitCsv(string? value) => string.IsNullOrWhiteSpace(value)
        ? [] : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    [Description("Please confirm the Bookings appointment ID to delete: {0}")]
    public sealed class DeleteAppointment : MCPhappey.Common.Models.IHasName
    {
        [JsonPropertyName("name"), Required] public string Name { get; set; } = default!;
    }
    [Description("Please confirm the Bookings service ID to delete: {0}")]
    public sealed class DeleteService : MCPhappey.Common.Models.IHasName
    {
        [JsonPropertyName("name"), Required] public string Name { get; set; } = default!;
    }
    [Description("Please confirm the Bookings customer ID to delete: {0}")]
    public sealed class DeleteCustomer : MCPhappey.Common.Models.IHasName
    {
        [JsonPropertyName("name"), Required] public string Name { get; set; } = default!;
    }

    [Description("Please review the Bookings appointment fields.")]
    public sealed class AppointmentInput
    {
        [JsonPropertyName("serviceId"), Required] public string ServiceId { get; set; } = default!;
        [JsonPropertyName("start"), Required] public DateTimeOffset Start { get; set; }
        [JsonPropertyName("end"), Required] public DateTimeOffset End { get; set; }
        [JsonPropertyName("timeZone"), Required] public string TimeZone { get; set; } = default!;
        [JsonPropertyName("customerName"), Required] public string CustomerName { get; set; } = default!;
        [JsonPropertyName("customerEmailAddress"), Required] public string CustomerEmailAddress { get; set; } = default!;
        [JsonPropertyName("customerPhone")] public string? CustomerPhone { get; set; }
        [JsonPropertyName("customerId")] public string? CustomerId { get; set; }
        [JsonPropertyName("staffMemberIdsCsv")] public string? StaffMemberIdsCsv { get; set; }
        [JsonPropertyName("serviceNotes")] public string? ServiceNotes { get; set; }
    }
    [Description("Please review the Bookings appointment changes.")]
    public sealed class AppointmentPatchInput
    {
        [JsonPropertyName("serviceId")] public string? ServiceId { get; set; }
        [JsonPropertyName("start")] public DateTimeOffset? Start { get; set; }
        [JsonPropertyName("end")] public DateTimeOffset? End { get; set; }
        [JsonPropertyName("timeZone")] public string? TimeZone { get; set; }
        [JsonPropertyName("customerName")] public string? CustomerName { get; set; }
        [JsonPropertyName("customerEmailAddress")] public string? CustomerEmailAddress { get; set; }
        [JsonPropertyName("customerPhone")] public string? CustomerPhone { get; set; }
        [JsonPropertyName("customerId")] public string? CustomerId { get; set; }
        [JsonPropertyName("staffMemberIdsCsv")] public string? StaffMemberIdsCsv { get; set; }
        [JsonPropertyName("serviceNotes")] public string? ServiceNotes { get; set; }
    }
    [Description("Please review the Bookings service fields.")]
    public sealed class ServiceInput
    {
        [JsonPropertyName("displayName"), Required] public string DisplayName { get; set; } = default!;
        [JsonPropertyName("defaultDurationMinutes"), Range(1, 10080)] public int DefaultDurationMinutes { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("notes")] public string? Notes { get; set; }
        [JsonPropertyName("isHiddenFromCustomers")] public bool IsHiddenFromCustomers { get; set; }
        [JsonPropertyName("isLocationOnline")] public bool IsLocationOnline { get; set; }
        [JsonPropertyName("staffMemberIdsCsv")] public string? StaffMemberIdsCsv { get; set; }
    }
    [Description("Please review the Bookings service changes.")]
    public sealed class ServicePatchInput
    {
        [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
        [JsonPropertyName("defaultDurationMinutes"), Range(1, 10080)] public int? DefaultDurationMinutes { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("notes")] public string? Notes { get; set; }
        [JsonPropertyName("isHiddenFromCustomers")] public bool? IsHiddenFromCustomers { get; set; }
        [JsonPropertyName("isLocationOnline")] public bool? IsLocationOnline { get; set; }
        [JsonPropertyName("staffMemberIdsCsv")] public string? StaffMemberIdsCsv { get; set; }
    }
    [Description("Please review the Bookings customer fields.")]
    public sealed class CustomerInput
    {
        [JsonPropertyName("displayName"), Required] public string DisplayName { get; set; } = default!;
        [JsonPropertyName("emailAddress"), Required, EmailAddress] public string EmailAddress { get; set; } = default!;
    }
    [Description("Please review the Bookings customer changes.")]
    public sealed class CustomerPatchInput
    {
        [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
        [JsonPropertyName("emailAddress"), EmailAddress] public string? EmailAddress { get; set; }
    }
}
