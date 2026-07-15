using CambioReal.Dlocal;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace CambioReal.Dlocal.SandboxTests;

/// <summary>
/// Integração sandbox real, opt-in — executa os **probes de leitura por produto** definidos como
/// pré-condição do goal para o DLocal: <c>GET /payments-methods</c> em CADA MID
/// (checkout/card/remittance/payout) + <c>GET /payments/{fictício}</c> (404 code 4000).
/// Variáveis a partir do <c>pass cambio-real-v2/dlocal/demo-env</c>
/// ({PRODUTO}_LOGIN/KEY/SECRET → prefixo <c>DLOCAL_SANDBOX_</c>). Criação de pagamento =
/// financial-write, NUNCA existe aqui (goal §0.5).
/// </summary>
public sealed class DlocalSandboxTests
{
    private readonly ITestOutputHelper output;

    public DlocalSandboxTests(ITestOutputHelper output) => this.output = output;

    [Theory]
    [Trait("Category", "Sandbox")]
    [InlineData(DlocalProducts.Checkout)]
    [InlineData(DlocalProducts.Card)]
    [InlineData(DlocalProducts.Remittance)]
    [InlineData(DlocalProducts.Payout)]
    public async Task PaymentMethodsReadLivePerProduct(string product)
    {
        using var provider = BuildServiceProvider();
        var client = provider.GetRequiredService<DlocalClient>();

        var methods = await client.GetPaymentMethodsAsync(product);

        methods.Count.ShouldBeGreaterThan(0);
        output.WriteLine($"[{product}] GET payments-methods: 200, {methods.Count} método(s) — HMAC do MID validado.");
    }

    [Fact]
    [Trait("Category", "Sandbox")]
    public async Task PaymentGetWithFictitiousIdReturnsDomain404()
    {
        using var provider = BuildServiceProvider();
        var client = provider.GetRequiredService<DlocalClient>();

        var error = await Should.ThrowAsync<DlocalApiException>(
            async () => await client.GetPaymentAsync(DlocalProducts.Checkout, "PAY-FICTICIO-000000"));

        error.StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
        error.ErrorCode.ShouldBe(4000);
        output.WriteLine("GET payments/{fictício}: 404, code=4000 — acesso real ao recurso confirmado.");
    }

    private static ServiceProvider BuildServiceProvider()
    {
        string Req(string name) =>
            Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
                ? value
                : throw new InvalidOperationException($"{name} ausente — carregue de `pass cambio-real-v2/dlocal/demo-env`.");

        var services = new ServiceCollection();
        services.AddDlocalClient(options =>
        {
            options.Environment = DlocalEnvironment.Sandbox;

            foreach (var product in new[]
            {
                DlocalProducts.Checkout, DlocalProducts.Card, DlocalProducts.Remittance, DlocalProducts.Payout,
            })
            {
                var prefix = $"DLOCAL_SANDBOX_{product.ToUpperInvariant()}";
                options.Products[product] = new DlocalProductCredential(
                    Req($"{prefix}_LOGIN"), Req($"{prefix}_KEY"), Req($"{prefix}_SECRET"));
            }
        });

        return services.BuildServiceProvider();
    }
}
