# nopayn-dotnet-sdk

Official C#/.NET SDK for the [NoPayn Payment Gateway](https://costplus.io). Simplifies the HPP (Hosted Payment Page) redirect flow, HMAC payload signing, and webhook verification.

[![CI](https://github.com/NoPayn/C_.NET-sdk/actions/workflows/ci.yml/badge.svg)](https://github.com/NoPayn/C_.NET-sdk/actions/workflows/ci.yml)

## Features

- **Zero dependencies** — uses only built-in `System.Text.Json` and `System.Security.Cryptography`
- Targets .NET 8.0 with C# 12 features (records, file-scoped namespaces, pattern matching)
- Nullable reference types enabled throughout
- HMAC-SHA256 signature generation and constant-time verification
- Automatic snake\_case/PascalCase mapping between the API and the SDK
- Webhook parsing + API-based order verification (as recommended by NoPayn)
- Fully async API surface

## Requirements

- .NET 8.0 SDK or later
- A NoPayn / Cost+ merchant account — [manage.nopayn.io](https://manage.nopayn.io/)

## Installation

### NuGet (when published)

```bash
dotnet add package NoPayn
```

### Project Reference (local development)

```bash
dotnet add reference path/to/src/NoPayn/NoPayn.csproj
```

## Quick Start

### 1. Initialise the client

```csharp
using NoPayn;
using NoPayn.Models;

var nopayn = new NoPaynClient(new NoPaynConfig(
    ApiKey: "your-api-key",       // From the NoPayn merchant portal
    MerchantId: "your-project"    // Your project/merchant ID
));
```

### 2. Create a payment and redirect to the HPP

```csharp
var result = await nopayn.GeneratePaymentUrlAsync(new CreateOrderParams
{
    Amount = 1295,              // €12.95 in cents
    Currency = "EUR",
    MerchantOrderId = "ORDER-001",
    Description = "Premium Widget",
    ReturnUrl = "https://shop.example.com/success",
    FailureUrl = "https://shop.example.com/failure",
    WebhookUrl = "https://shop.example.com/webhook",
    Locale = "en-GB",
    ExpirationPeriod = "PT30M",
});

// Redirect the customer
// result.OrderUrl   → HPP (customer picks payment method)
// result.PaymentUrl → direct link to the first transaction's payment method
// result.Signature  → HMAC-SHA256 for verification
// result.OrderId    → NoPayn order UUID
```

### 3. Handle the webhook

```csharp
app.MapPost("/webhook", async (HttpContext ctx) =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var rawBody = await reader.ReadToEndAsync();
    var verified = await nopayn.VerifyWebhookAsync(rawBody);

    Console.WriteLine(verified.Order.Status); // "completed", "cancelled", etc.
    Console.WriteLine(verified.IsFinal);      // true when the order won't change

    if (verified.Order.Status == "completed")
    {
        // Fulfil the order
    }

    return Results.Ok();
});
```

## API Reference

### `new NoPaynClient(config)`

| Parameter    | Type     | Required | Default                        |
|--------------|----------|----------|--------------------------------|
| `ApiKey`     | `string` | Yes      | —                              |
| `MerchantId` | `string` | Yes      | —                              |
| `BaseUrl`    | `string` | No       | `https://api.nopayn.co.uk`     |

An optional `HttpClient` can be passed as the second constructor parameter for custom HTTP handling or testing.

### `client.CreateOrderAsync(params): Task<Order>`

Creates an order via `POST /v1/orders/`.

| Parameter          | Type                    | Required | Description                                      |
|--------------------|-------------------------|----------|--------------------------------------------------|
| `Amount`           | `int`                   | Yes      | Amount in smallest currency unit (cents)          |
| `Currency`         | `string`                | Yes      | ISO 4217 code (`EUR`, `GBP`, `USD`, `NOK`, `SEK`) |
| `MerchantOrderId`  | `string?`               | No       | Your internal order reference                     |
| `Description`      | `string?`               | No       | Order description                                 |
| `ReturnUrl`        | `string?`               | No       | Redirect after successful payment                 |
| `FailureUrl`       | `string?`               | No       | Redirect on cancel/expiry/error                   |
| `WebhookUrl`       | `string?`               | No       | Async status-change notifications                 |
| `Locale`           | `string?`               | No       | HPP language (`en-GB`, `de-DE`, `nl-NL`, etc.)    |
| `PaymentMethods`   | `IReadOnlyList<string>?` | No       | Filter HPP methods                                |
| `ExpirationPeriod` | `string?`               | No       | ISO 8601 duration (`PT30M`)                       |

**Available payment methods:** `credit-card`, `apple-pay`, `google-pay`, `vipps-mobilepay`

### `client.GetOrderAsync(orderId): Task<Order>`

Fetches order details via `GET /v1/orders/{id}/`.

### `client.CreateRefundAsync(orderId, amount, description?): Task<Refund>`

Issues a full or partial refund via `POST /v1/orders/{id}/refunds/`.

### `client.GeneratePaymentUrlAsync(params): Task<PaymentUrlResult>`

Convenience method that creates an order and returns:

```csharp
public record PaymentUrlResult(
    string OrderId,        // NoPayn order UUID
    string OrderUrl,       // HPP URL
    string? PaymentUrl,    // Direct payment URL (first transaction)
    string Signature,      // HMAC-SHA256 of amount:currency:orderId
    Order Order            // Full order object
);
```

### `client.GenerateSignature(amount, currency, orderId): string`

Generates an HMAC-SHA256 hex signature. The canonical message is `{amount}:{currency}:{orderId}`, signed with the API key.

### `client.VerifySignature(amount, currency, orderId, signature): bool`

Constant-time verification of an HMAC-SHA256 signature. Returns `true` if valid.

### `client.VerifyWebhookAsync(rawBody): Task<VerifiedWebhook>`

Parses the webhook body, then calls `GET /v1/orders/{id}/` to verify the actual status. Returns:

```csharp
public record VerifiedWebhook(
    string OrderId,    // NoPayn order UUID from the webhook
    Order Order,       // Order details verified via API
    bool IsFinal       // true for completed/cancelled/expired/error
);
```

### `client.ParseWebhookBody(rawBody): WebhookPayload`

Parses and validates a webhook body without calling the API.

### Standalone HMAC Utilities

```csharp
using NoPayn;

var sig = NoPaynSignature.Generate("your-api-key", 1295, "EUR", "order-uuid");
var ok  = NoPaynSignature.Verify("your-api-key", 1295, "EUR", "order-uuid", sig);
```

## Error Handling

```csharp
using NoPayn.Exceptions;

try
{
    await nopayn.CreateOrderAsync(new CreateOrderParams { Amount = 100, Currency = "EUR" });
}
catch (ApiException ex)
{
    Console.Error.WriteLine(ex.StatusCode);  // 401, 400, etc.
    Console.Error.WriteLine(ex.ErrorBody);   // Raw API error response
}
catch (NoPaynException ex)
{
    Console.Error.WriteLine(ex.Message);     // Network or parsing error
}
```

| Exception           | Description                          |
|---------------------|--------------------------------------|
| `NoPaynException`   | Base exception (network, parsing)    |
| `ApiException`      | HTTP error from the API              |
| `WebhookException`  | Invalid webhook payload              |

## Order Statuses

| Status       | Final? | Description                                    |
|--------------|--------|------------------------------------------------|
| `new`        | No     | Order created                                  |
| `processing` | No     | Payment in progress                            |
| `completed`  | Yes    | Payment successful — deliver the goods         |
| `cancelled`  | Yes    | Payment cancelled by customer                  |
| `expired`    | Yes    | Payment link timed out                         |
| `error`      | Yes    | Technical failure                              |

## Webhook Best Practices

1. **Always verify via the API** — the webhook payload only contains the order ID, never the status. The SDK's `VerifyWebhookAsync()` does this automatically.
2. **Return HTTP 200** to acknowledge receipt. Any other code triggers up to 10 retries (2 minutes apart).
3. **Implement a backup poller** — for orders older than 10 minutes that haven't reached a final status, poll `GetOrderAsync()` as a safety net.
4. **Be idempotent** — you may receive the same webhook multiple times.

## Demo Merchant Site

A Docker-based demo app is included for testing the full payment flow.

### Run with Docker Compose

```bash
cd demo

# Create a .env file
cat > .env << EOF
NOPAYN_API_KEY=your-api-key
NOPAYN_MERCHANT_ID=your-merchant-id
PUBLIC_URL=http://localhost:3000
EOF

docker compose up --build
```

Open [http://localhost:3000](http://localhost:3000) to see the demo checkout page.

### Run without Docker

```bash
# Build everything
dotnet build

# Start the demo
cd demo
NOPAYN_API_KEY=your-key NOPAYN_MERCHANT_ID=your-id dotnet run
```

## Testing

```bash
dotnet test                                          # Run all tests
dotnet test --collect:"XPlat Code Coverage"          # With coverage report
```

## Test Cards

Use these cards in NoPayn test mode (project status `active-testing`):

| Card                     | Number                | Notes                                        |
|--------------------------|-----------------------|----------------------------------------------|
| Visa (frictionless)      | `4018 8100 0010 0036` | No 3DS challenge                             |
| Mastercard (frictionless) | `5420 7110 0021 0016` | No 3DS challenge                             |
| Visa (3DS)               | `4018 8100 0015 0015` | OTP: `0101` (success), `3333` (fail)         |
| Mastercard (3DS)         | `5299 9100 1000 0015` | OTP: `4445` (success), `9999` (fail)         |

Use any future expiry date and any 3-digit CVC.

## Solution Structure

```
NoPayn.sln
├── src/NoPayn/                    # Class library (net8.0)
│   ├── NoPaynClient.cs            # Main SDK client
│   ├── Signature.cs               # HMAC-SHA256 utilities
│   ├── Models/                    # Request/response records
│   │   ├── NoPaynConfig.cs
│   │   ├── CreateOrderParams.cs
│   │   ├── Order.cs
│   │   ├── Transaction.cs
│   │   ├── Refund.cs
│   │   ├── WebhookPayload.cs
│   │   ├── PaymentUrlResult.cs
│   │   └── VerifiedWebhook.cs
│   └── Exceptions/
│       ├── NoPaynException.cs
│       ├── ApiException.cs
│       └── WebhookException.cs
├── tests/NoPayn.Tests/            # xUnit tests
│   ├── SignatureTests.cs
│   ├── ClientTests.cs
│   └── WebhookTests.cs
└── demo/                          # ASP.NET Core minimal API demo
    ├── Program.cs
    ├── Views/
    ├── Dockerfile
    └── docker-compose.yml
```

## License

MIT — see [LICENSE](LICENSE).

## Support

- NoPayn API docs: [dev.nopayn.io](https://dev.nopayn.io/)
- Merchant portal: [manage.nopayn.io](https://manage.nopayn.io/)
- Developer: [Cost+](https://costplus.io)
