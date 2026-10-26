namespace VibeWeather.Services;

/// <summary>
/// Whether the quick place picker's sheet (QuickPlaces, behind the floating button) is open. A singleton, so Android's
/// Back (MainPage) can close it before doing anything else.
/// </summary>
public sealed class PlacePicker
{
    public bool IsOpen { get; private set; }

    public event Action? Changed;

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        Changed?.Invoke();
    }

    /// <returns>Whether it was open.</returns>
    public bool Close()
    {
        if (!IsOpen) return false;
        IsOpen = false;
        Changed?.Invoke();
        return true;
    }
}
