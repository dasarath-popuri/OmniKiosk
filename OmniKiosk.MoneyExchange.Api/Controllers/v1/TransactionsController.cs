using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using OmniKiosk.MoneyExchange.Api.Models.v1;
using System.Data;

namespace OmniKiosk.MoneyExchange.Api.Controllers.v1
{
    [ApiController]
    [Route("api/v1/[controller]")]
    [Authorize(Policy = "KioskOnly")]
    public class TransactionsController : ControllerBase
    {
        private readonly string _connectionString;
        private readonly ILogger<TransactionsController> _logger;

        public TransactionsController(IConfiguration config, ILogger<TransactionsController> logger)
        {
            _connectionString = config.GetConnectionString("PhantomRemit")
                ?? throw new InvalidOperationException("ConnectionStrings:PhantomRemit is not configured.");
            _logger = logger;
        }

        // POST /api/v1/Transactions - KSK_CreateMoneyExchangeTransaction,
        // which now generates the receipt number itself (KSK_GetReceiptNo)
        // instead of trusting whatever the kiosk sent.
        [HttpPost]
        public async Task<ActionResult<CreateTransactionResponse>> CreateTransaction([FromBody] CreateTransactionRequest request)
        {
            using var con = new SqlConnection(_connectionString);

            var p = new DynamicParameters();
            p.Add("@KioskId", request.KioskId);
            p.Add("@BranchId", request.BranchId);
            p.Add("@CustomerRef", request.CustomerRef);
            p.Add("@FromCurrency", request.FromCurrency);
            p.Add("@FromAmount", request.FromAmount);
            p.Add("@Rate", request.Rate);
            p.Add("@MyrAmount", request.MyrAmount);
            p.Add("@CashInsertedMyr", request.CashInsertedMyr);
            p.Add("@CreatedBy", request.CreatedBy);
            p.Add("@ScreeningTransGuid", request.ScreeningTransGuid);
            p.Add("@GeneratedReceiptNo", dbType: DbType.String, size: 20, direction: ParameterDirection.Output);
            p.Add("@NewTransactionId", dbType: DbType.Int64, direction: ParameterDirection.Output);

            try
            {
                await con.ExecuteAsync("KSK_CreateMoneyExchangeTransaction", p, commandType: CommandType.StoredProcedure);
                var newId = p.Get<long>("@NewTransactionId");
                var receiptNo = p.Get<string>("@GeneratedReceiptNo");

                _logger.LogInformation(
                    "Transaction {TransactionId} created - Kiosk {KioskId}, {FromAmount} {FromCurrency} -> RM {MyrAmount}, Receipt {ReceiptNo}",
                    newId, request.KioskId, request.FromAmount, request.FromCurrency, request.MyrAmount, receiptNo);

                return Ok(new CreateTransactionResponse { TransactionId = newId, ReceiptNo = receiptNo });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create transaction for Kiosk {KioskId}", request.KioskId);
                return StatusCode(500, new { error = "Could not save the transaction. Nothing was recorded." });
            }
        }

        // PUT /api/v1/Transactions/{id}/complete - KSK_CompleteMoneyExchangeTransaction,
        // which also triggers the Mc_ mirror and screening commit automatically
        // when Status = Completed.
        [HttpPut("{transactionId}/complete")]
        public async Task<IActionResult> CompleteTransaction(long transactionId, [FromBody] CompleteTransactionRequest request)
        {
            using var con = new SqlConnection(_connectionString);

            try
            {
                await con.ExecuteAsync("KSK_CompleteMoneyExchangeTransaction",
                    new { TransactionId = transactionId, Status = request.Status },
                    commandType: CommandType.StoredProcedure);

                _logger.LogInformation("Transaction {TransactionId} marked {Status}", transactionId, request.Status);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update Transaction {TransactionId} to {Status}", transactionId, request.Status);
                return StatusCode(500, new { error = "Could not update the transaction status." });
            }
        }

        // PUT /api/v1/Transactions/{id}/amounts - pushes the final running
        // totals (foreign inserted, MYR payable) to the transaction record.
        // Must be called before /complete, since KSK_MirrorToMcTransaction
        // reads these same columns to build the Mc_TransMaster/Mc_Transaction
        // rows - without this call, the transaction row (and everything
        // mirrored from it) stays at the 0 values it was created with.
        [HttpPut("{transactionId}/amounts")]
        public async Task<IActionResult> UpdateAmounts(long transactionId, [FromBody] UpdateAmountsRequest request)
        {
            using var con = new SqlConnection(_connectionString);

            try
            {
                await con.ExecuteAsync("KSK_UpdateTransactionAmounts",
                    new { TransactionId = transactionId, request.FromAmount, request.MyrAmount, request.CashInsertedMyr },
                    commandType: CommandType.StoredProcedure);

                _logger.LogInformation("Transaction {TransactionId} amounts updated - {FromAmount} {MyrAmount} MYR", transactionId, request.FromAmount, request.MyrAmount);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update amounts for Transaction {TransactionId}", transactionId);
                return StatusCode(500, new { error = "Could not update the transaction amounts." });
            }
        }

        // POST /api/v1/Transactions/{id}/notes - KSK_RecordTransactionNote.
        [HttpPost("{transactionId}/notes")]
        public async Task<IActionResult> RecordNote(long transactionId, [FromBody] RecordNoteRequest request)
        {
            using var con = new SqlConnection(_connectionString);

            try
            {
                await con.ExecuteAsync("KSK_RecordTransactionNote",
                    new
                    {
                        TransactionId = transactionId,
                        request.SequenceNo,
                        request.CurrencyCode,
                        request.DenominationValue,
                        request.Outcome
                    },
                    commandType: CommandType.StoredProcedure);

                _logger.LogInformation(
                    "Transaction {TransactionId} note #{SequenceNo}: {DenominationValue} {CurrencyCode} - {Outcome}",
                    transactionId, request.SequenceNo, request.DenominationValue, request.CurrencyCode, request.Outcome);

                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to record note #{SequenceNo} for Transaction {TransactionId}", request.SequenceNo, transactionId);
                return StatusCode(500, new { error = "Could not record this note. The physical note decision already happened - only the log entry failed." });
            }
        }
        // GET /api/v1/Transactions/check-per-transaction-limit -
        // KSK_CheckPerTransactionLimitOnly. Deliberately ID-agnostic - used
        // at the currency selection screen, before any customer has been
        // identified. See CheckLimits below for the full check (per-
        // transaction + daily + monthly), which needs a real SenderId.
        [HttpGet("check-per-transaction-limit")]
        public async Task<ActionResult<CheckPerTransactionLimitResponse>> CheckPerTransactionLimit(
            [FromQuery] string kioskId, [FromQuery] decimal proposedMyrAmount)
        {
            using var con = new SqlConnection(_connectionString);

            var p = new DynamicParameters();
            p.Add("@KioskId", kioskId);
            p.Add("@ProposedMyrAmount", proposedMyrAmount);
            p.Add("@IsWithinLimits", dbType: DbType.Boolean, direction: ParameterDirection.Output);
            p.Add("@PerTxnLimit", dbType: DbType.Decimal, direction: ParameterDirection.Output);

            try
            {
                await con.ExecuteAsync("KSK_CheckPerTransactionLimitOnly", p, commandType: CommandType.StoredProcedure);

                var result = new CheckPerTransactionLimitResponse
                {
                    IsWithinLimits = p.Get<bool>("@IsWithinLimits"),
                    PerTxnLimit = p.Get<decimal>("@PerTxnLimit")
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Per-transaction limit check failed for KioskId {KioskId}", kioskId);
                return StatusCode(500, new { error = "Could not check the per-transaction limit." });
            }
        }

        // GET /api/v1/Transactions/check-limits - KSK_CheckMoneyExchangeLimits.
        // Read-only, no transaction needs to exist yet - this is checked
        // BEFORE and DURING cash-in (see CashInStep.xaml.cs), not against
        // an already-created transaction record.
        [HttpGet("check-limits")]
        public async Task<ActionResult<CheckLimitsResponse>> CheckLimits(
            [FromQuery] int senderId, [FromQuery] string kioskId, [FromQuery] decimal proposedMyrAmount)
        {
            using var con = new SqlConnection(_connectionString);

            var p = new DynamicParameters();
            p.Add("@SenderId", senderId);
            p.Add("@KioskId", kioskId);
            p.Add("@ProposedMyrAmount", proposedMyrAmount);
            p.Add("@IsWithinLimits", dbType: DbType.Boolean, direction: ParameterDirection.Output);
            p.Add("@BreachedLimit", dbType: DbType.String, size: 20, direction: ParameterDirection.Output);
            p.Add("@DailyTotalSoFar", dbType: DbType.Decimal, direction: ParameterDirection.Output);
            p.Add("@Rolling30DayTotal", dbType: DbType.Decimal, direction: ParameterDirection.Output);

            try
            {
                await con.ExecuteAsync("KSK_CheckMoneyExchangeLimits", p, commandType: CommandType.StoredProcedure);

                var result = new CheckLimitsResponse
                {
                    IsWithinLimits = p.Get<bool>("@IsWithinLimits"),
                    BreachedLimit = p.Get<string?>("@BreachedLimit"),
                    DailyTotalSoFar = p.Get<decimal>("@DailyTotalSoFar"),
                    Rolling30DayTotal = p.Get<decimal>("@Rolling30DayTotal")
                };

                _logger.LogInformation(
                    "Limit check - SenderId {SenderId}, KioskId {KioskId}, Proposed RM{Proposed}: {Result}",
                    senderId, kioskId, proposedMyrAmount, result.IsWithinLimits ? "within limits" : $"BREACHED ({result.BreachedLimit})");

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Limit check failed for SenderId {SenderId}, KioskId {KioskId}", senderId, kioskId);
                return StatusCode(500, new { error = "Could not check transaction limits." });
            }
        }
        // GET /api/v1/Transactions/check-per-txn-limit -
        // KSK_CheckPerTransactionLimitOnly. Deliberately narrower than
        // check-limits above - for CurrencySelectionStep, before any
        // customer identity is known, so only the flat per-transaction cap
        // can be meaningfully checked (no SenderId to look up daily/monthly
        // history against yet).
        [HttpGet("check-per-txn-limit")]
        public async Task<ActionResult<CheckPerTxnLimitResponse>> CheckPerTransactionLimitOnly(
            [FromQuery] string kioskId, [FromQuery] decimal proposedMyrAmount)
        {
            using var con = new SqlConnection(_connectionString);

            var p = new DynamicParameters();
            p.Add("@KioskId", kioskId);
            p.Add("@ProposedMyrAmount", proposedMyrAmount);
            p.Add("@IsWithinLimit", dbType: DbType.Boolean, direction: ParameterDirection.Output);
            p.Add("@PerTxnLimit", dbType: DbType.Decimal, direction: ParameterDirection.Output);

            try
            {
                await con.ExecuteAsync("KSK_CheckPerTransactionLimitOnly", p, commandType: CommandType.StoredProcedure);

                var result = new CheckPerTxnLimitResponse
                {
                    IsWithinLimit = p.Get<bool>("@IsWithinLimit"),
                    PerTxnLimit = p.Get<decimal>("@PerTxnLimit")
                };

                _logger.LogInformation(
                    "Per-transaction limit check - KioskId {KioskId}, Proposed RM{Proposed}: {Result}",
                    kioskId, proposedMyrAmount, result.IsWithinLimit ? "within limit" : "BREACHED");

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Per-transaction limit check failed for KioskId {KioskId}", kioskId);
                return StatusCode(500, new { error = "Could not check the per-transaction limit." });
            }
        }
    }
}