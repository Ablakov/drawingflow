namespace DrawingFlow.UnitTests.Domain;

/// <summary>Перечисления контрактов должны побитово совпадать с доменными: приведение через int используется в маппинге.</summary>
public sealed class EnumParityTests
{
    public static TheoryData<Type, Type> Pairs => new()
    {
        { typeof(DrawingFlow.Domain.Enums.OrganizationRole), typeof(DrawingFlow.Contracts.OrganizationRole) },
        { typeof(DrawingFlow.Domain.Enums.ProjectRole), typeof(DrawingFlow.Contracts.ProjectRole) },
        { typeof(DrawingFlow.Domain.Enums.ProjectStatus), typeof(DrawingFlow.Contracts.ProjectStatus) },
        { typeof(DrawingFlow.Domain.Enums.DocumentStatus), typeof(DrawingFlow.Contracts.Documents.DocumentStatus) },
        { typeof(DrawingFlow.Domain.Enums.VersionStatus), typeof(DrawingFlow.Contracts.Documents.VersionStatus) },
        { typeof(DrawingFlow.Domain.Enums.FileMatchingRule), typeof(DrawingFlow.Contracts.Agents.FileMatchingRule) },
        { typeof(DrawingFlow.Domain.Enums.SyncOperationStatus), typeof(DrawingFlow.Contracts.Agents.SyncOperationStatus) },
        { typeof(DrawingFlow.Domain.Enums.SyncRequestedAction), typeof(DrawingFlow.Contracts.Agents.SyncRequestedAction) },
        { typeof(DrawingFlow.Domain.Enums.ReviewDecision), typeof(DrawingFlow.Contracts.Workflow.ReviewDecision) },
        { typeof(DrawingFlow.Domain.Enums.WorkflowTaskType), typeof(DrawingFlow.Contracts.Workflow.WorkflowTaskType) },
        { typeof(DrawingFlow.Domain.Enums.WorkflowTaskStatus), typeof(DrawingFlow.Contracts.Workflow.WorkflowTaskStatus) },
    };

    [Theory]
    [MemberData(nameof(Pairs))]
    public void Domain_and_contract_enums_match(Type domain, Type contract)
    {
        var domainMembers = Enum.GetNames(domain).ToDictionary(n => n, n => Convert.ToInt32(Enum.Parse(domain, n)));
        var contractMembers = Enum.GetNames(contract).ToDictionary(n => n, n => Convert.ToInt32(Enum.Parse(contract, n)));
        Assert.Equal(domainMembers.OrderBy(k => k.Key), contractMembers.OrderBy(k => k.Key));
    }

    [Fact]
    public void Permission_name_constants_match_domain_flags()
    {
        static IEnumerable<string> Constants(Type type) => type
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Select(f => (string)f.GetRawConstantValue()!);

        var organizationFlags = Enum.GetNames<DrawingFlow.Domain.Authorization.OrganizationPermission>().Where(n => n != "None").OrderBy(n => n);
        var projectFlags = Enum.GetNames<DrawingFlow.Domain.Authorization.ProjectPermission>().Where(n => n != "None").OrderBy(n => n);

        Assert.Equal(organizationFlags, Constants(typeof(DrawingFlow.Contracts.OrganizationPermissions)).OrderBy(n => n));
        Assert.Equal(projectFlags, Constants(typeof(DrawingFlow.Contracts.ProjectPermissions)).OrderBy(n => n));
    }

    [Fact]
    public void Permission_names_are_computed_from_flags()
    {
        var names = DrawingFlow.Application.Common.ContractMapping.PermissionNames(DrawingFlow.Domain.Authorization.AccessRules.PermissionsOf(DrawingFlow.Domain.Enums.ProjectRole.Reviewer));
        Assert.Equal(new[] { "View", "Review" }, names);
    }
}
