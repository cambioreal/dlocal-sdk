using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CambioReal.Dlocal;

/// <summary>Ambiente da dLocal API.</summary>
public enum DlocalEnvironment
{
    /// <summary>Sandbox — <c>https://sandbox.dlocal.com/</c>.</summary>
    Sandbox = 0,

    /// <summary>Produção — <c>https://api.dlocal.com/</c>.</summary>
    Production = 1,
}

/// <summary>Credencial de um produto/MID dLocal (a dLocal emite um trio por produto).</summary>
public sealed record DlocalProductCredential(string Login, string TransKey, string Secret);

/// <summary>Nomes canônicos dos produtos/MIDs usados pela plataforma (conjunto aberto).</summary>
public static class DlocalProducts
{
    public const string Checkout = "checkout";
    public const string Card = "card";
    public const string Remittance = "remittance";
    public const string Payout = "payout";
}

/// <summary>Configuração do <see cref="DlocalClient"/>.</summary>
public sealed class DlocalOptions
{
    /// <summary>Nome da seção de configuração sugerida.</summary>
    public const string SectionName = "Dlocal";

    /// <summary>
    /// Credenciais por produto/MID (<c>checkout</c>/<c>card</c>/<c>remittance</c>/<c>payout</c> —
    /// origem: <c>pass cambio-real-v2/dlocal/demo-env</c>). Toda operação declara o produto.
    /// </summary>
    public Dictionary<string, DlocalProductCredential> Products { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ambiente alvo. O padrão é <see cref="DlocalEnvironment.Sandbox"/>, deliberadamente.</summary>
    public DlocalEnvironment Environment { get; set; } = DlocalEnvironment.Sandbox;

    /// <summary>Sobrescreve o endereço base. Precisa terminar em <c>/</c>.</summary>
    public Uri? BaseAddress { get; set; }

    /// <summary>Header <c>X-Version</c> — o legado usa <c>2.1</c>.</summary>
    public string ApiVersion { get; set; } = "2.1";

    /// <summary>Timeout de cada requisição HTTP.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Endereço base efetivo.</summary>
    public Uri ResolveBaseAddress() => BaseAddress ?? Environment switch
    {
        DlocalEnvironment.Production => new Uri("https://api.dlocal.com/"),
        _ => new Uri("https://sandbox.dlocal.com/"),
    };

    /// <summary>Valida a configuração e lança se estiver inconsistente.</summary>
    /// <exception cref="InvalidOperationException">Nenhum produto configurado ou credencial incompleta.</exception>
    public void Validate()
    {
        if (Products.Count == 0)
        {
            throw new InvalidOperationException($"{nameof(DlocalOptions)}.{nameof(Products)} precisa ter ao menos um produto.");
        }

        foreach (var (name, credential) in Products)
        {
            if (string.IsNullOrWhiteSpace(credential.Login)
                || string.IsNullOrWhiteSpace(credential.TransKey)
                || string.IsNullOrWhiteSpace(credential.Secret))
            {
                throw new InvalidOperationException($"Credencial dLocal incompleta para o produto '{name}'.");
            }
        }

        if (Timeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{nameof(Timeout)} precisa ser positivo.");
        }
    }
}

/// <summary>Erro devolvido pela dLocal (<c>{code, message, param?}</c> — confirmado ao vivo: 404 = code 4000).</summary>
public sealed class DlocalApiException : Exception
{
    /// <summary>Cria uma exceção sem contexto.</summary>
    public DlocalApiException()
    {
    }

    /// <summary>Cria uma exceção com mensagem.</summary>
    public DlocalApiException(string message)
        : base(message)
    {
    }

    /// <summary>Cria uma exceção com mensagem e causa.</summary>
    public DlocalApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Cria uma exceção com contexto de resposta.</summary>
    public DlocalApiException(HttpStatusCode statusCode, int? errorCode, string message, string? responseBody)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        ResponseBody = responseBody;
    }

    /// <summary>Status HTTP da resposta.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary><c>code</c> numérico da dLocal (ex.: 4000 = Payment not found; 3001 = credencial inválida).</summary>
    public int? ErrorCode { get; }

    /// <summary>Corpo bruto da resposta, para diagnóstico.</summary>
    public string? ResponseBody { get; }
}

/// <summary>Método de pagamento — <c>GET /payments-methods</c> (validado ao vivo nos 4 MIDs).</summary>
public sealed record DlocalPaymentMethod
{
    public string? Id { get; init; }
    public string? Type { get; init; }
    public string? Name { get; init; }
    public string? Logo { get; init; }
    public IReadOnlyList<string>? AllowedFlows { get; init; }
}

/// <summary>
/// Corpo de <c>POST /payments</c> — Payins v2.1, confirmado no legado
/// (<c>cerebro/EnvioBr/DLocal/PixRequest</c>: PQ/DIRECT). Campos além destes (risk data etc.)
/// podem ir em <see cref="AdditionalRiskData"/> cru.
/// </summary>
public sealed record CreateDlocalPaymentRequest
{
    public required decimal Amount { get; init; }
    public string Currency { get; init; } = "BRL";
    public string Country { get; init; } = "BR";

    /// <summary><c>PQ</c> (Pix QR), boleto, cartão etc. — ids da dLocal.</summary>
    public required string PaymentMethodId { get; init; }

    public string PaymentMethodFlow { get; init; } = "DIRECT";
    public required DlocalPayer Payer { get; init; }
    public required string OrderId { get; init; }
    public string? NotificationUrl { get; init; }
    public string? Description { get; init; }
    public JsonElement? AdditionalRiskData { get; init; }
}

/// <summary>Pagador — shape do legado.</summary>
public sealed record DlocalPayer
{
    public required string Name { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public required string Document { get; init; }
    public DlocalAddress? Address { get; init; }
    public string? UserReference { get; init; }
}

/// <summary>Endereço do pagador.</summary>
public sealed record DlocalAddress
{
    public string? State { get; init; }
    public string? City { get; init; }
    public string? ZipCode { get; init; }
    public string? Street { get; init; }
    public string? Number { get; init; }
}

/// <summary>
/// Cotação entre moedas — resposta de <c>GET /currency-exchanges</c> (confirmado na doc oficial:
/// <c>https://docs.dlocal.com/reference/get-an-exchange-rate</c>). Só tem <c>from</c>/<c>to</c>/<c>rate</c>
/// — não existe parâmetro/campo <c>amount</c> neste endpoint (verificado em duas páginas da doc
/// oficial, nenhuma menciona <c>amount</c>; o rate é o resultado, não um conversor de valor).
/// </summary>
public sealed record DlocalCurrencyExchange
{
    public string? From { get; init; }
    public string? To { get; init; }
    public decimal? Rate { get; init; }
}

/// <summary>
/// Corpo de <c>POST /refunds</c> — confirmado em <c>https://docs.dlocal.com/reference/make-a-refund</c>.
/// Único campo obrigatório é <see cref="PaymentId"/>; sem <see cref="Amount"/> a dLocal reembolsa o
/// valor total do payment. Campos bancários são recomendados (não obrigatórios) para
/// TICKET/BANK_TRANSFER.
/// </summary>
public sealed record CreateDlocalRefundRequest
{
    public required string PaymentId { get; init; }
    public decimal? Amount { get; init; }
    public string? Currency { get; init; }
    public string? NotificationUrl { get; init; }
    public string? CustomMerchantName { get; init; }
    public string? Description { get; init; }
    public string? OrderRefundId { get; init; }
    public string? BeneficiaryName { get; init; }
    public string? BeneficiaryLastname { get; init; }
    public string? Bank { get; init; }
    public string? BankCode { get; init; }
    public string? BankAccount { get; init; }
    public string? BankAccountType { get; init; }
    public string? BankBranch { get; init; }
    public string? BankBranchName { get; init; }
    public string? DocumentType { get; init; }
    public string? DocumentId { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? Address { get; init; }
    public string? City { get; init; }

    /// <summary>Split do reembolso entre uma ou mais contas — shape variável, exposto cru.</summary>
    public JsonElement? Splits { get; init; }
}

/// <summary>
/// Reembolso — resposta de POST/GET <c>/refunds</c>, campos confirmados em "The Refund Object"
/// (<c>https://docs.dlocal.com/reference/the-refund-object</c>) e nos exemplos ao vivo de
/// <c>make-a-refund</c>/<c>retrieve-a-refund</c>.
/// </summary>
public sealed record DlocalRefund
{
    public string? Id { get; init; }
    public string? PaymentId { get; init; }
    public string? OrderId { get; init; }
    public string? CustomMerchantName { get; init; }
    public decimal? Amount { get; init; }
    public decimal? AmountRefunded { get; init; }
    public string? Currency { get; init; }

    /// <summary>Valores conhecidos: <c>SUCCESS</c> (visto ao vivo na doc), conjunto aberto ⇒ string.</summary>
    public string? Status { get; init; }
    public int? StatusCode { get; init; }
    public string? StatusDetail { get; init; }

    /// <summary>
    /// Cru (<see langword="string"/>), não <see cref="DateTimeOffset"/>: o exemplo oficial devolve
    /// offset sem dois-pontos (<c>+0000</c>), formato que o conversor padrão do
    /// <c>System.Text.Json</c> rejeita (confirmado empiricamente) — mesmo risco latente, não
    /// corrigido aqui, existe em <see cref="DlocalPayment.CreatedDate"/>.
    /// </summary>
    public string? CreatedDate { get; init; }
    public string? NotificationUrl { get; init; }
    public string? Description { get; init; }
    public string? Bank { get; init; }
    public string? BankAccount { get; init; }
    public string? BankAccountType { get; init; }
    public string? BankBranch { get; init; }
}

/// <summary>Pagamento — resposta de POST/GET <c>/payments</c>. Campos variáveis expostos crus.</summary>
public sealed record DlocalPayment
{
    public string? Id { get; init; }
    public decimal? Amount { get; init; }
    public string? Currency { get; init; }
    public string? Country { get; init; }
    public string? PaymentMethodId { get; init; }

    /// <summary>Valores conhecidos: <c>PENDING</c>, <c>PAID</c>, <c>REJECTED</c>, <c>CANCELLED</c>, <c>EXPIRED</c> (conjunto aberto).</summary>
    public string? Status { get; init; }

    public string? StatusDetail { get; init; }
    public int? StatusCode { get; init; }
    public string? OrderId { get; init; }
    public JsonElement? ThreeDsecure { get; init; }
    public JsonElement? Ticket { get; init; }
    public JsonElement? Wallet { get; init; }
    public JsonElement? QrCode { get; init; }
    public DateTimeOffset? CreatedDate { get; init; }
}

/// <summary>
/// Cliente da dLocal Payins API v2.1 — assinatura <c>V2-HMAC-SHA256</c> de
/// <c>login + X-Date + body</c> com a secret do PRODUTO declarado em cada operação (a dLocal
/// emite um MID por produto: checkout/card/remittance/payout).
/// </summary>
public sealed class DlocalClient
{
    private static readonly JsonSerializerOptions Json = CreateJson();

    private readonly HttpClient httpClient;
    private readonly DlocalOptions options;
    private readonly TimeProvider timeProvider;

    /// <summary>Cria o cliente sobre um <see cref="HttpClient"/> já configurado.</summary>
    public DlocalClient(HttpClient httpClient, IOptions<DlocalOptions> options, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        this.httpClient = httpClient;
        this.options = options.Value;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Lista métodos de pagamento do MID. Leitura — o probe canônico por produto (validado ao vivo nos 4 MIDs).</summary>
    public Task<IReadOnlyList<DlocalPaymentMethod>> GetPaymentMethodsAsync(
        string product, string country = "BR", CancellationToken cancellationToken = default) =>
        SendAsync<IReadOnlyList<DlocalPaymentMethod>>(
            product, HttpMethod.Get, $"payments-methods?country={Uri.EscapeDataString(country)}", body: null, cancellationToken);

    /// <summary>Consulta um pagamento. <c>GET /payments/{id}</c> — fonte de verdade de status (404 = code 4000, validado ao vivo).</summary>
    public Task<DlocalPayment> GetPaymentAsync(string product, string paymentId, CancellationToken cancellationToken = default) =>
        SendAsync<DlocalPayment>(
            product, HttpMethod.Get, $"payments/{Uri.EscapeDataString(paymentId)}", body: null, cancellationToken);

    /// <summary>
    /// Cria um pagamento (payin PIX/boleto/cartão/TED conforme <c>payment_method_id</c>).
    /// **FINANCIAL-WRITE** — não executar contra sandbox sem autorização explícita (goal §0.5).
    /// </summary>
    public Task<DlocalPayment> CreatePaymentAsync(
        string product, CreateDlocalPaymentRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<DlocalPayment>(product, HttpMethod.Post, "payments", request, cancellationToken);

    /// <summary>
    /// Cotação atual entre moedas. <c>GET /currency-exchanges?from=&amp;to=</c> — leitura. A dLocal
    /// só aceita <c>from=USD</c> no momento (doc oficial); não há parâmetro <c>amount</c> neste
    /// endpoint.
    /// </summary>
    public Task<DlocalCurrencyExchange> GetCurrencyExchangeAsync(
        string product, string from, string to, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(from);
        ArgumentException.ThrowIfNullOrWhiteSpace(to);

        return SendAsync<DlocalCurrencyExchange>(
            product,
            HttpMethod.Get,
            $"currency-exchanges?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}",
            body: null,
            cancellationToken);
    }

    /// <summary>
    /// Cria um reembolso de um payin existente. <c>POST /refunds</c>.
    /// **FINANCIAL-WRITE** — não executar contra sandbox sem autorização explícita (goal §0.5).
    /// </summary>
    public Task<DlocalRefund> CreateRefundAsync(
        string product, CreateDlocalRefundRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<DlocalRefund>(product, HttpMethod.Post, "refunds", request, cancellationToken);

    /// <summary>Consulta um reembolso. <c>GET /refunds/{id}</c> — leitura.</summary>
    public Task<DlocalRefund> GetRefundAsync(string product, string refundId, CancellationToken cancellationToken = default) =>
        SendAsync<DlocalRefund>(
            product, HttpMethod.Get, $"refunds/{Uri.EscapeDataString(refundId)}", body: null, cancellationToken);

    private async Task<TResponse> SendAsync<TResponse>(
        string product, HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(product);

        if (!options.Products.TryGetValue(product, out var credential))
        {
            throw new InvalidOperationException(
                $"Produto dLocal '{product}' não configurado em {nameof(DlocalOptions)}.{nameof(DlocalOptions.Products)}.");
        }

        var payload = body is null ? string.Empty : JsonSerializer.Serialize(body, Json);
        var date = timeProvider.GetUtcNow().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");

        // Assinatura confirmada no legado (AbstractRequest::signature): HMAC-SHA256(login+date+body).
        var signature = Convert.ToHexStringLower(
            HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(credential.Secret),
                Encoding.UTF8.GetBytes(credential.Login + date + payload)));

        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));

        if (payload.Length > 0)
        {
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        }

        request.Headers.TryAddWithoutValidation("X-Date", date);
        request.Headers.TryAddWithoutValidation("X-Login", credential.Login);
        request.Headers.TryAddWithoutValidation("X-Trans-Key", credential.TransKey);
        request.Headers.TryAddWithoutValidation("X-Version", options.ApiVersion);
        request.Headers.TryAddWithoutValidation("Authorization", $"V2-HMAC-SHA256, Signature: {signature}");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var (code, message) = TryExtractError(responseBody);
            throw new DlocalApiException(
                response.StatusCode,
                code,
                $"A dLocal respondeu HTTP {(int)response.StatusCode} ({response.StatusCode})"
                + (message is null ? "." : $": {message}."),
                responseBody);
        }

        return JsonSerializer.Deserialize<TResponse>(responseBody, Json)
            ?? throw new DlocalApiException(
                response.StatusCode, null, "A dLocal devolveu um corpo JSON vazio onde um valor era esperado.", responseBody);
    }

    private static (int? Code, string? Message) TryExtractError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return (null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, null);
            }

            int? code = root.TryGetProperty("code", out var codeEl) && codeEl.ValueKind == JsonValueKind.Number
                ? codeEl.GetInt32()
                : null;
            var message = root.TryGetProperty("message", out var msgEl) && msgEl.ValueKind == JsonValueKind.String
                ? msgEl.GetString()
                : null;

            return (code, message);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static JsonSerializerOptions CreateJson()
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        json.MakeReadOnly(populateMissingResolver: true);
        return json;
    }
}

/// <summary>Registro do cliente dLocal no container.</summary>
public static class DlocalServiceCollectionExtensions
{
    /// <summary>Registra o cliente a partir de uma seção de configuração.</summary>
    public static IServiceCollection AddDlocalClient(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return services.AddDlocalClient(configuration.Bind);
    }

    /// <summary>Registra <see cref="DlocalClient"/> e o pipeline HTTP (assinatura por request no cliente).</summary>
    public static IServiceCollection AddDlocalClient(this IServiceCollection services, Action<DlocalOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);
        services.AddOptions<DlocalOptions>().Validate(
            options =>
            {
                options.Validate();
                return true;
            },
            "A configuração do DlocalOptions é inválida.");

        services.TryAddSingleton(TimeProvider.System);

        services.AddHttpClient("dlocal.api", (provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<DlocalOptions>>().Value;
            options.Validate();

            client.BaseAddress = options.ResolveBaseAddress();
            client.Timeout = options.Timeout;
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "CambioReal/1.0");
        });

        services.TryAddTransient(provider =>
        {
            var factory = provider.GetRequiredService<IHttpClientFactory>();
            return new DlocalClient(
                factory.CreateClient("dlocal.api"),
                provider.GetRequiredService<IOptions<DlocalOptions>>(),
                provider.GetRequiredService<TimeProvider>());
        });

        return services;
    }
}
