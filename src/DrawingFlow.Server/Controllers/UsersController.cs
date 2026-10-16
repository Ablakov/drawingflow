using DrawingFlow.Application.Users;
using DrawingFlow.Contracts;
using DrawingFlow.Contracts.Users;
using Microsoft.AspNetCore.Mvc;

namespace DrawingFlow.Server.Controllers;

[ApiController]
[Route("api/users")]
public sealed class UsersController(UserService userService) : ControllerBase
{
    [HttpGet("me")]
    public Task<CurrentUserDto> GetMe(CancellationToken cancellationToken)
        => userService.GetCurrentAsync(cancellationToken);

    /// <summary>Список пользователей сервера (только администратор сервера).</summary>
    [HttpGet]
    public Task<PagedResult<UserDto>> List([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
        => userService.ListAsync(search, page, pageSize, cancellationToken);

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create([FromBody] CreateUserRequest request, CancellationToken cancellationToken)
    {
        var user = await userService.CreateAsync(request, cancellationToken);
        return Created($"/api/users/{user.Id}", user);
    }

    [HttpPut("{userId:guid}")]
    public Task<UserDto> Update(Guid userId, [FromBody] UpdateUserRequest request, CancellationToken cancellationToken)
        => userService.UpdateAsync(userId, request, cancellationToken);
}
