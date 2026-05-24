using MudBlazor;

namespace MCServer;

public static class Theme
{
    private static readonly string GreenAccent = "#69c247";
    private static readonly string GrayAccent = "#696969";

    public static MudTheme MasterTheme => new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = GreenAccent,
            Secondary = GrayAccent,
            Background = "#FFFFFF",
            Surface = "#F5F5F5",
            AppbarBackground = "#FFFFFF",
            AppbarText = "#000000",
            DrawerBackground = "#FFFFFF",
            DrawerText = "#000000",
            TextPrimary = "#000000",
            TextSecondary = "#333333",
            ActionDefault = GrayAccent,
            ActionDisabled = "#CCCCCC",
            Divider = "#E0E0E0",
            DividerLight = "#F0F0F0",
            TableLines = "#E0E0E0",
            LinesDefault = "#E0E0E0",
            LinesInputs = "#E0E0E0",
            Dark = "#000000",
            DarkDarken = "#1A1A1A",
            DarkLighten = "#333333",
            Info = "#2196F3",
            Success = "#4CAF50",
            Warning = "#FF9800",
            Error = "#F44336",
        },
        PaletteDark = new PaletteDark
        {
            Primary = GreenAccent,
            Secondary = GrayAccent,
            Background = "#121212",
            Surface = "#1E1E1E",
            AppbarBackground = "#000000",
            AppbarText = "#FFFFFF",
            DrawerBackground = "#1A1A1A",
            DrawerText = "#FFFFFF",
            TextPrimary = "#FFFFFF",
            TextSecondary = "#B0B0B0",
            ActionDefault = GrayAccent,
            ActionDisabled = "#555555",
            Divider = "#333333",
            DividerLight = "#2A2A2A",
            TableLines = "#333333",
            LinesDefault = "#333333",
            LinesInputs = "#333333",
            Dark = "#000000",
            DarkDarken = "#0A0A0A",
            DarkLighten = "#2A2A2A",
            Info = "#64B5F6",
            Success = "#81C784",
            Warning = "#FFB74D",
            Error = "#E57373",
        }
    };
}
