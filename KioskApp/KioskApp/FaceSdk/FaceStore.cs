using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace OmniKiosk.Wpf.FaceSdk
{
    public class StoredFace
    {
        public string UserId { get; set; }
        public byte[] Feature { get; set; }
    }

    public static class FaceStore
    {
        private static readonly string FilePath = "Data/faces.json";

        public static List<StoredFace> Load()
        {
            if (!File.Exists(FilePath))
                return new List<StoredFace>();

            return JsonSerializer.Deserialize<List<StoredFace>>(
                File.ReadAllText(FilePath));
        }

        public static void Save(string userId, byte[] feature)
        {
            Directory.CreateDirectory("Data");
            var faces = Load();
            faces.Add(new StoredFace { UserId = userId, Feature = feature });
            File.WriteAllText(FilePath,
                JsonSerializer.Serialize(faces));
        }
    }
}
