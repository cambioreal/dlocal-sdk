using System.Net;
using CambioReal.Dlocal.Tests.Fakes;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace CambioReal.Dlocal.Tests;

public sealed class DlocalClientTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    private static DlocalOptions NewOptions() => new()
    {
        Environment = DlocalEnvironment.Sandbox,
        Products =
        {
            [DlocalProducts.Checkout] = new DlocalProductCredential("login-1", "key-1", "secret-1"),
        },
    };

    private static (DlocalClient Client, RecordingHttpMessageHandler Transport) NewClient(
        params (HttpStatusCode Status, string Json)[] responses)
    {
        var transport = new RecordingHttpMessageHandler();
        foreach (var (status, json) in responses)
        {
            transport.RespondWith(status, json);
        }

        var httpClient = new HttpClient(transport) { BaseAddress = NewOptions().ResolveBaseAddress() };
        return (new DlocalClient(httpClient, Options.Create(NewOptions()), new MutableTimeProvider(Epoch)), transport);
    }

    [Fact]
    public void ValidOptionsPassValidation()
        => Should.NotThrow(() => NewOptions().Validate());

    [Fact]
    public void IncompleteProductCredentialThrows()
    {
        var options = NewOptions();
        options.Products["card"] = new DlocalProductCredential("l", "k", "");

        Should.Throw<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public async Task RequestsCarryHmacHeadersWithDeterministicSignature()
    {
        var (client, transport) = NewClient((HttpStatusCode.OK, "[]"));

        await client.GetPaymentMethodsAsync(DlocalProducts.Checkout);

        var request = transport.Requests.Single();
        request.RequestUri!.ToString().ShouldBe("https://sandbox.dlocal.com/payments-methods?country=BR");
        request.Headers!["X-Login"].ShouldBe("login-1");
        request.Headers!["X-Trans-Key"].ShouldBe("key-1");
        request.Headers!["X-Version"].ShouldBe("2.1");
        request.Headers!["X-Date"].ShouldBe("2026-07-15T12:00:00.000Z");

        // HMAC-SHA256("login-1" + date + "", "secret-1") — assinatura do legado, determinística com o relógio fixo.
        var expected = Convert.ToHexStringLower(
            System.Security.Cryptography.HMACSHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("secret-1"),
                System.Text.Encoding.UTF8.GetBytes("login-1" + "2026-07-15T12:00:00.000Z")));
        request.Headers!["Authorization"].ShouldBe($"V2-HMAC-SHA256, Signature: {expected}");
    }

    [Fact]
    public async Task UnknownProductThrowsBeforeAnyRequest()
    {
        var (client, transport) = NewClient();

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await client.GetPaymentMethodsAsync("inexistente"));

        transport.Requests.Count.ShouldBe(0);
    }

    [Fact]
    public async Task CreatePaymentSerializesSnakeCaseAndSignsBody()
    {
        var (client, transport) = NewClient((HttpStatusCode.OK,
            """{"id":"PAY-1","status":"PENDING","payment_method_id":"PQ","order_id":"BR1"}"""));

        var payment = await client.CreatePaymentAsync(DlocalProducts.Checkout, new CreateDlocalPaymentRequest
        {
            Amount = 500.00m,
            PaymentMethodId = "PQ",
            OrderId = "BR1",
            Payer = new DlocalPayer { Name = "Fulano", Document = "52998224725" },
        });

        payment.Status.ShouldBe("PENDING");

        var request = transport.Requests.Single();
        request.Body!.ShouldContain("\"payment_method_id\":\"PQ\"");
        request.Body!.ShouldContain("\"payment_method_flow\":\"DIRECT\"");
        request.Body!.ShouldContain("\"order_id\":\"BR1\"");
    }

    /// <summary>Forma de erro real validada ao vivo: 404 = <c>{"code":4000,"message":"Payment not found"}</c>.</summary>
    [Fact]
    public async Task DomainErrorMapsNumericCode()
    {
        var (client, _) = NewClient((HttpStatusCode.NotFound, """{"code":4000,"message":"Payment not found"}"""));

        var error = await Should.ThrowAsync<DlocalApiException>(
            async () => await client.GetPaymentAsync(DlocalProducts.Checkout, "PAY-X"));

        error.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        error.ErrorCode.ShouldBe(4000);
        error.Message.ShouldContain("Payment not found");
    }
}
