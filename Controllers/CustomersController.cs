using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using IamApi.Models;
using IamApi.Services;

namespace IamApi.Controllers;

[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/[controller]")]
[Route("api/v{version:apiVersion}/[controller]")]
public sealed class CustomersController : ControllerBase
{
    private readonly IDataStore _dataStore;
    private readonly ILogger<CustomersController> _logger;

    public CustomersController(IDataStore dataStore, ILogger<CustomersController> logger)
    {
        _dataStore = dataStore;
        _logger = logger;
    }

    [HttpGet]
    [OutputCache(PolicyName = "Short")]
    public ActionResult<IEnumerable<Customer>> GetCustomers()
    {
        var customers = _dataStore.GetCustomers();
        return Ok(customers.OrderBy(c => c.Name));
    }

    [HttpGet("{id}")]
    public ActionResult<Customer> GetCustomer(string id)
    {
        var customer = _dataStore.GetCustomer(id);
        if (customer == null)
        {
            return NotFound();
        }
        return Ok(customer);
    }

    [HttpPost]
    public ActionResult<Customer> CreateCustomer([FromBody] CreateCustomerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Customer name is required");
        }

        var customer = new Customer
        {
            Id = Guid.NewGuid().ToString(),
            Name = request.Name,
            Description = request.Description ?? string.Empty,
            ContactEmail = request.ContactEmail,
            DefaultRegistry = request.DefaultRegistry,
            DefaultContainerImage = request.DefaultContainerImage,
            CreatedAt = DateTime.UtcNow
        };
        
        _dataStore.AddCustomer(customer);
        _logger.LogInformation("Customer created: {CustomerName}", customer.Name);
        
        return CreatedAtAction(nameof(GetCustomer), new { id = customer.Id }, customer);
    }

    [HttpPut("{id}")]
    public ActionResult<Customer> UpdateCustomer(string id, [FromBody] CreateCustomerRequest request)
    {
        var existing = _dataStore.GetCustomer(id);
        if (existing == null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Customer name is required");
        }

        existing.Name = request.Name;
        existing.Description = request.Description ?? string.Empty;
        existing.ContactEmail = request.ContactEmail;
        existing.DefaultRegistry = request.DefaultRegistry;
        existing.DefaultContainerImage = request.DefaultContainerImage;
        existing.UpdatedAt = DateTime.UtcNow;
        
        _dataStore.UpdateCustomer(existing);
        _logger.LogInformation("Customer updated: {CustomerName}", existing.Name);
        
        return Ok(existing);
    }

    [HttpDelete("{id}")]
    public ActionResult DeleteCustomer(string id)
    {
        var customer = _dataStore.GetCustomer(id);
        if (customer == null)
        {
            return NotFound();
        }

        _dataStore.DeleteCustomer(id);
        _logger.LogInformation("Customer deleted: {CustomerName}", customer.Name);
        
        return NoContent();
    }
}
