using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using SmsService.Api.Sending;

namespace SmsService.Tests.Infrastructure;

/// <summary>
/// The real API against a fresh test database, with a fake SMS provider.
/// The background SendWorker is removed so API tests see deterministic statuses (the worker has its own tests).
/// </summary>
public sealed class ApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    public ISmsProviderClient Provider { get; } = Substitute.For<ISmsProviderClient>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<ISmsProviderClient>();
            services.AddSingleton(Provider);
        });
    }
}
