using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace OmniKiosk.Wpf.Services.MoneyExchange
{
    public sealed class KioskDb
    {
        public string DbPath { get; }

        public KioskDb()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KioskApp");
            Directory.CreateDirectory(dir);
            DbPath = Path.Combine(dir, "kiosk.db");
            EnsureCreated();
        }

        private void EnsureCreated()
        {
            using var con = new SqliteConnection($"Data Source={DbPath}");
            con.Open();

            var cmd = con.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS Customers (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  IdType TEXT NOT NULL,
  IdNo TEXT NOT NULL,
  FullName TEXT,
  Nationality TEXT,
  Sex TEXT,
  DateOfBirth TEXT,
  MobileNo TEXT,
  FaceFeatureBase64 TEXT,
  FaceImageBase64 TEXT,
  CreatedAtUtc TEXT NOT NULL,
  UpdatedAtUtc TEXT NOT NULL,
  LastSeenUtc TEXT
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_Customers_IdType_IdNo ON Customers(IdType, IdNo);

CREATE TABLE IF NOT EXISTS MoneyExchangeTxns (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  CreatedAtUtc TEXT NOT NULL,
  CustomerId INTEGER NOT NULL,
  FromCurrency TEXT NOT NULL,
  FromAmount REAL NOT NULL,
  Rate REAL NOT NULL,
  MyrAmount REAL NOT NULL,
  CashInsertedMyr REAL NOT NULL,
  Status TEXT NOT NULL,
  Notes TEXT,
  FOREIGN KEY(CustomerId) REFERENCES Customers(Id)
);
";
            cmd.ExecuteNonQuery();

            // CREATE TABLE IF NOT EXISTS only fires on a brand new kiosk.db - a
            // kiosk that's already been running has a Customers table without
            // MobileNo, and that statement silently does nothing to it. This
            // migrates existing installs without touching any saved history.
            MigrateAddColumnIfMissing(con, "Customers", "MobileNo", "TEXT");
        }

        private static void MigrateAddColumnIfMissing(SqliteConnection con, string table, string column, string sqlType)
        {
            using (var check = con.CreateCommand())
            {
                check.CommandText = $"PRAGMA table_info({table});";
                using var reader = check.ExecuteReader();
                while (reader.Read())
                {
                    // column 'name' is index 1 in PRAGMA table_info's result set
                    if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                        return; // already present, nothing to do
                }
            }

            using var alter = con.CreateCommand();
            alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {sqlType};";
            alter.ExecuteNonQuery();
        }

        public SqliteConnection Open()
        {
            var con = new SqliteConnection($"Data Source={DbPath}");
            con.Open();
            return con;
        }
    }
}
