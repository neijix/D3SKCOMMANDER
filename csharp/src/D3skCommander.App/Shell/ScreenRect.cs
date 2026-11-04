namespace D3skCommander.App.Shell;

/// <summary>A rectangle in screen pixels.</summary>
public readonly record struct ScreenRect(int X, int Y, int Width, int Height);

/// <summary>A point in screen pixels.</summary>
public readonly record struct ScreenPoint(int X, int Y);

/// <summary>The monitor the main window sits on.</summary>
public readonly record struct DesktopScreen(ScreenRect WorkingArea, double Scaling);
