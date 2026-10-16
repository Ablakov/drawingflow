using DrawingFlow.Application.Projects;
using DrawingFlow.Contracts.Projects;
using Microsoft.AspNetCore.Mvc;

namespace DrawingFlow.Server.Controllers;

[ApiController]
[Route("api/projects")]
public sealed class ProjectsController(ProjectService projectService) : ControllerBase
{
    [HttpGet("{projectId:guid}")]
    public Task<ProjectDto> Get(Guid projectId, CancellationToken cancellationToken)
        => projectService.GetAsync(projectId, cancellationToken);

    [HttpPut("{projectId:guid}")]
    public Task<ProjectDto> Update(Guid projectId, [FromBody] UpdateProjectRequest request, CancellationToken cancellationToken)
        => projectService.UpdateAsync(projectId, request, cancellationToken);

    [HttpGet("{projectId:guid}/members")]
    public Task<IReadOnlyList<ProjectMemberDto>> ListMembers(Guid projectId, CancellationToken cancellationToken)
        => projectService.ListMembersAsync(projectId, cancellationToken);

    [HttpPost("{projectId:guid}/members")]
    public async Task<ActionResult<ProjectMemberDto>> AddMember(Guid projectId, [FromBody] AddProjectMemberRequest request, CancellationToken cancellationToken)
    {
        var member = await projectService.AddMemberAsync(projectId, request, cancellationToken);
        return Created($"/api/projects/{projectId}/members/{member.UserId}", member);
    }

    [HttpPut("{projectId:guid}/members/{userId:guid}")]
    public Task<ProjectMemberDto> UpdateMember(Guid projectId, Guid userId, [FromBody] UpdateProjectMemberRequest request, CancellationToken cancellationToken)
        => projectService.UpdateMemberRoleAsync(projectId, userId, request, cancellationToken);

    [HttpDelete("{projectId:guid}/members/{userId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid projectId, Guid userId, CancellationToken cancellationToken)
    {
        await projectService.RemoveMemberAsync(projectId, userId, cancellationToken);
        return NoContent();
    }
}
