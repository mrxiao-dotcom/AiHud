using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using AiHUD.Converters;
using AiHUD.Models;

namespace AiHUD.Services;

public class DataService
{
    private static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AiHUD"
    );

    private static readonly string DataFilePath = Path.Combine(AppDataFolder, "data.json");
    private static readonly string SettingsFilePath = Path.Combine(AppDataFolder, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = 
        { 
            new JsonStringEnumConverter(),
            new CategorySurrogateConverter()
        }
    };

    public DataService()
    {
        EnsureAppDataFolder();
    }

    private void EnsureAppDataFolder()
    {
        if (!Directory.Exists(AppDataFolder))
        {
            Directory.CreateDirectory(AppDataFolder);
        }
    }

    public async Task<AppData> LoadDataAsync()
    {
        try
        {
            if (File.Exists(DataFilePath))
            {
                var json = await File.ReadAllTextAsync(DataFilePath);
                System.Diagnostics.Debug.WriteLine($"[DataService.LoadDataAsync] 读取到 JSON 长度: {json?.Length ?? 0}");
                var surrogate = JsonSerializer.Deserialize<AppDataSerializationSurrogate>(json!, JsonOptions);
                if (surrogate != null)
                {
                    var data = surrogate.ToAppData();
                    return data;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading data: {ex.Message}\n{ex.StackTrace}");
        }

        return CreateDefaultData();
    }

    public async Task SaveDataAsync(AppData data)
    {
        try
        {
            var surrogate = AppDataSerializationSurrogate.FromAppData(data);
            var json = JsonSerializer.Serialize(surrogate, JsonOptions);
            System.Diagnostics.Debug.WriteLine($"[DataService.SaveDataAsync] 保存 JSON 长度: {json?.Length ?? 0}");
            await File.WriteAllTextAsync(DataFilePath, json);
            System.Diagnostics.Debug.WriteLine($"[DataService.SaveDataAsync] 保存成功!");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving data: {ex.Message}\n{ex.StackTrace}");
        }
    }

    public async Task SaveDataAsync(object data)
    {
        try
        {
            if (data is AppData appData)
            {
                await SaveDataAsync(appData);
            }
            else
            {
                var json = JsonSerializer.Serialize(data, JsonOptions);
                await File.WriteAllTextAsync(DataFilePath, json);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving data: {ex.Message}\n{ex.StackTrace}");
        }
    }

    public async Task<AppSettings> LoadSettingsAsync()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = await File.ReadAllTextAsync(SettingsFilePath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading settings: {ex.Message}");
        }

        return new AppSettings();
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            await File.WriteAllTextAsync(SettingsFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving settings: {ex.Message}");
        }
    }

    private static AppData CreateDefaultData()
    {
        var data = new AppData
        {
            Categories = new ObservableCollection<Category>
            {
                new()
                {
                    Name = "投资类",
                    Icon = "ChartLine",
                    Color = "#4CAF50",
                    Order = 1,
                    AppItems = new ObservableCollection<AppItem>()
                },
                new()
                {
                    Name = "工具类",
                    Icon = "Wrench",
                    Color = "#2196F3",
                    Order = 2,
                    AppItems = new ObservableCollection<AppItem>()
                },
                new()
                {
                    Name = "AI 助手",
                    Icon = "Robot",
                    Color = "#9C27B0",
                    Order = 3,
                    AppItems = new ObservableCollection<AppItem>()
                }
            }
        };

        return data;
    }
}
