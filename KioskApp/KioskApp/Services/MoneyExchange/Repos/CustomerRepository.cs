using System;
using Microsoft.Data.Sqlite;
using OmniKiosk.Wpf.Models.MoneyExchange;

namespace OmniKiosk.Wpf.Services.MoneyExchange.Repos
{
    public sealed class CustomerRepository
    {
        private readonly KioskDb _db;
        public CustomerRepository(KioskDb db) => _db = db;

        public CustomerProfile? GetByIdNo(string idType, string idNo)
        {
            using var con = _db.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"SELECT Id,IdType,IdNo,FullName,Nationality,Sex,DateOfBirth,MobileNo,FaceFeatureBase64,FaceImageBase64,CreatedAtUtc,UpdatedAtUtc,LastSeenUtc
                                FROM Customers WHERE IdType=$t AND IdNo=$n LIMIT 1";
            cmd.Parameters.AddWithValue("$t", idType);
            cmd.Parameters.AddWithValue("$n", idNo);

            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;

            return new CustomerProfile
            {
                Id = r.GetInt64(0),
                IdType = r.GetString(1),
                IdNo = r.GetString(2),
                FullName = r.IsDBNull(3) ? "" : r.GetString(3),
                Nationality = r.IsDBNull(4) ? "" : r.GetString(4),
                Sex = r.IsDBNull(5) ? "" : r.GetString(5),
                DateOfBirth = r.IsDBNull(6) ? "" : r.GetString(6),
                MobileNo = r.IsDBNull(7) ? "" : r.GetString(7),
                FaceFeatureBase64 = r.IsDBNull(8) ? null : r.GetString(8),
                FaceImageBase64 = r.IsDBNull(9) ? null : r.GetString(9),
                CreatedAtUtc = DateTime.Parse(r.GetString(10)),
                UpdatedAtUtc = DateTime.Parse(r.GetString(11)),
                LastSeenUtc = r.IsDBNull(12) ? null : DateTime.Parse(r.GetString(12)),
            };
        }

        public CustomerProfile Upsert(CustomerProfile c)
        {
            c.UpdatedAtUtc = DateTime.UtcNow;

            using var con = _db.Open();
            using var tx = con.BeginTransaction();

            // try get existing
            long? existingId = null;
            using (var check = con.CreateCommand())
            {
                check.Transaction = tx;
                check.CommandText = @"SELECT Id FROM Customers WHERE IdType=$t AND IdNo=$n LIMIT 1";
                check.Parameters.AddWithValue("$t", c.IdType);
                check.Parameters.AddWithValue("$n", c.IdNo);
                var obj = check.ExecuteScalar();
                if (obj != null && obj != DBNull.Value) existingId = Convert.ToInt64(obj);
            }

            if (existingId.HasValue)
            {
                c.Id = existingId.Value;
                using var upd = con.CreateCommand();
                upd.Transaction = tx;
                upd.CommandText = @"
UPDATE Customers SET
  FullName=$fn, Nationality=$nat, Sex=$sex, DateOfBirth=$dob, MobileNo=$mob,
  FaceFeatureBase64=$ff, FaceImageBase64=$fi,
  UpdatedAtUtc=$uu, LastSeenUtc=$ls
WHERE Id=$id";
                upd.Parameters.AddWithValue("$fn", c.FullName ?? "");
                upd.Parameters.AddWithValue("$nat", c.Nationality ?? "");
                upd.Parameters.AddWithValue("$sex", c.Sex ?? "");
                upd.Parameters.AddWithValue("$dob", c.DateOfBirth ?? "");
                upd.Parameters.AddWithValue("$mob", c.MobileNo ?? "");
                upd.Parameters.AddWithValue("$ff", (object?)c.FaceFeatureBase64 ?? DBNull.Value);
                upd.Parameters.AddWithValue("$fi", (object?)c.FaceImageBase64 ?? DBNull.Value);
                upd.Parameters.AddWithValue("$uu", c.UpdatedAtUtc.ToString("o"));
                upd.Parameters.AddWithValue("$ls", (object?)(c.LastSeenUtc?.ToString("o")) ?? DBNull.Value);
                upd.Parameters.AddWithValue("$id", c.Id);
                upd.ExecuteNonQuery();
            }
            else
            {
                c.CreatedAtUtc = DateTime.UtcNow;
                using var ins = con.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = @"
INSERT INTO Customers(IdType,IdNo,FullName,Nationality,Sex,DateOfBirth,MobileNo,FaceFeatureBase64,FaceImageBase64,CreatedAtUtc,UpdatedAtUtc,LastSeenUtc)
VALUES($t,$n,$fn,$nat,$sex,$dob,$mob,$ff,$fi,$cu,$uu,$ls);
SELECT last_insert_rowid();";
                ins.Parameters.AddWithValue("$t", c.IdType);
                ins.Parameters.AddWithValue("$n", c.IdNo);
                ins.Parameters.AddWithValue("$fn", c.FullName ?? "");
                ins.Parameters.AddWithValue("$nat", c.Nationality ?? "");
                ins.Parameters.AddWithValue("$sex", c.Sex ?? "");
                ins.Parameters.AddWithValue("$dob", c.DateOfBirth ?? "");
                ins.Parameters.AddWithValue("$mob", c.MobileNo ?? "");
                ins.Parameters.AddWithValue("$ff", (object?)c.FaceFeatureBase64 ?? DBNull.Value);
                ins.Parameters.AddWithValue("$fi", (object?)c.FaceImageBase64 ?? DBNull.Value);
                ins.Parameters.AddWithValue("$cu", c.CreatedAtUtc.ToString("o"));
                ins.Parameters.AddWithValue("$uu", c.UpdatedAtUtc.ToString("o"));
                ins.Parameters.AddWithValue("$ls", (object?)(c.LastSeenUtc?.ToString("o")) ?? DBNull.Value);

                c.Id = Convert.ToInt64(ins.ExecuteScalar());
            }

            tx.Commit();
            return c;
        }
    }
}
