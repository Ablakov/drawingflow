using DrawingFlow.Application.Audit;
using DrawingFlow.Application.Organizations;
using DrawingFlow.Application.Projects;
using DrawingFlow.Contracts;
using DrawingFlow.Contracts.Audit;
using DrawingFlow.Contracts.Organizations;
using DrawingFlow.Contracts.Projects;
using Microsoft.AspNetCore.Mvc;

namespace DrawingFlow.Server.Controllers;

[ApiController]
[Route("api/organizations")]
public sealed class OrganizationsController(
    OrganizationService organizationService,
    ProjectService projectService,
    AuditService auditService) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<OrganizationDto>> List(CancellationToken cancellationToken)
        => organizationService.ListForCurrentUserAsync(cancellationToken);

    [HttpPost]
    public async Task<ActionResult<OrganizationDto>> Create([FromBody] CreateOrganizationRequest request, CancellationToken cancellationToken)
    {
        var organization = await organizationService.CreateAsync(request, cancellationToken);
        return Created($"/api/organizations/{organization.Id}", organization);
    }

    [HttpGet("{organizationId:guid}")]
    public Task<OrganizationDto> Get(Guid organizationId, CancellationToken cancellationToken)
        => organizationService.GetAsync(organizationId, cancellationToken);

    [HttpPut("{organizationId:guid}")]
    public Task<OrganizationDto> Update(Guid organizationId, [FromBody] UpdateOrganizationRequest request, CancellationToken cancellationToken)
        => organizationService.UpdateAsync(organizationId, request, cancellationToken);

    // --- Участники ---

    [HttpGet("{organizationId:guid}/members")]
    public Task<IReadOnlyList<OrganizationMemberDto>> ListMembers(Guid organizationId, CancellationToken cancellationToken)
        => organizationService.ListMembersAsync(organizationId, cancellationToken);

    [HttpPost("{organizationId:guid}/members")]
    public async Task<ActionResult<OrganizationMemberDto>> AddMember(Guid organizationId, [FromBody] AddOrganizationMemberRequest request, CancellationToken cancellationToken)
    {
        var member = await organizationService.AddMemberAsync(organizationId, request, cancellationToken);
        return Created($"/api/organizations/{organizationId}/members/{member.UserId}", member);
    }

    [HttpPut("{organizationId:guid}/members/{userId:guid}")]
    public Task<OrganizationMemberDto> UpdateMember(Guid organizationId, Guid userId, [FromBody] UpdateOrganizationMemberRequest request, CancellationToken cancellationToken)
        => organizationService.UpdateMemberRoleAsync(organizationId, userId, request, cancellationToken);

    [HttpDelete("{organizationId:guid}/members/{userId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid organizationId, Guid userId, CancellationToken cancellationToken)
    {
        await organizationService.RemoveMemberAsync(organizationId, userId, cancellationToken);
        return NoContent();
    }

    // --- Проекты ---

    [HttpGet("{organizationId:guid}/projects")]
    public Task<PagedResult<ProjectDto>> ListProjects(Guid organizationId, [FromQuery] ProjectListQuery query, CancellationToken cancellationToken)
        => projectService.ListAsync(organizationId, query, cancellationToken);

    [HttpPost("{organizationId:guid}/projects")]
    public async Task<ActionResult<ProjectDto>> CreateProject(Guid organizationId, [FromBody] CreateProjectRequest request, CancellationToken cancellationToken)
    {
        var project = await projectService.CreateAsync(organizationId, request, cancellationToken);
        return Created($"/api/projects/{project.Id}", project);
    }

    // --- Аудит ---

    [HttpGet("{organizationId:guid}/audit")]
    public Task<PagedResult<AuditEventDto>> ListAudit(Guid organizationId, [FromQuery] AuditListQuery query, CancellationToken cancellationToken)
        => auditService.ListAsync(organizationId, query, cancellationToken);
}
