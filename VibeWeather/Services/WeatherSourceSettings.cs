namespace VibeWeather.Services;

public enum WeatherSource { OpenMeteo, AzureMaps }

/// <summary>
/// Which weather service the app uses, and the Azure Maps subscription key. The key is kept in the
/// platform's secure storage (Android: encrypted with a Keystore key), not in plain preferences.
/// <see cref="Changed"/> fires when the source or the key changes.
/// </summary>
public sealed class WeatherSourceSettings
{
    const string SourcePreference = "weather_source";
    const string AzureKeyStorage = "azure_maps_key";

    readonly SemaphoreSlim keyLock = new(1, 1);
    string? azureKey;
    bool keyLoaded;

    public event Action? Changed;

    public WeatherSource Source { get; private set; } =
        Enum.TryParse<WeatherSource>(Preferences.Default.Get(SourcePreference, ""), out var source) ? source : WeatherSource.OpenMeteo;

    public async Task<string?> GetAzureKeyAsync()
    {
        await keyLock.WaitAsync();
        try
        {
            if (!keyLoaded)
            {
                azureKey = await SecureStorage.Default.GetAsync(AzureKeyStorage);
                keyLoaded = true;
            }
            return azureKey;
        }
        finally
        {
            keyLock.Release();
        }
    }

    public async Task<bool> HasAzureKeyAsync() => !string.IsNullOrEmpty(await GetAzureKeyAsync());

    /// <summary>Stores the key (already validated by the caller).</summary>
    public async Task SetAzureKeyAsync(string key)
    {
        await keyLock.WaitAsync();
        try
        {
            await SecureStorage.Default.SetAsync(AzureKeyStorage, key);
            azureKey = key;
            keyLoaded = true;
        }
        finally
        {
            keyLock.Release();
        }
        Changed?.Invoke();
    }

    /// <summary>Forgets the key; if Azure Maps was the source, the app goes back to Open-Meteo.</summary>
    public async Task RemoveAzureKeyAsync()
    {
        await keyLock.WaitAsync();
        try
        {
            SecureStorage.Default.Remove(AzureKeyStorage);
            azureKey = null;
            keyLoaded = true;
        }
        finally
        {
            keyLock.Release();
        }
        if (Source == WeatherSource.AzureMaps) StoreSource(WeatherSource.OpenMeteo);
        Changed?.Invoke();
    }

    /// <summary>Switches the source. Returns false (and changes nothing) for Azure Maps without a key.</summary>
    public async Task<bool> TrySetSourceAsync(WeatherSource source)
    {
        if (source == Source) return true;
        if (source == WeatherSource.AzureMaps && !await HasAzureKeyAsync()) return false;
        StoreSource(source);
        Changed?.Invoke();
        return true;
    }

    void StoreSource(WeatherSource source)
    {
        Source = source;
        Preferences.Default.Set(SourcePreference, source.ToString());
    }
}
