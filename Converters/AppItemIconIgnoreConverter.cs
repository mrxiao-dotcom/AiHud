using System;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiHUD.Models;

namespace AiHUD.Converters;

/// <summary>
/// 用于序列化的代理类，不包含不可序列化的 Icon 属性
/// </summary>
public class AppItemSerializationSurrogate
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsVisible { get; set; } = true;
    public int LaunchCount { get; set; }
    public DateTime? LastLaunchTime { get; set; }
    public string? IconPath { get; set; }

    public AppItem ToAppItem()
    {
        return new AppItem
        {
            Id = Id,
            Name = Name,
            ExecutablePath = ExecutablePath,
            Arguments = Arguments,
            Description = Description,
            IsVisible = IsVisible,
            LaunchCount = LaunchCount,
            LastLaunchTime = LastLaunchTime,
            IconPath = IconPath
        };
    }

    public static AppItemSerializationSurrogate FromAppItem(Models.AppItem item)
    {
        return new AppItemSerializationSurrogate
        {
            Id = item.Id,
            Name = item.Name,
            ExecutablePath = item.ExecutablePath,
            Arguments = item.Arguments,
            Description = item.Description,
            IsVisible = item.IsVisible,
            LaunchCount = item.LaunchCount,
            LastLaunchTime = item.LastLaunchTime,
            IconPath = item.IconPath
        };
    }
}

/// <summary>
/// Category 的序列化代理类
/// </summary>
public class CategorySerializationSurrogate
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public int Order { get; set; }
    public bool IsExpanded { get; set; } = true;
    public ObservableCollection<AppItemSerializationSurrogate> AppItems { get; set; } = new();

    public Category ToCategory()
    {
        var category = new Models.Category
        {
            Id = Id,
            Name = Name,
            Icon = Icon,
            Color = Color,
            Order = Order,
            IsExpanded = IsExpanded
        };

        foreach (var item in AppItems)
        {
            category.AppItems.Add(item.ToAppItem());
        }

        return category;
    }

    public static CategorySerializationSurrogate FromCategory(Models.Category category)
    {
        var surrogate = new CategorySerializationSurrogate
        {
            Id = category.Id,
            Name = category.Name,
            Icon = category.Icon,
            Color = category.Color,
            Order = category.Order,
            IsExpanded = category.IsExpanded
        };

        foreach (var item in category.AppItems)
        {
            surrogate.AppItems.Add(AppItemSerializationSurrogate.FromAppItem(item));
        }

        return surrogate;
    }
}

/// <summary>
/// AppData 的序列化代理类
/// </summary>
public class AppDataSerializationSurrogate
{
    public ObservableCollection<CategorySerializationSurrogate> Categories { get; set; } = new();
    public AppSettings? Settings { get; set; }

    public Models.AppData ToAppData()
    {
        var data = new Models.AppData();
        
        foreach (var cat in Categories)
        {
            data.Categories.Add(cat.ToCategory());
        }

        if (Settings != null)
        {
            data.Settings = Settings;
        }

        return data;
    }

    public static AppDataSerializationSurrogate FromAppData(Models.AppData data)
    {
        var surrogate = new AppDataSerializationSurrogate
        {
            Settings = data.Settings
        };

        foreach (var cat in data.Categories)
        {
            surrogate.Categories.Add(CategorySerializationSurrogate.FromCategory(cat));
        }

        return surrogate;
    }
}

/// <summary>
/// 处理 Category 序列化的转换器
/// </summary>
public class CategorySurrogateConverter : JsonConverter<Models.Category>
{
    public override Models.Category Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var surrogate = JsonSerializer.Deserialize<CategorySerializationSurrogate>(ref reader, options);
        return surrogate?.ToCategory() ?? new Models.Category();
    }

    public override void Write(Utf8JsonWriter writer, Models.Category value, JsonSerializerOptions options)
    {
        var surrogate = CategorySerializationSurrogate.FromCategory(value);
        JsonSerializer.Serialize(writer, surrogate, options);
    }
}
