using System.Net.Http.Json;
using System.Text.Json;
using Codions.Contracts.Models;

namespace Codions.ChatAdapter;

// JSON options matching typical ASP.NET Core camelCase API responses

public interface ICodionsApiClient
{
    Task<(bool Success, string? JobId, string? Error)> CreateJobAsync(JobRequest request, CancellationToken ct = default);
}

public sealed class CodionsApiClient : ICodionsApiClient
{
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public CodionsApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<(bool Success, string? JobId, string? Error)> CreateJobAsync(JobRequest request, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/jobs", request, JsonOptions, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.Created)
            {
                var body = await response.Content.ReadFromJsonAsync<CreateJobResponseDto>(JsonOptions, ct);
                var jobId = body?.JobId ?? TryGetJobIdFromLocation(response.Headers.Location);
                return (true, jobId, null);
            }

            var errorBody = await response.Content.ReadAsStringAsync(ct);
            var shortError = response.StatusCode switch
            {
                System.Net.HttpStatusCode.BadRequest => "Invalid request.",
                System.Net.HttpStatusCode.NotFound => "Resource not found.",
                System.Net.HttpStatusCode.InternalServerError => "Codions API error.",
                _ => $"HTTP {(int)response.StatusCode}: {errorBody}"
            };
            return (false, null, shortError);
        }
        catch (HttpRequestException ex)
        {
            return (false, null, $"Codions API unreachable: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return (false, null, "Request timed out.");
        }
    }

    private static string? TryGetJobIdFromLocation(Uri? location)
    {
        if (location is null) return null;
        var segments = location.AbsolutePath.TrimEnd('/').Split('/');
        return segments.Length > 0 ? segments[^1] : null;
    }
}

/// <summary>
/// DTO for 201 response body (jobId only; status enum not needed here).
/// </summary>
internal sealed class CreateJobResponseDto
{
    public string? JobId { get; set; }
}
