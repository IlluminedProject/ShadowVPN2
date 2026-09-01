using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using ShadowVPN2.Infrastructure;
using Xunit;

namespace ShadowVPN2.Tests;

public sealed class ApplicationServicesTests {
    [Fact]
    public void Application_services_should_build_with_valid_dependencies() {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions {
            EnvironmentName = "Development",
            ApplicationName = typeof(ApplicationServicesTests).Assembly.GetName().Name
        });

        var action = () => {
            builder.AddApplicationServices();
            using var app = builder.Build();
        };

        action.Should().NotThrow();
    }
}