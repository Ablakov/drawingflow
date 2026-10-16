using DrawingFlow.Application.Common;
using DrawingFlow.Domain.Exceptions;
using Api = DrawingFlow.Contracts;
using Dom = DrawingFlow.Domain.Enums;

namespace DrawingFlow.UnitTests.Application;

public sealed class ContractMappingTests
{
    [Fact]
    public void Roles_round_trip()
    {
        foreach (var role in Enum.GetValues<Dom.OrganizationRole>())
        {
            Assert.Equal(role, role.ToApi().ToDomain());
        }

        foreach (var role in Enum.GetValues<Dom.ProjectRole>())
        {
            Assert.Equal(role, role.ToApi().ToDomain());
        }
    }

    [Fact]
    public void Undefined_values_are_rejected_as_validation_errors()
    {
        Assert.Throws<ValidationException>(() => ((Api.OrganizationRole)42).ToDomain());
        Assert.Throws<ValidationException>(() => ((Api.ProjectRole)42).ToDomain());
        Assert.Throws<ValidationException>(() => ((Api.ProjectStatus)42).ToDomain());
    }

    [Fact]
    public void Paging_normalizes_bounds()
    {
        Assert.Equal((1, 50), Paging.Normalize(0, 0));
        Assert.Equal((3, Api.PageRequest.MaxPageSize), Paging.Normalize(3, 10_000));
        Assert.Equal((2, 20), Paging.Normalize(2, 20));
    }

    [Fact]
    public void Paging_converts_unspecified_kind_to_utc()
    {
        var unspecified = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Unspecified);
        var converted = Paging.ToUtc(unspecified);
        Assert.Equal(DateTimeKind.Utc, converted!.Value.Kind);
        Assert.Equal(unspecified.Ticks, converted.Value.Ticks);
        Assert.Null(Paging.ToUtc(null));
    }
}
