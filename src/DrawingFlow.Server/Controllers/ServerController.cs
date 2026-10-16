using System.Reflection;
using DrawingFlow.Application.Abstractions;
using DrawingFlow.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DrawingFlow.Server.Controllers;

[ApiController]
[Route("api/server")]
public sealed class ServerController(IClock clock) : ControllerBase
{
    /// <summary>Сведения о сервере. Доступны без аутентификации: используются клиентом для проверки адреса.</summary>
    [HttpGet("info")]
    [AllowAnonymous]
    public ServerInfoDto GetInfo()
    {
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "unknown";
        return new ServerInfoDto("DrawingFlow", version, clock.UtcNow);
    }
}
