using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Rolling.Infrastructure.Payments;

namespace Rolling.Web.Controllers;

[ApiController]
[Route("api/plum")]
public sealed class PlumPaymentsController : ControllerBase
{
    private readonly PlumPaymentService _plumPaymentService;
    private readonly ILogger<PlumPaymentsController> _logger;

    public PlumPaymentsController(PlumPaymentService plumPaymentService, ILogger<PlumPaymentsController> logger)
    {
        _plumPaymentService = plumPaymentService;
        _logger = logger;
    }

    // ===== BranchEpos =====

    [HttpGet("branch-epos/branches")]
    public Task<IActionResult> GetClientBranchesAsync([FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardGetAsync("branchEpos/GetClientBranches", null, language, cancellationToken);

    [HttpGet("branch-epos")]
    public Task<IActionResult> GetBranchEposAsync([FromQuery] string branchId, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardGetAsync("branchEpos/GetBranchEpos", BuildQuery(("branchId", branchId)), language, cancellationToken);

    // ===== Cards =====

    [HttpPost("cards")]
    public Task<IActionResult> CreateUserCardAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("UserCard/createUserCard", payload, language, cancellationToken);

    [HttpPost("cards/confirm")]
    public Task<IActionResult> ConfirmUserCardCreateAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("UserCard/confirmUserCardCreate", payload, language, cancellationToken);

    [HttpGet("cards/otp")]
    public Task<IActionResult> ResendOtpAsync([FromQuery] string session, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardGetAsync("UserCard/resendOtp", BuildQuery(("session", session)), language, cancellationToken);

    [HttpGet("cards/user")]
    public Task<IActionResult> GetAllUserCardsAsync([FromQuery] string userId, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardGetAsync("UserCard/getAllUserCards", BuildQuery(("userId", userId)), language, cancellationToken);

    [HttpDelete("cards")]
    public Task<IActionResult> DeleteUserCardAsync([FromQuery] string userCardId, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardDeleteAsync("UserCard/deleteUserCard", BuildQuery(("userCardId", userCardId)), language, cancellationToken);

    [HttpPost("cards/check-pinfl")]
    public Task<IActionResult> CheckCardPinflAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("UserCard/CheckCardPinfl", payload, language, cancellationToken);

    [HttpPost("cards/owner-info")]
    public Task<IActionResult> GetCardOwnerInfoAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("UserCard/getCardOwnerInfoByPan", payload, language, cancellationToken);

    [HttpGet("cards/info")]
    public Task<IActionResult> GetUserCardInfoAsync(
        [FromQuery] string userId,
        [FromQuery] string cardId,
        [FromQuery] string? language,
        CancellationToken cancellationToken) =>
        ForwardGetAsync("UserCard/getUserCardInfo", BuildQuery(("userId", userId), ("cardId", cardId)), language, cancellationToken);

    [HttpPost("cards/balances")]
    public Task<IActionResult> GetUserCardsBalancesAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("UserCard/getUserCardsBalances", payload, language, cancellationToken);

    [HttpPost("cards/balances/by-id")]
    public Task<IActionResult> GetUserCardsBalancesByIdAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("UserCard/getUserCardsBalancesById", payload, language, cancellationToken);

    // ===== Payment =====

    [HttpPost("pay")]
    public Task<IActionResult> PayAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("Payment/paymentWithoutRegistration", payload, language, cancellationToken);

    [HttpPost("pay_confirm")]
    public Task<IActionResult> PayConfirmAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("Payment/confirmPayment", payload, language, cancellationToken);

    [HttpPost("payments/without-registration")]
    public Task<IActionResult> PaymentWithoutRegistrationAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("Payment/paymentWithoutRegistration", payload, language, cancellationToken);

    [HttpPost("payments")]
    public Task<IActionResult> PaymentAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("Payment/payment", payload, language, cancellationToken);

    [HttpPost("payments/confirm")]
    public Task<IActionResult> ConfirmPaymentAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("Payment/confirmPayment", payload, language, cancellationToken);

    [HttpPost("payments/reverse")]
    public Task<IActionResult> PaymentReverseAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("Payment/paymentReverse", payload, language, cancellationToken);

    [HttpPost("payments/reverse/partial")]
    public Task<IActionResult> ReversePartialAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("Payment/reversePartial", payload, language, cancellationToken);

    [HttpGet("payments/by-extra")]
    public Task<IActionResult> GetTransactionByExtraAsync([FromQuery] string extraId, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardGetAsync("Payment/getTransactionByExt", BuildQuery(("extraId", extraId)), language, cancellationToken);

    [HttpPost("payments/search")]
    public Task<IActionResult> GetTransactionsAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("Payment/getTransactions", payload, language, cancellationToken);

    // ===== Payment Hold =====

    [HttpPost("hold/create")]
    public Task<IActionResult> CreateHoldAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("Hold/CreateHold", payload, language, cancellationToken);

    [HttpPost("hold/confirm")]
    public Task<IActionResult> ConfirmCreateHoldAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("Hold/ConfirmCreateHold", payload, language, cancellationToken);

    [HttpPost("hold/dismiss")]
    public Task<IActionResult> DismissHeldTransactionAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("Hold/DismissHeldTransaction", payload, language, cancellationToken);

    [HttpPost("hold/charge")]
    public Task<IActionResult> ChargeHoldAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("Hold/ChargeHold", payload, language, cancellationToken);

    // ===== Fiscal =====

    [HttpPost("fiscal/register")]
    public Task<IActionResult> RegisterFiscalAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("Fiscal/RegisterFiscal", payload, language, cancellationToken);

    // ===== AutoPayment =====

    [HttpPost("auto-payment/client")]
    public Task<IActionResult> CreateAutoPaymentClientAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("AutoPayment/CreateClient", payload, language, cancellationToken);

    [HttpPost("auto-payment/contract/with-otp")]
    public Task<IActionResult> CreateContractWithOtpAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("AutoPayment/CreateContractWithOtp", payload, language, cancellationToken);

    [HttpPost("auto-payment/contract/confirm")]
    public Task<IActionResult> ConfirmCreatedContractAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("AutoPayment/ConfirmCreatedContract", payload, language, cancellationToken);

    [HttpPost("auto-payment/contract/payment")]
    public Task<IActionResult> ContractPaymentAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("AutoPayment/ContractPayment", payload, language, cancellationToken);

    [HttpGet("auto-payment/contract/payment-status")]
    public Task<IActionResult> ContractPaymentCompletionStatusAsync([FromQuery] string extraId, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardGetAsync("AutoPayment/ContractPaymentCompletionStatus", BuildQuery(("extraId", extraId)), language, cancellationToken);

    [HttpPost("auto-payment/contract/payment/reverse")]
    public Task<IActionResult> ContractPaymentReverseAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("AutoPayment/ContractPaymentReverse", payload, language, cancellationToken);

    [HttpPost("auto-payment/contract/payment/full")]
    public Task<IActionResult> ContractFullPaymentAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("AutoPayment/ContractFullPayment", payload, language, cancellationToken);

    [HttpGet("auto-payment/client/uzcard-refresh")]
    public Task<IActionResult> UpdateClientUzcardCardsAsync([FromQuery] string autoPayClientId, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardGetAsync("AutoPayment/UpdateClientUzcardCards", BuildQuery(("autoPayClientId", autoPayClientId)), language, cancellationToken);

    [HttpGet("auto-payment/cards/update-status")]
    public Task<IActionResult> CheckUpdateCardsAsync([FromQuery] string jobId, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardGetAsync("AutoPayment/CheckUpdateCards", BuildQuery(("jobId", jobId)), language, cancellationToken);

    [HttpGet("auto-payment/contract/cancel")]
    public Task<IActionResult> CancelContractAsync([FromQuery] string autoPayContractId, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardGetAsync("AutoPayment/CancelContract", BuildQuery(("autoPayContractId", autoPayContractId)), language, cancellationToken);

    [HttpGet("auto-payment/client")]
    public Task<IActionResult> GetClientAsync([FromQuery] string userId, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardGetAsync("AutoPayment/GetClient", BuildQuery(("userId", userId)), language, cancellationToken);

    [HttpGet("auto-payment/contract")]
    public Task<IActionResult> GetContractAsync([FromQuery] string autoPayContractId, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardGetAsync("AutoPayment/GetContract", BuildQuery(("autoPayContractId", autoPayContractId)), language, cancellationToken);

    [HttpGet("auto-payment/contract/transactions")]
    public Task<IActionResult> GetContractTransactionsAsync([FromQuery] string autoPayContractId, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardGetAsync("AutoPayment/GetContractTransactions", BuildQuery(("autoPayContractId", autoPayContractId)), language, cancellationToken);

    [HttpGet("auto-payment/contracts")]
    public Task<IActionResult> GetContractsAsync([FromQuery] string userId, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardGetAsync("AutoPayment/GetContracts", BuildQuery(("userId", userId)), language, cancellationToken);

    [HttpGet("auto-payment/client/humo-refresh")]
    public Task<IActionResult> UpdateClientHumoCardsAsync([FromQuery] string autoPayClientId, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardGetAsync("AutoPayment/UpdateClientHumoCards", BuildQuery(("autoPayClientId", autoPayClientId)), language, cancellationToken);

    // ===== Scoring =====

    [HttpPost("scoring/confirm")]
    public Task<IActionResult> ConfirmCreateScoringAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("Scoring/ConfirmCreateScoring", payload, language, cancellationToken);

    [HttpPost("scoring/humo/by-card")]
    public Task<IActionResult> CreateHumoScoringCardsByPersonCodeByCardAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("Scoring/CreateHumoScoringCardsByPersonCodeByCard", payload, language, cancellationToken);

    [HttpPost("scoring/humo/by-card-summary")]
    public Task<IActionResult> CreateHumoScoringCardsByPersonCodeByCardSummaryAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("Scoring/CreateHumoScoringCardsByPersonCodeByCardSummary", payload, language, cancellationToken);

    [HttpPost("scoring/humo/summary")]
    public Task<IActionResult> CreateHumoScoringCardsByPersonCodeSummaryAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("Scoring/CreateHumoScoringCardsByPersonCodeSummary", payload, language, cancellationToken);

    [HttpPost("scoring/humo/total-summary")]
    public Task<IActionResult> CreateHumoScoringCardsByPersonCodeTotalSummaryAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("Scoring/CreateHumoScoringCardsByPersonCodeTotalSummary", payload, language, cancellationToken);

    [HttpPost("scoring/by-card")]
    public Task<IActionResult> CreateScoringByPersonCodeByCardAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("Scoring/CreateScoringByPersonCodeByCard", payload, language, cancellationToken);

    [HttpPost("scoring/summary")]
    public Task<IActionResult> CreateScoringByPersonCodeSummaryAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("Scoring/CreateScoringByPersonCodeSummary", payload, language, cancellationToken);

    [HttpGet("scoring/eb-debt")]
    public Task<IActionResult> GetEbDebtScoreAsync(
        [FromQuery] string pinfl,
        [FromQuery] string phone,
        [FromQuery] string? language,
        CancellationToken cancellationToken) =>
        ForwardGetAsync("Scoring/GetEbDebtScore", BuildQuery(("pinfl", pinfl), ("phone", phone)), language, cancellationToken);

    [HttpGet("scoring/humo/by-card")]
    public Task<IActionResult> GetHumoScoringCardsByPersonCodeByCardAsync([FromQuery] string scoringId, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardGetAsync("Scoring/GetHumoScoringCardsByPersonCodeByCard", BuildQuery(("scoringId", scoringId)), language, cancellationToken);

    [HttpGet("scoring/humo/by-card-summary")]
    public Task<IActionResult> GetHumoScoringCardsByPersonCodeByCardSummaryAsync([FromQuery] string scoringId, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardGetAsync("Scoring/GetHumoScoringCardsByPersonCodeByCardSummary", BuildQuery(("scoringId", scoringId)), language, cancellationToken);

    [HttpGet("scoring/humo/summary")]
    public Task<IActionResult> GetHumoScoringCardsByPersonCodeSummaryAsync([FromQuery] string scoringId, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardGetAsync("Scoring/GetHumoScoringCardsByPersonCodeSummary", BuildQuery(("scoringId", scoringId)), language, cancellationToken);

    [HttpGet("scoring/humo/total-summary")]
    public Task<IActionResult> GetHumoScoringCardsByPersonCodeTotalSummaryAsync([FromQuery] string scoringId, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardGetAsync("Scoring/GetHumoScoringCardsByPersonCodeTotalSummary", BuildQuery(("scoringId", scoringId)), language, cancellationToken);

    [HttpGet("scoring/by-card/{scoringId}")]
    public Task<IActionResult> GetScoringByPersonCodeByCardAsync([FromRoute] string scoringId, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardGetAsync("Scoring/GetScoringByPersonCodeByCard", BuildQuery(("scoringId", scoringId)), language, cancellationToken);

    [HttpGet("scoring/summary/{scoringId}")]
    public Task<IActionResult> GetScoringByPersonCodeSummaryAsync([FromRoute] string scoringId, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardGetAsync("Scoring/GetScoringByPersonCodeSummary", BuildQuery(("scoringId", scoringId)), language, cancellationToken);

    [HttpGet("scoring/tax-debt")]
    public Task<IActionResult> GetTaxDebtScoreAsync([FromQuery] string pinfl, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardGetAsync("Scoring/GetTaxDebtScore", BuildQuery(("pinfl", pinfl)), language, cancellationToken);

    [HttpPost("scoring/humo")]
    public Task<IActionResult> HumoScoringAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("Scoring/HumoScoring", payload, language, cancellationToken);

    [HttpPost("scoring/humo/avg")]
    public Task<IActionResult> HumoScoringAvgAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("Scoring/HumoScoringAvg", payload, language, cancellationToken);

    [HttpPost("scoring/humo/month")]
    public Task<IActionResult> HumoScoringMonthAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("Scoring/HumoScoringMonth", payload, language, cancellationToken);

    [HttpPost("scoring/amount")]
    public Task<IActionResult> CreateScoringAmountAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("Scoring/createScoringAmount", payload, language, cancellationToken);

    [HttpPost("scoring/card")]
    public Task<IActionResult> CreateScoringCardAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("Scoring/createScoringCard", payload, language, cancellationToken);

    [HttpGet("scoring/month")]
    public Task<IActionResult> ScoringGetMonthAsync([FromQuery] string scoringId, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardGetAsync("Scoring/scoringGetMonth", BuildQuery(("scoringId", scoringId)), language, cancellationToken);

    [HttpGet("scoring/point")]
    public Task<IActionResult> ScoringGetPointAsync([FromQuery] string scoringId, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardGetAsync("Scoring/scoringGetPoint", BuildQuery(("scoringId", scoringId)), language, cancellationToken);

    // ===== Credit =====

    [HttpPost("credit")]
    public Task<IActionResult> CreditAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("Credit/Credit", payload, language, cancellationToken);

    [HttpPost("credit/self-employed")]
    public Task<IActionResult> CreditToSelfEmployedAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("Credit/CreditToSelfEmployed", payload, language, cancellationToken);

    [HttpGet("credit/by-data")]
    public Task<IActionResult> GetCreditTransactionByDataAsync(
        [FromQuery] string transactionId,
        [FromQuery] string extraId,
        [FromQuery] string? language,
        CancellationToken cancellationToken) =>
        ForwardGetAsync("Credit/GetCreditTransactionByData", BuildQuery(("transactionId", transactionId), ("extraId", extraId)), language, cancellationToken);

    [HttpPost("credit/search")]
    public Task<IActionResult> GetCreditTransactionsAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("Credit/GetCreditTransactions", payload, language, cancellationToken);

    [HttpPost("credit/search/excel")]
    public Task<IActionResult> GetCreditTransactionsExcelAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("Credit/GetCreditTransactionsExcel", payload, language, cancellationToken);

    [HttpPost("credit/reverse")]
    public Task<IActionResult> ReverseCreditAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("Credit/ReverseCredit", payload, language, cancellationToken);

    [HttpPost("credit/card-owner")]
    public Task<IActionResult> GetCreditCardOwnerInfoAsync([FromBody] JsonElement payload, [FromQuery] string? language, CancellationToken cancellationToken) =>
        ForwardPostAsync("Credit/getCardOwnerInfoByPan", payload, language, cancellationToken);

    private Task<IActionResult> ForwardGetAsync(
        string path,
        Dictionary<string, string?>? query,
        string? language,
        CancellationToken cancellationToken) =>
        ForwardAsync(HttpMethod.Get, path, query, null, language, cancellationToken);

    private Task<IActionResult> ForwardPostAsync(
        string path,
        JsonElement? payload,
        string? language,
        CancellationToken cancellationToken) =>
        ForwardAsync(HttpMethod.Post, path, null, payload, language, cancellationToken);

    private Task<IActionResult> ForwardDeleteAsync(
        string path,
        Dictionary<string, string?>? query,
        string? language,
        CancellationToken cancellationToken) =>
        ForwardAsync(HttpMethod.Delete, path, query, null, language, cancellationToken);

    private async Task<IActionResult> ForwardAsync(
        HttpMethod method,
        string path,
        Dictionary<string, string?>? query,
        JsonElement? payload,
        string? language,
        CancellationToken cancellationToken)
    {
        var requestBody = payload.HasValue ? payload.Value.GetRawText() : "<empty>";
        _logger.LogInformation(
            "PLUM forward {Method} {Path} lang={Language} body={Body}",
            method,
            path,
            language ?? _plumPaymentService.DefaultLanguage,
            Truncate(requestBody, 500));
        Console.WriteLine($"PLUM forward {method} {path} lang={language ?? _plumPaymentService.DefaultLanguage} body={Truncate(requestBody, 500)}");

        var result = await _plumPaymentService.SendAsync(method, path, query, payload, language, cancellationToken);

        var responsePreview = IsTextContent(result.ContentType)
            ? Truncate(Encoding.UTF8.GetString(result.Body), 500)
            : "<binary>";

        _logger.LogInformation(
            "PLUM response {StatusCode} {Path} type={ContentType} preview={Preview}",
            result.StatusCode,
            path,
            result.ContentType,
            responsePreview);
        Console.WriteLine($"PLUM response {result.StatusCode} {path} type={result.ContentType} preview={responsePreview}");

        return ToActionResult(result);
    }

    private IActionResult ToActionResult(PlumForwardResult result)
    {
        var body = result.Body ?? Array.Empty<byte>();
        var contentType = string.IsNullOrWhiteSpace(result.ContentType) ? "application/json" : result.ContentType;
        var isText = IsTextContent(contentType);

        if (isText)
        {
            return new ContentResult
            {
                StatusCode = result.StatusCode,
                ContentType = contentType,
                Content = Encoding.UTF8.GetString(body)
            };
        }

        Response.StatusCode = result.StatusCode;
        var downloadName = contentType.Contains("excel", StringComparison.OrdinalIgnoreCase)
            ? "plum-export.xlsx"
            : "plum-response";

        return File(body, contentType, downloadName);
    }

    private static Dictionary<string, string?> BuildQuery(params (string Key, string? Value)[] values)
    {
        var dict = new Dictionary<string, string?>();
        foreach (var (key, value) in values)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            dict[key] = value;
        }

        return dict;
    }

    private static string Truncate(string value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength] + "...";

    private static bool IsTextContent(string contentType) =>
        contentType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
        contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase);
}
