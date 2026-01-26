using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using IamApi.Models;
using IamApi.Services;

namespace IamApi.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class CustomersController : ControllerBase
{
    private readonly IDataStore _dataStore;
    private readonly ILogger<CustomersController> _logger;

    public CustomersController(IDataStore dataStore, ILogger<CustomersController> logger)
    {
        _dataStore = dataStore;
        _logger = logger;
    }

    [HttpGet]
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
    public ActionResult<Customer> CreateCustomer([FromBody] Customer customer)
    {
        if (string.IsNullOrWhiteSpace(customer.Name))
        {
            return BadRequest("Customer name is required");
        }

        customer.Id = Guid.NewGuid().ToString();
        customer.CreatedAt = DateTime.UtcNow;
        
        _dataStore.AddCustomer(customer);
        _logger.LogInformation("Customer created: {CustomerName}", customer.Name);
        
        return CreatedAtAction(nameof(GetCustomer), new { id = customer.Id }, customer);
    }

    [HttpPut("{id}")]
    public ActionResult<Customer> UpdateCustomer(string id, [FromBody] Customer customer)
    {
        var existing = _dataStore.GetCustomer(id);
        if (existing == null)
        {
            return NotFound();
        }

        customer.Id = id;
        customer.CreatedAt = existing.CreatedAt;
        customer.UpdatedAt = DateTime.UtcNow;
        
        _dataStore.UpdateCustomer(customer);
        _logger.LogInformation("Customer updated: {CustomerName}", customer.Name);
        
        return Ok(customer);
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
