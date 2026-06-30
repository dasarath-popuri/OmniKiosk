using Microsoft.Data.Sqlite;
using OmniKiosk.Wpf.Models.MoneyExchange;

namespace OmniKiosk.Wpf.Services.MoneyExchange.Repos
{
    public sealed class TransactionRepository
    {
        private readonly KioskDb _db;
        public TransactionRepository(KioskDb db) => _db = db;

        public long Insert(MoneyExchangeTxn t)
        {
            using var con = _db.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
INSERT INTO MoneyExchangeTxns(CreatedAtUtc,CustomerId,FromCurrency,FromAmount,Rate,MyrAmount,CashInsertedMyr,Status,Notes)
VALUES($cu,$cid,$ccy,$amt,$rate,$myr,$cash,$st,$notes);
SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$cu", t.CreatedAtUtc.ToString("o"));
            cmd.Parameters.AddWithValue("$cid", t.CustomerId);
            cmd.Parameters.AddWithValue("$ccy", t.FromCurrency);
            cmd.Parameters.AddWithValue("$amt", t.FromAmount);
            cmd.Parameters.AddWithValue("$rate", t.Rate);
            cmd.Parameters.AddWithValue("$myr", t.MyrAmount);
            cmd.Parameters.AddWithValue("$cash", t.CashInsertedMyr);
            cmd.Parameters.AddWithValue("$st", t.Status);
            cmd.Parameters.AddWithValue("$notes", (object?)t.Notes ?? System.DBNull.Value);
            return System.Convert.ToInt64(cmd.ExecuteScalar());
        }
    }
}
