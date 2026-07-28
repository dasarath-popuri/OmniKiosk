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

        // POST /api/v1/Transactions - KSK_CreateMoneyExchangeTransaction.
        [HttpPost]
        public async Task<ActionResult<CreateTransactionResponse>> CreateTransaction([FromBody] CreateTransactionRequest request)
        {
            using var con = new SqlConnection(_connectionString);

            var p = new DynamicParameters();
            p.Add("@KioskId", request.KioskId);
            p.Add("@ReceiptNo", request.ReceiptNo);
            p.Add("@CustomerRef", request.CustomerRef);
            p.Add("@FromCurrency", request.FromCurrency);
            p.Add("@FromAmount", request.FromAmount);
            p.Add("@Rate", request.Rate);
            p.Add("@MyrAmount", request.MyrAmount);
            p.Add("@CashInsertedMyr", request.CashInsertedMyr);
            p.Add("@CreatedBy", request.CreatedBy);
            p.Add("@ScreeningTransGuid", request.ScreeningTransGuid);
            p.Add("@NewTransactionId", dbType: DbType.Int64, direction: ParameterDirection.Output);

            try
            {
                await con.ExecuteAsync("KSK_CreateMoneyExchangeTransaction", p, commandType: CommandType.StoredProcedure);
                var newId = p.Get<long>("@NewTransactionId");

                _logger.LogInformation(
                    "Transaction {TransactionId} created - Kiosk {KioskId}, {FromAmount} {FromCurrency} -> RM {MyrAmount}, Receipt {ReceiptNo}",
                    newId, request.KioskId, request.FromAmount, request.FromCurrency, request.MyrAmount, request.ReceiptNo);

                return Ok(new CreateTransactionResponse { TransactionId = newId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create transaction for Kiosk {KioskId}, Receipt {ReceiptNo}", request.KioskId, request.ReceiptNo);
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
    }
}
