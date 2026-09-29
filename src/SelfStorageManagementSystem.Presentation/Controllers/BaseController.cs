using Microsoft.AspNetCore.Mvc;

namespace SelfStorageManagementSystem.Presentation.Controllers;

/// <summary>
/// Minimal abstract base controller for API controllers.
/// Provides uniform routing convention and API controller behaviors.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public abstract class BaseController : ControllerBase
{
}
