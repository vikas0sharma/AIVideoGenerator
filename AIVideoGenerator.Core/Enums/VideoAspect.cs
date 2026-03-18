using System;
using System.Collections.Generic;
using System.Text;

namespace AIVideoGenerator.Core.Enums
{
    public enum VideoAspect
    {
        Portrait,   // 9:16  → 1080×1920
        Landscape,  // 16:9  → 1920×1080
        Square      // 1:1   → 1080×1080
    }

    public static class VideoAspectExtensions
    {
        public static (int Width, int Height) ToResolution(this VideoAspect aspect) => aspect switch
        {
            VideoAspect.Landscape => (1920, 1080),
            VideoAspect.Portrait => (1080, 1920),
            VideoAspect.Square => (1080, 1080),
            _ => (1080, 1920)
        };

        public static string ToOrientationString(this VideoAspect aspect) => aspect switch
        {
            VideoAspect.Landscape => "landscape",
            VideoAspect.Portrait => "portrait",
            VideoAspect.Square => "square",
            _ => "portrait"
        };

        public static VideoAspect Parse(string? value) => value switch
        {
            "16:9" or "landscape" => VideoAspect.Landscape,
            "9:16" or "portrait" => VideoAspect.Portrait,
            "1:1" or "square" => VideoAspect.Square,
            _ => VideoAspect.Portrait
        };
    }
}
