using Forge.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.GitHub;

public static class GitHubExtensions
{
    public static IServiceCollection AddForgeGitHub(this IServiceCollection services)
    {
        services.AddSingleton<IGitHubService, GitHubService>();
        return services;
    }
}
