using DrawingFlow.Application.Audit;
using DrawingFlow.Application.Auth;
using DrawingFlow.Application.Authorization;
using DrawingFlow.Application.Agents;
using DrawingFlow.Application.Documents;
using DrawingFlow.Application.Sync;
using DrawingFlow.Application.Organizations;
using DrawingFlow.Application.Projects;
using DrawingFlow.Application.Users;
using DrawingFlow.Application.Workflow;
using Microsoft.Extensions.DependencyInjection;

namespace DrawingFlow.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddDrawingFlowApplication(this IServiceCollection services)
    {
        services.AddScoped<AccessService>();
        services.AddScoped<AuditService>();
        services.AddScoped<AuthService>();
        services.AddScoped<UserService>();
        services.AddScoped<OrganizationService>();
        services.AddScoped<ProjectService>();
        services.AddScoped<DocumentService>();
        services.AddScoped<AgentService>();
        services.AddScoped<WatchedFolderService>();
        services.AddScoped<SyncOperationService>();
        services.AddScoped<WorkflowService>();
        return services;
    }
}
