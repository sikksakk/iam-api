using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using IamApi.Models;
using IamApi.Services;

namespace IamApi.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class RegistriesController : ControllerBase
{
    private readonly IDataStore _dataStore;
    private readonly ILogger<RegistriesController> _logger;

    public RegistriesController(IDataStore dataStore, ILogger<RegistriesController> logger)
    {
        _dataStore = dataStore;
        _logger = logger;
    }

    [HttpGet]
    public ActionResult<IEnumerable<ContainerRegistry>> GetAll()
    {
        return Ok(_dataStore.GetRegistries());
    }

    [HttpGet("{id}")]
    public ActionResult<ContainerRegistry> GetById(Guid id)
    {
        var registry = _dataStore.GetRegistry(id);
        if (registry == null)
            return NotFound();

        return Ok(registry);
    }

    [HttpPost]
    public ActionResult<ContainerRegistry> Create(CreateRegistryRequest request)
    {
        var registry = new ContainerRegistry
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Server = request.Server,
            Username = request.Username,
            Password = request.Password,
            CreatedAt = DateTime.UtcNow
        };

        _dataStore.AddRegistry(registry);
        _logger.LogInformation("Created registry {Name} with server {Server}", registry.Name, registry.Server);

        return CreatedAtAction(nameof(GetById), new { id = registry.Id }, registry);
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(Guid id)
    {
        var registry = _dataStore.GetRegistry(id);
        if (registry == null)
            return NotFound();

        _dataStore.RemoveRegistry(id);
        _logger.LogInformation("Deleted registry {Name}", registry.Name);

        return NoContent();
    }
}
