using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OmniKiosk.Wpf.Sdk.Face
{
    public class CustomerRecord
    {
        public string CustomerId { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public string IdType { get; set; } = "";
        public string IdNo { get; set; } = "";
        public string Phone { get; set; } = "";

        // Face feature/template returned by SDK
        public string FeatureBase64 { get; set; } = "";

        // Optional: store snapshot for UX
        public string? FaceImageBase64 { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
    }

    public class CustomerStore
    {
        private readonly string _path;

        public CustomerStore(string path)
        {
            _path = path;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        }

        public List<CustomerRecord> Load()
        {
            if (!File.Exists(_path)) return new List<CustomerRecord>();
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<List<CustomerRecord>>(json) ?? new List<CustomerRecord>();
        }

        public void Save(List<CustomerRecord> records)
        {
            var json = JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_path, json);
        }

        public void Upsert(List<CustomerRecord> records, CustomerRecord record)
        {
            var idx = records.FindIndex(r => r.CustomerId == record.CustomerId);
            if (idx >= 0) records[idx] = record;
            else records.Add(record);

            Save(records);
        }
    }
}