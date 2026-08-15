using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace CambioReal.Dlocal.Tests;

public sealed class StartupValidationTests
{
    [Fact]
    public void InvalidOptionsFailThroughTheStandardStartupValidator()
    {
        var services = new ServiceCollection();
        services.AddDlocalClient(_ => { });

        using var provider = services.BuildServiceProvider();
        var validator = provider.GetRequiredService<IStartupValidator>();

        Should.Throw<OptionsValidationException>(validator.Validate);
    }

    [Fact]
    public void ValidOptionsPassThroughTheStandardStartupValidator()
    {
        var services = new ServiceCollection();
        services.AddDlocalClient(options => { options.Products["checkout"] = new DlocalProductCredential("login", "trans-key", "secret"); });

        using var provider = services.BuildServiceProvider();

        Should.NotThrow(provider.GetRequiredService<IStartupValidator>().Validate);
    }
}
