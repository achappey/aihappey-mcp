using System.Text.Json;
using System.Text.Json.Nodes;

namespace MCPhappey.Tools.OpenAI.Decisions;

public sealed record OpenAIDecisionsProgress(int Progress, int? Total, string Message);

/// <summary>Preserves shared-evidence calls, but evaluates folder documents in isolation.</summary>
public sealed class OpenAIDecisionsExecutor(
    OpenAIDecisionsInputBuilder inputs,
    Func<string[], CancellationToken, Task<OpenAIDecisionsDocuments>> discover,
    Func<OpenAIDecisionsRequest, CancellationToken, Task<JsonElement>> create,
    Func<OpenAIDecisionsProgress, CancellationToken, Task>? reportProgress = null)
{
    public async Task<JsonElement> ExecuteAsync(
        JsonArray questions, string? inputText, string[]? documentUrls, string[]? imageUrls,
        string imageDetail = "auto", string? safetyIdentifier = null, CancellationToken cancellationToken = default)
    {
        OpenAIDecisionsQuestions.Validate(questions);
        OpenAIDecisionsInputBuilder.ValidateInputs(documentUrls, imageUrls, imageDetail);
        await ReportAsync(0, null, "Discovering document and folder sources.", cancellationToken);
        var sources = await discover(documentUrls ?? [], cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        Task<JsonElement> EvaluateAsync(JsonNode input) => create(new OpenAIDecisionsRequest
        {
            Input = input,
            Questions = questions,
            SafetyIdentifier = safetyIdentifier
        }, cancellationToken);

        if (!sources.HasFolders)
        {
            // Preserve the exact existing input and raw response contract, including URL ordering/duplicates.
            await ReportAsync(1, 2, "Processing shared evidence.", cancellationToken);
            var input = await inputs.BuildAsync(inputText, documentUrls, imageUrls, imageDetail, cancellationToken);
            var response = await EvaluateAsync(input);
            await ReportAsync(2, 2, "Decisions evaluation completed.", cancellationToken);
            return response;
        }

        var total = sources.Documents.Count;
        var totalSteps = checked(2 * total + 2);
        await ReportAsync(1, totalSteps, $"Discovered {total} documents in folder/file inputs; subfolders are excluded.", cancellationToken);
        // Download shared images only once, and clone their content for each independent request.
        JsonNode? sharedInput = total > 0 && (!string.IsNullOrWhiteSpace(inputText) || imageUrls is { Length: > 0 })
            ? await inputs.BuildAsync(inputText, null, imageUrls, imageDetail, cancellationToken) : null;
        var results = new JsonArray();
        var succeeded = 0;
        var failed = 0;
        foreach (var document in sources.Documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ReportAsync(2 * results.Count + 2, totalSteps, $"Processing {document.Name ?? document.Url} ({results.Count + 1}/{total}).", cancellationToken);
            var result = new JsonObject { ["url"] = document.Url, ["name"] = document.Name };
            try
            {
                var documentInput = await inputs.BuildAsync(null, [document.Url], null, imageDetail, cancellationToken);
                var response = await EvaluateAsync(CombineInput(sharedInput, documentInput));
                result["response"] = JsonNode.Parse(response.GetRawText());
                succeeded++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                result["error"] = exception.Message;
                failed++;
            }
            results.Add(result);
            await ReportAsync(2 * results.Count + 1, totalSteps,
                $"Processed {document.Name ?? document.Url}: {succeeded} succeeded, {failed} failed.", cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        await ReportAsync(totalSteps, totalSteps, $"Completed: {succeeded} succeeded, {failed} failed out of {total} documents.", cancellationToken);
        return JsonSerializer.SerializeToElement(new
        {
            mode = "per_document",
            folders = sources.Folders.Select(folder => new { url = folder.Url, name = folder.Name }),
            totalDocuments = total,
            succeeded,
            failed,
            message = total == 0 ? "No direct-child files were found; subfolders are not processed." : "Folder documents were evaluated independently.",
            results
        });
    }

    private Task ReportAsync(int completed, int? total, string message, CancellationToken cancellationToken)
        => reportProgress?.Invoke(new(completed, total, message), cancellationToken) ?? Task.CompletedTask;

    private static JsonNode CombineInput(JsonNode? shared, JsonNode document)
    {
        if (shared is null)
            return document;
        if (shared is JsonValue)
            return JsonValue.Create(shared.GetValue<string>() + "\n\n" + document.GetValue<string>())!;

        var combined = shared.DeepClone();
        combined[0]!["content"]!.AsArray().Add(new JsonObject
        {
            ["type"] = "input_text", ["text"] = document.GetValue<string>()
        });
        return combined;
    }
}
