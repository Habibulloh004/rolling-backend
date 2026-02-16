using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rolling.Infrastructure.Persistence.Postgres;
using Rolling.Web.Auth;

namespace Rolling.Web.Controllers;

[ApiController]
[Route("api/transaction")]
public sealed class TransactionsController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public TransactionsController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> ListAsync([FromQuery] int take = 100, CancellationToken cancellationToken = default)
    {
        var size = Math.Clamp(take, 1, 500);
        var items = await _dbContext.Transactions
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(size)
            .Select(x => new
            {
                x.Id,
                x.TransactionId,
                x.UserId,
                x.Status,
                x.Amount,
                x.OrderId,
                x.CreateTime,
                x.PerformTime,
                x.CancelTime,
                x.Reason,
                x.Provider,
                x.PrepareId,
                x.CreatedAt,
                x.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [AdminAuthorize]
    [HttpGet("/api/admin/transaction")]
    public Task<IActionResult> ListAdminAsync([FromQuery] int take = 100, CancellationToken cancellationToken = default)
    {
        return ListAsync(take, cancellationToken);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetAsync(string id, CancellationToken cancellationToken)
    {
        var transaction = await _dbContext.Transactions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id || x.TransactionId == id, cancellationToken);

        if (transaction is null)
        {
            return NotFound("Transaction not found");
        }

        object? orderDetails = null;
        if (!string.IsNullOrWhiteSpace(transaction.OrderDetailsJson))
        {
            try
            {
                orderDetails = JsonSerializer.Deserialize<object>(transaction.OrderDetailsJson);
            }
            catch (JsonException)
            {
                orderDetails = transaction.OrderDetailsJson;
            }
        }

        return Ok(new
        {
            transaction.Id,
            transaction.TransactionId,
            transaction.UserId,
            orderDetails,
            transaction.Status,
            transaction.Amount,
            transaction.OrderId,
            transaction.CreateTime,
            transaction.PerformTime,
            transaction.CancelTime,
            transaction.Reason,
            transaction.Provider,
            transaction.PrepareId,
            transaction.CreatedAt,
            transaction.UpdatedAt
        });
    }

    [AdminAuthorize]
    [HttpGet("/api/admin/transaction/{id}")]
    public Task<IActionResult> GetAdminAsync(string id, CancellationToken cancellationToken)
    {
        return GetAsync(id, cancellationToken);
    }
}
