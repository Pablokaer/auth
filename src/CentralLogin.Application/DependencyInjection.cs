using CentralLogin.Application.Users;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace CentralLogin.Application;

/// <summary>Composition root for the Application layer.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<ProvisionUserCommandValidator>(
            includeInternalTypes: true);

        services.AddScoped<IUserProvisioningService, UserProvisioningService>();

        return services;
    }
}
