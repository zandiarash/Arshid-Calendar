namespace ArshidCalendar.Desktop.ViewModels;

/// <summary>A month in the taskbar flyout's year view.</summary>
/// <param name="IsCurrent">Today's month (filled with the accent colour).</param>
/// <param name="IsDisplayed">The month the day view was showing (ringed).</param>
public sealed record MonthCell(int Month, string Name, bool IsCurrent, bool IsDisplayed);
