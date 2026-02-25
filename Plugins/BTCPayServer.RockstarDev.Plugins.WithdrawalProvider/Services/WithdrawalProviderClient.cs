using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace BTCPayServer.RockstarDev.Plugins.WithdrawalProvider.Services;

public class WithdrawalProviderClient
{
    public static readonly Uri DefaultApiUri = new("https://xxx.com/");
    public const string HttpClientName = "withdrawal-provider";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly HttpClient _httpClient;

    public WithdrawalProviderClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public static WithdrawalProviderClient Create(IHttpClientFactory httpClientFactory, string apiKey)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        client.DefaultRequestHeaders.Remove("api-key");
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("api-key", apiKey);
        }

        return new WithdrawalProviderClient(client);
    }

    public async Task<string> GetUserId(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync("/api/v1/user/user-id", cancellationToken);
        var payload = await ReadAsAsync<UserIdResponse>(response, cancellationToken);
        return payload.UserId;
    }

    public Task<RateResponse> GetRate(string ticker, CancellationToken cancellationToken = default)
    {
        return PostAsAsync<RateRequest, RateResponse>("/api/v1/offramp/rates", new RateRequest(ticker), cancellationToken);
    }

    public Task<CreateOrderResponse> CreateOrder(CreateOrderRequest request, CancellationToken cancellationToken = default)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/offramp/order")
        {
            Content = CreateJsonContent(request)
        };
        message.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return SendAsync<CreateOrderResponse>(message, cancellationToken);
    }

    public Task<BalanceResponse> GetFiatBalance(string currency, CancellationToken cancellationToken = default)
    {
        return PostAsAsync<BalanceRequest, BalanceResponse>("/api/v1/user/get-balance/fiat", new BalanceRequest(currency), cancellationToken);
    }

    public Task<GetTransactionsResponse> GetTransactions(long startDate, long endDate, CancellationToken cancellationToken = default)
    {
        return PostAsAsync<GetTransactionsRequest, GetTransactionsResponse>(
            "/api/v1/account/transactions",
            new GetTransactionsRequest(startDate, endDate),
            cancellationToken);
    }

    public async Task<Uri?> GetSignupUrl(Uri callback, CancellationToken cancellationToken = default)
    {
        var response = await PostAsAsync<SignupUrlRequest, SignupUrlResponse>(
            "/api/v1/application/btcpay/signup-url",
            new SignupUrlRequest(callback.ToString()),
            cancellationToken);

        return Uri.TryCreate(response.SignupUrl, UriKind.Absolute, out var uri) ? uri : null;
    }

    private async Task<TResponse> PostAsAsync<TRequest, TResponse>(string url, TRequest request, CancellationToken cancellationToken)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = CreateJsonContent(request)
        };
        return await SendAsync<TResponse>(message, cancellationToken);
    }

    private async Task<TResponse> SendAsync<TResponse>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadAsAsync<TResponse>(response, cancellationToken);
    }

    private static async Task<TResponse> ReadAsAsync<TResponse>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            ProviderErrorResponse? error = null;
            try
            {
                error = JsonSerializer.Deserialize<ProviderErrorResponse>(responseBody, JsonOptions);
            }
            catch
            {
                // ignored: we still throw with raw payload below
            }

            throw new WithdrawalProviderApiException(
                response.StatusCode,
                error,
                string.IsNullOrWhiteSpace(responseBody)
                    ? $"Provider API request failed with status {(int)response.StatusCode}."
                    : responseBody);
        }

        var payload = JsonSerializer.Deserialize<TResponse>(responseBody, JsonOptions);
        if (payload is null)
        {
            throw new WithdrawalProviderApiException(
                HttpStatusCode.InternalServerError,
                null,
                "Provider API response could not be parsed.");
        }

        return payload;
    }

    private static StringContent CreateJsonContent<TRequest>(TRequest request)
    {
        var json = JsonSerializer.Serialize(request, JsonOptions);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    public record UserIdResponse(string UserId);
    public record RateRequest(string Ticker);
    public record RateResponse(string Ticker, string Currency, decimal Price, decimal ProviderPrice, long Timestamp);
    public record CreateOrderRequest(string SourceAmount, string IpAddress, string PaymentMethod);
    public record CreateOrderResponse(string Id, string Amount, string? Invoice, string? DepositAddress, long ExpiresAt);
    public record BalanceRequest(string Currency);
    public record BalanceResponse(decimal Balance);
    public record GetTransactionsRequest(long StartDate, long EndDate);
    public record GetTransactionsResponse(IReadOnlyList<ProviderTransaction> Transactions);
    public record ProviderTransaction(
        string OrderId,
        string Type,
        string SubType,
        decimal SourceAmount,
        string SourceCurrency,
        decimal DestinationAmount,
        string DestinationCurrency,
        string DestinationAddress,
        string Status,
        DateTimeOffset CreatedAt);
    public record SignupUrlRequest(string Callback);
    public record SignupUrlResponse(string SignupUrl);

    public record ProviderErrorResponse(
        string? Message,
        string? StatusCode,
        string? ErrorCode,
        string? ErrorMessage,
        JsonElement? ErrorDetails);
}

public class WithdrawalProviderApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public WithdrawalProviderClient.ProviderErrorResponse? Error { get; }

    public WithdrawalProviderApiException(HttpStatusCode statusCode,
        WithdrawalProviderClient.ProviderErrorResponse? error,
        string message) : base(message)
    {
        StatusCode = statusCode;
        Error = error;
    }
}
