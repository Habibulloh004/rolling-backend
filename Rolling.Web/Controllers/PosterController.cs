using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Rolling.Infrastructure.Poster;

namespace Rolling.Web.Controllers;

[ApiController]
[Route("api/poster")]
public class PosterController : ControllerBase
{
    private readonly ICachedPosterService _posterService;
    private readonly ILogger<PosterController> _logger;

    public PosterController(
        ICachedPosterService posterService,
        ILogger<PosterController> logger)
    {
        _posterService = posterService;
        _logger = logger;
    }

    // ===== MENU ENDPOINTS =====

    /// <summary>
    /// GET /api/poster/products
    /// Get all products (cached)
    /// </summary>
    [HttpGet("products")]
    public async Task<IActionResult> GetProducts([FromQuery] Dictionary<string, string?>? queryParams, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _posterService.GetProductsAsync(queryParams, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting products");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// POST /api/poster/products/revalidate
    /// Revalidate products cache
    /// </summary>
    [HttpPost("products/revalidate")]
    public async Task<IActionResult> RevalidateProducts([FromQuery] Dictionary<string, string?>? queryParams, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _posterService.RevalidateProductsAsync(queryParams, cancellationToken);
            return Ok(new
            {
                message = "Products cache revalidated",
                data = result.Data,
                source = result.Source,
                cached = result.Cached
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revalidating products");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// GET /api/poster/categories
    /// Get all categories (cached)
    /// </summary>
    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _posterService.GetCategoriesAsync(cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting categories");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// POST /api/poster/categories/revalidate
    /// Revalidate categories cache
    /// </summary>
    [HttpPost("categories/revalidate")]
    public async Task<IActionResult> RevalidateCategories(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _posterService.RevalidateCategoriesAsync(cancellationToken);
            return Ok(new
            {
                message = "Categories cache revalidated",
                data = result.Data,
                source = result.Source,
                cached = result.Cached
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revalidating categories");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ===== PROMOTION ENDPOINTS =====

    /// <summary>
    /// GET /api/poster/promotions
    /// Get promotions (cached)
    /// </summary>
    [HttpGet("promotions")]
    public async Task<IActionResult> GetPromotions([FromQuery] Dictionary<string, string?>? queryParams, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _posterService.GetPromotionsAsync(queryParams, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting promotions");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// POST /api/poster/promotions/revalidate
    /// Revalidate promotions cache
    /// </summary>
    [HttpPost("promotions/revalidate")]
    public async Task<IActionResult> RevalidatePromotions([FromQuery] Dictionary<string, string?>? queryParams, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _posterService.RevalidatePromotionsAsync(queryParams, cancellationToken);
            return Ok(new
            {
                message = "Promotions cache revalidated",
                data = result.Data,
                source = result.Source,
                cached = result.Cached
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revalidating promotions");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ===== CLIENT ENDPOINTS =====

    /// <summary>
    /// GET /api/poster/clients
    /// Get all clients (cached)
    /// </summary>
    [HttpGet("clients")]
    public async Task<IActionResult> GetClients([FromQuery] Dictionary<string, string?>? queryParams, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _posterService.GetClientsAsync(queryParams, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting clients");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// POST /api/poster/clients/revalidate
    /// Revalidate clients cache
    /// </summary>
    [HttpPost("clients/revalidate")]
    public IActionResult RevalidateClients()
    {
        return BadRequest(new { error = "Client revalidation is disabled." });
    }

    /// <summary>
    /// GET /api/poster/clients/:id
    /// Get specific client (cached)
    /// </summary>
    [HttpGet("clients/{id}")]
    public async Task<IActionResult> GetClient(string id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _posterService.GetClientAsync(id, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting client {ClientId}", id);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// POST /api/poster/clients/:id/revalidate
    /// Revalidate specific client cache
    /// </summary>
    [HttpPost("clients/{id}/revalidate")]
    public IActionResult RevalidateClient(string id)
    {
        return BadRequest(new { error = $"Client revalidation is disabled (client: {id})." });
    }

    /// <summary>
    /// GET /api/poster/client-groups
    /// Get client groups (cached)
    /// </summary>
    [HttpGet("client-groups")]
    public async Task<IActionResult> GetClientGroups(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _posterService.GetClientGroupsAsync(cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting client groups");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// POST /api/poster/client-groups/revalidate
    /// Revalidate client groups cache
    /// </summary>
    [HttpPost("client-groups/revalidate")]
    public async Task<IActionResult> RevalidateClientGroups(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _posterService.RevalidateClientGroupsAsync(cancellationToken);
            return Ok(new
            {
                message = "Client groups cache revalidated",
                data = result.Data,
                source = result.Source,
                cached = result.Cached
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revalidating client groups");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// POST /api/poster/clients
    /// Create new client (invalidates client caches)
    /// </summary>
    [HttpPost("clients")]
    public async Task<IActionResult> CreateClient([FromBody] JsonElement payload, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _posterService.CreateClientAsync(payload, cancellationToken);
            return StatusCode(201, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating client");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ===== ORDER ENDPOINTS =====

    /// <summary>
    /// POST /api/poster/orders
    /// Create incoming order (no caching)
    /// </summary>
    [HttpPost("orders")]
    public async Task<IActionResult> CreateIncomingOrder([FromBody] JsonElement payload, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _posterService.CreateIncomingOrderAsync(payload, cancellationToken);
            return StatusCode(201, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating incoming order");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ===== TRANSACTION ENDPOINTS =====

    /// <summary>
    /// GET /api/poster/transactions
    /// Get transactions (cached)
    /// </summary>
    [HttpGet("transactions")]
    public async Task<IActionResult> GetTransactions([FromQuery] Dictionary<string, string?> queryParams, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _posterService.GetTransactionsAsync(queryParams, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting transactions");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// POST /api/poster/transactions/revalidate
    /// Revalidate transactions cache
    /// </summary>
    [HttpPost("transactions/revalidate")]
    public async Task<IActionResult> RevalidateTransactions([FromQuery] Dictionary<string, string?> queryParams, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _posterService.RevalidateTransactionsAsync(queryParams, cancellationToken);
            return Ok(new
            {
                message = "Transactions cache revalidated",
                data = result.Data,
                source = result.Source,
                cached = result.Cached
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revalidating transactions");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// GET /api/poster/transactions/:id
    /// Get specific transaction (cached)
    /// </summary>
    [HttpGet("transactions/{id}")]
    public async Task<IActionResult> GetTransaction(string id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _posterService.GetTransactionAsync(id, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting transaction {TransactionId}", id);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// GET /api/poster/transactions/:id/products
    /// Get transaction products (cached)
    /// </summary>
    [HttpGet("transactions/{id}/products")]
    public async Task<IActionResult> GetTransactionProducts(string id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _posterService.GetTransactionProductsAsync(id, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting transaction products for {TransactionId}", id);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ===== LOCATION ENDPOINTS =====

    /// <summary>
    /// GET /api/poster/spots
    /// Get spots/locations (cached)
    /// </summary>
    [HttpGet("spots")]
    public async Task<IActionResult> GetSpots(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _posterService.GetSpotsAsync(cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting spots");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// POST /api/poster/spots/revalidate
    /// Revalidate spots cache
    /// </summary>
    [HttpPost("spots/revalidate")]
    public async Task<IActionResult> RevalidateSpots(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _posterService.RevalidateSpotsAsync(cancellationToken);
            return Ok(new
            {
                message = "Spots cache revalidated",
                data = result.Data,
                source = result.Source,
                cached = result.Cached
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revalidating spots");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ===== EMPLOYEE ENDPOINTS =====

    /// <summary>
    /// GET /api/poster/employees
    /// Get employees (cached)
    /// </summary>
    [HttpGet("employees")]
    public async Task<IActionResult> GetEmployees(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _posterService.GetEmployeesAsync(cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting employees");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// POST /api/poster/employees/revalidate
    /// Revalidate employees cache
    /// </summary>
    [HttpPost("employees/revalidate")]
    public async Task<IActionResult> RevalidateEmployees(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _posterService.RevalidateEmployeesAsync(cancellationToken);
            return Ok(new
            {
                message = "Employees cache revalidated",
                data = result.Data,
                source = result.Source,
                cached = result.Cached
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revalidating employees");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ===== GLOBAL REVALIDATION =====

    /// <summary>
    /// POST /api/poster/revalidate-all
    /// Revalidate ALL Poster caches
    /// </summary>
    [HttpPost("revalidate-all")]
    public async Task<IActionResult> RevalidateAll(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _posterService.RevalidateAllAsync(cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revalidating all caches");
            return StatusCode(500, new { error = ex.Message });
        }
    }

}
