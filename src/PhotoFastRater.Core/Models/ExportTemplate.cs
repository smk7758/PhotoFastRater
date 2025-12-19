namespace PhotoFastRater.Core.Models;

public class ExportTemplate
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    // 出力サイズ
    public int OutputWidth { get; set; }
    public int OutputHeight { get; set; }
    public bool MaintainAspectRatio { get; set; } = true;

    // 枠設定
    public bool EnableFrame { get; set; }
    public int FrameWidth { get; set; }
    public string FrameColor { get; set; } = "#FFFFFF";

    // EXIF オーバーレイ設定
    public bool EnableExifOverlay { get; set; }
    public ExifOverlayPosition Position { get; set; }
    public int CustomX { get; set; } = 50;  // カスタム位置のX座標（パーセント: 0-100）
    public int CustomY { get; set; } = 50;  // カスタム位置のY座標（パーセント: 0-100）
    public string DisplayFields { get; set; } = string.Empty; // JSON serialized
    public string FontFamily { get; set; } = "Arial";
    public int FontSize { get; set; } = 14;
    public string TextColor { get; set; } = "#FFFFFF";
    public string BackgroundColor { get; set; } = "#000000";
    public int BackgroundOpacity { get; set; } = 70;

    // SNS 設定
    public SocialMediaPlatform TargetPlatform { get; set; }
}

public enum ExifOverlayPosition
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    Custom
}

public enum ExifField
{
    CameraModel,
    LensModel,
    FocalLength,
    Aperture,
    ShutterSpeed,
    ISO,
    DateTaken,
    Location
}

public enum SocialMediaPlatform
{
    Instagram,
    Twitter,
    Facebook,
    Custom
}
