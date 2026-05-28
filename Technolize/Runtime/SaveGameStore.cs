using System.Text.Json;

namespace Technolize.Runtime;

public sealed class SaveGameStore
{
    private const string SaveFileName = "current-save.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _saveDirectory;
    private readonly string _saveFilePath;

    public SaveGameStore()
    {
        _saveDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Technolize");
        _saveFilePath = Path.Combine(_saveDirectory, SaveFileName);
    }

    public bool HasCurrentSave()
    {
        return File.Exists(_saveFilePath);
    }

    public bool TryLoadCurrentSave(out SaveGameMetadata? save)
    {
        save = null;
        if (!File.Exists(_saveFilePath))
        {
            return false;
        }

        try
        {
            string json = File.ReadAllText(_saveFilePath);
            save = JsonSerializer.Deserialize<SaveGameMetadata>(json, JsonOptions);
            return save is not null;
        }
        catch
        {
            return false;
        }
    }

    public SaveGameMetadata CreateNewSave()
    {
        Directory.CreateDirectory(_saveDirectory);

        SaveGameMetadata save = new(
            Guid.NewGuid(),
            Random.Shared.Next(int.MinValue, int.MaxValue),
            DateTimeOffset.UtcNow);

        File.WriteAllText(_saveFilePath, JsonSerializer.Serialize(save, JsonOptions));
        return save;
    }

    public void DeleteCurrentSave()
    {
        if (File.Exists(_saveFilePath))
        {
            File.Delete(_saveFilePath);
        }
    }
}

public sealed record SaveGameMetadata(Guid Id, int WorldSeed, DateTimeOffset CreatedAtUtc);
