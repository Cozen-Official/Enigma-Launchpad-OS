#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace Cozen
{
    public static class ColorNameMatcher
    {
        private static readonly string[] CachedPresetNames =
        {
            "Alice Blue", "Antique White", "Aqua", "Aqua Green", "Azure", "Beige", "Bisque", "Black", "Blanched Almond", "Blue",
            "Blue Violet", "Brown", "Burly Wood", "Cadet Blue", "Acid Green", "Cocoa Brown", "Coral", "Field Blue", "Cornsilk", "Crimson",
            "Cyan", "Dark Blue", "Dark Cyan", "Dark Gold", "Dark Green", "Dark Grey", "Dark Khaki", "Dark Magenta", "Moss Olive", "Dark Orange",
            "Dark Orchid", "Dark Red", "Dark Salmon", "Pine Green", "Slate Indigo", "Charcoal", "Teal Glow", "Dark Violet", "Deep Pink", "Bright Sky",
            "Dim Grey", "Dodger Blue", "Fire Brick", "Floral White", "Forest Green", "Fuchsia", "Soft Gray", "Ghost White", "Gold", "Old Gold",
            "Green", "Green Yellow", "Honeydew", "Hot Pink", "Indian Red", "Indigo", "Ivory", "Khaki", "Lavender", "Lavender Blush",
            "Lawn Green", "Lemon Chiffon", "Light Blue", "Light Coral", "Light Cyan", "Soft Gold", "Light Green", "Light Grey", "Light Pink", "Light Salmon",
            "Sea Mint", "Sky Mist", "Ash Grey", "Pale Steel", "Light Yellow", "Lime", "Lime Green", "Linen", "Magenta", "Maroon",
            "Seafoam", "Medium Blue", "Medium Orchid", "Medium Purple", "Lagoon Green", "Storm Blue", "Fresh Green", "Teal Green", "Berry Red", "Midnight Blue",
            "Mint Cream", "Misty Rose", "Moccasin", "Navajo White", "Navy", "Old Lace", "Olive", "Olive Drab", "Orange", "Orange Red",
            "Orchid", "Dusty Gold", "Pale Green", "Soft Aqua", "Muted Rose", "Papaya Whip", "Peach Puff", "Peru", "Pink", "Plum",
            "Powder Blue", "Purple", "Rebecca Purple", "Red", "Rosy Brown", "Royal Blue", "Saddle Brown", "Salmon", "Sandy Brown", "Sea Green",
            "Seashell", "Sienna", "Silver", "Sky Blue", "Slate Blue", "Slate Grey", "Snow", "Spring Green", "Steel Blue", "Tan",
            "Teal", "Thistle", "Tomato", "Pure Teal", "Violet", "Wheat", "White", "White Smoke", "Yellow", "Yellow Green"
        };

        private static readonly Color[] CachedPresetColors =
        {
            new Color(0.871367119f, 0.938685728f, 1.000000000f, 1f), // Alice Blue (#F0F8FF)
            new Color(0.955973353f, 0.830769877f, 0.679542470f, 1f), // Antique White (#FAEBD7)
            new Color(0.000000000f, 1.000000000f, 1.000000000f, 1f), // Aqua (#00FFFF)
            new Color(0.212230757f, 1.000000000f, 0.658374817f, 1f), // Aqua Green (#7FFFD4)
            new Color(0.871367119f, 1.000000000f, 1.000000000f, 1f), // Azure (#F0FFFF)
            new Color(0.913098652f, 0.913098652f, 0.715693501f, 1f), // Beige (#F5F5DC)
            new Color(1.000000000f, 0.775822218f, 0.552011402f, 1f), // Bisque (#FFE4C4)
            new Color(0.000000000f, 0.000000000f, 0.000000000f, 1f), // Black (#000000)
            new Color(1.000000000f, 0.830769877f, 0.610495571f, 1f), // Blanched Almond (#FFEBCD)
            new Color(0.000000000f, 0.000000000f, 1.000000000f, 1f), // Blue (#0000FF)
            new Color(0.254152094f, 0.024157632f, 0.760524505f, 1f), // Blue Violet (#8A2BE2)
            new Color(0.376262123f, 0.023153366f, 0.023153366f, 1f), // Brown (#A52A2A)
            new Color(0.730460740f, 0.479320183f, 0.242281122f, 1f), // Burly Wood (#DEB887)
            new Color(0.114435374f, 0.341914425f, 0.351532600f, 1f), // Cadet Blue (#5F9EA0)
            new Color(0.212230757f, 1.000000000f, 0.000000000f, 1f), // Acid Green (#7FFF00)
            new Color(0.644479682f, 0.141263291f, 0.012983032f, 1f), // Cocoa Brown (#D2691E)
            new Color(1.000000000f, 0.212230757f, 0.080219820f, 1f), // Coral (#FF7F50)
            new Color(0.127437680f, 0.300543794f, 0.846873232f, 1f), // Field Blue (#6495ED)
            new Color(1.000000000f, 0.938685728f, 0.715693501f, 1f), // Cornsilk (#FFF8DC)
            new Color(0.715693501f, 0.006995410f, 0.045186204f, 1f), // Crimson (#DC143C)
            new Color(0.000000000f, 1.000000000f, 1.000000000f, 1f), // Cyan (#00FFFF)
            new Color(0.000000000f, 0.000000000f, 0.258182853f, 1f), // Dark Blue (#00008B)
            new Color(0.000000000f, 0.258182853f, 0.258182853f, 1f), // Dark Cyan (#008B8B)
            new Color(0.479320183f, 0.238397574f, 0.003346536f, 1f), // Dark Gold (#B8860B)
            new Color(0.000000000f, 0.127437680f, 0.000000000f, 1f), // Dark Green (#006400)
            new Color(0.396755231f, 0.396755231f, 0.396755231f, 1f), // Dark Grey (#A9A9A9)
            new Color(0.508881321f, 0.473531496f, 0.147027266f, 1f), // Dark Khaki (#BDB76B)
            new Color(0.258182853f, 0.000000000f, 0.258182853f, 1f), // Dark Magenta (#8B008B)
            new Color(0.090841711f, 0.147027266f, 0.028426040f, 1f), // Moss Olive (#556B2F)
            new Color(1.000000000f, 0.262250658f, 0.000000000f, 1f), // Dark Orange (#FF8C00)
            new Color(0.318546778f, 0.031896033f, 0.603827339f, 1f), // Dark Orchid (#9932CC)
            new Color(0.258182853f, 0.000000000f, 0.000000000f, 1f), // Dark Red (#8B0000)
            new Color(0.814846572f, 0.304987314f, 0.194617830f, 1f), // Dark Salmon (#E9967A)
            new Color(0.274677312f, 0.502886458f, 0.274677312f, 1f), // Pine Green (#8FBC8F)
            new Color(0.064803267f, 0.046665086f, 0.258182853f, 1f), // Slate Indigo (#483D8B)
            new Color(0.028426040f, 0.078187422f, 0.078187422f, 1f), // Charcoal (#2F4F4F)
            new Color(0.000000000f, 0.617206562f, 0.637596874f, 1f), // Teal Glow (#00CED1)
            new Color(0.296138271f, 0.000000000f, 0.651405637f, 1f), // Dark Violet (#9400D3)
            new Color(1.000000000f, 0.006995410f, 0.291770650f, 1f), // Deep Pink (#FF1493)
            new Color(0.000000000f, 0.520995573f, 1.000000000f, 1f), // Bright Sky (#00BFFF)
            new Color(0.141263291f, 0.141263291f, 0.141263291f, 1f), // Dim Grey (#696969)
            new Color(0.012983032f, 0.278894263f, 1.000000000f, 1f), // Dodger Blue (#1E90FF)
            new Color(0.445201195f, 0.015996293f, 0.015996293f, 1f), // Fire Brick (#B22222)
            new Color(1.000000000f, 0.955973353f, 0.871367119f, 1f), // Floral White (#FFFAF0)
            new Color(0.015996293f, 0.258182853f, 0.015996293f, 1f), // Forest Green (#228B22)
            new Color(1.000000000f, 0.000000000f, 1.000000000f, 1f), // Fuchsia (#FF00FF)
            new Color(0.715693501f, 0.715693501f, 0.715693501f, 1f), // Soft Gray (#DCDCDC)
            new Color(0.938685728f, 0.938685728f, 1.000000000f, 1f), // Ghost White (#F8F8FF)
            new Color(1.000000000f, 0.679542470f, 0.000000000f, 1f), // Gold (#FFD700)
            new Color(0.701101892f, 0.376262123f, 0.014443844f, 1f), // Old Gold (#DAA520)
            new Color(0.000000000f, 0.215860500f, 0.000000000f, 1f), // Green (#008000)
            new Color(0.417885071f, 1.000000000f, 0.028426040f, 1f), // Green Yellow (#ADFF2F)
            new Color(0.871367119f, 1.000000000f, 0.871367119f, 1f), // Honeydew (#F0FFF0)
            new Color(1.000000000f, 0.141263291f, 0.456411023f, 1f), // Hot Pink (#FF69B4)
            new Color(0.610495571f, 0.107023103f, 0.107023103f, 1f), // Indian Red (#CD5C5C)
            new Color(0.070360096f, 0.000000000f, 0.223227957f, 1f), // Indigo (#4B0082)
            new Color(1.000000000f, 1.000000000f, 0.871367119f, 1f), // Ivory (#FFFFF0)
            new Color(0.871367119f, 0.791297940f, 0.262250658f, 1f), // Khaki (#F0E68C)
            new Color(0.791297940f, 0.791297940f, 0.955973353f, 1f), // Lavender (#E6E6FA)
            new Color(1.000000000f, 0.871367119f, 0.913098652f, 1f), // Lavender Blush (#FFF0F5)
            new Color(0.201556254f, 0.973445290f, 0.000000000f, 1f), // Lawn Green (#7CFC00)
            new Color(1.000000000f, 0.955973353f, 0.610495571f, 1f), // Lemon Chiffon (#FFFACD)
            new Color(0.417885071f, 0.686685312f, 0.791297940f, 1f), // Light Blue (#ADD8E6)
            new Color(0.871367119f, 0.215860500f, 0.215860500f, 1f), // Light Coral (#F08080)
            new Color(0.745404210f, 1.000000000f, 1.000000000f, 1f), // Light Cyan (#E0FFFF)
            new Color(0.955973353f, 0.955973353f, 0.644479682f, 1f), // Soft Gold (#FAFAD2)
            new Color(0.278894263f, 0.854992608f, 0.278894263f, 1f), // Light Green (#90EE90)
            new Color(0.651405637f, 0.651405637f, 0.651405637f, 1f), // Light Grey (#D3D3D3)
            new Color(1.000000000f, 0.467783796f, 0.533276404f, 1f), // Light Pink (#FFB6C1)
            new Color(1.000000000f, 0.351532600f, 0.194617830f, 1f), // Light Salmon (#FFA07A)
            new Color(0.014443844f, 0.445201195f, 0.401977780f, 1f), // Sea Mint (#20B2AA)
            new Color(0.242281122f, 0.617206562f, 0.955973353f, 1f), // Sky Mist (#87CEFA)
            new Color(0.184474995f, 0.246201327f, 0.318546778f, 1f), // Ash Grey (#778899)
            new Color(0.434153636f, 0.552011402f, 0.730460740f, 1f), // Pale Steel (#B0C4DE)
            new Color(1.000000000f, 1.000000000f, 0.745404210f, 1f), // Light Yellow (#FFFFE0)
            new Color(0.000000000f, 1.000000000f, 0.000000000f, 1f), // Lime (#00FF00)
            new Color(0.031896033f, 0.610495571f, 0.031896033f, 1f), // Lime Green (#32CD32)
            new Color(0.955973353f, 0.871367119f, 0.791297940f, 1f), // Linen (#FAF0E6)
            new Color(1.000000000f, 0.000000000f, 1.000000000f, 1f), // Magenta (#FF00FF)
            new Color(0.215860500f, 0.000000000f, 0.000000000f, 1f), // Maroon (#800000)
            new Color(0.132868322f, 0.610495571f, 0.401977780f, 1f), // Seafoam (#66CDAA)
            new Color(0.000000000f, 0.000000000f, 0.610495571f, 1f), // Medium Blue (#0000CD)
            new Color(0.491020850f, 0.090841711f, 0.651405637f, 1f), // Medium Orchid (#BA55D3)
            new Color(0.291770650f, 0.162029376f, 0.708375780f, 1f), // Medium Purple (#9370DB)
            new Color(0.045186204f, 0.450785783f, 0.165132195f, 1f), // Lagoon Green (#3CB371)
            new Color(0.198069320f, 0.138431615f, 0.854992608f, 1f), // Storm Blue (#7B68EE)
            new Color(0.000000000f, 0.955973353f, 0.323143209f, 1f), // Fresh Green (#00FA9A)
            new Color(0.064803267f, 0.637596874f, 0.603827339f, 1f), // Teal Green (#48D1CC)
            new Color(0.571124829f, 0.007499032f, 0.234550582f, 1f), // Berry Red (#C71585)
            new Color(0.009721217f, 0.009721217f, 0.162029376f, 1f), // Midnight Blue (#191970)
            new Color(0.913098652f, 1.000000000f, 0.955973353f, 1f), // Mint Cream (#F5FFFA)
            new Color(1.000000000f, 0.775822218f, 0.752942217f, 1f), // Misty Rose (#FFE4E1)
            new Color(1.000000000f, 0.775822218f, 0.462077000f, 1f), // Moccasin (#FFE4B5)
            new Color(1.000000000f, 0.730460740f, 0.417885071f, 1f), // Navajo White (#FFDEAD)
            new Color(0.000000000f, 0.000000000f, 0.215860500f, 1f), // Navy (#000080)
            new Color(0.982250550f, 0.913098652f, 0.791297940f, 1f), // Old Lace (#FDF5E6)
            new Color(0.215860500f, 0.215860500f, 0.000000000f, 1f), // Olive (#808000)
            new Color(0.147027266f, 0.270497791f, 0.016807376f, 1f), // Olive Drab (#6B8E23)
            new Color(1.000000000f, 0.376262123f, 0.000000000f, 1f), // Orange (#FFA500)
            new Color(1.000000000f, 0.059511238f, 0.000000000f, 1f), // Orange Red (#FF4500)
            new Color(0.701101892f, 0.162029376f, 0.672443157f, 1f), // Orchid (#DA70D6)
            new Color(0.854992608f, 0.806952258f, 0.401977780f, 1f), // Dusty Gold (#EEE8AA)
            new Color(0.313988713f, 0.964686248f, 0.313988713f, 1f), // Pale Green (#98FB98)
            new Color(0.428690497f, 0.854992608f, 0.854992608f, 1f), // Soft Aqua (#AFEEEE)
            new Color(0.708375780f, 0.162029376f, 0.291770650f, 1f), // Muted Rose (#DB7093)
            new Color(1.000000000f, 0.863157213f, 0.665387298f, 1f), // Papaya Whip (#FFEFD5)
            new Color(1.000000000f, 0.701101892f, 0.485149940f, 1f), // Peach Puff (#FFDAB9)
            new Color(0.610495571f, 0.234550582f, 0.049706566f, 1f), // Peru (#CD853F)
            new Color(1.000000000f, 0.527115126f, 0.597201788f, 1f), // Pink (#FFC0CB)
            new Color(0.723055129f, 0.351532600f, 0.723055129f, 1f), // Plum (#DDA0DD)
            new Color(0.434153636f, 0.745404210f, 0.791297940f, 1f), // Powder Blue (#B0E0E6)
            new Color(0.215860500f, 0.000000000f, 0.215860500f, 1f), // Purple (#800080)
            new Color(0.132868322f, 0.033104767f, 0.318546778f, 1f), // Rebecca Purple (#663399)
            new Color(1.000000000f, 0.000000000f, 0.000000000f, 1f), // Red (#FF0000)
            new Color(0.502886458f, 0.274677312f, 0.274677312f, 1f), // Rosy Brown (#BC8F8F)
            new Color(0.052860647f, 0.141263291f, 0.752942217f, 1f), // Royal Blue (#4169E1)
            new Color(0.258182853f, 0.059511238f, 0.006512091f, 1f), // Saddle Brown (#8B4513)
            new Color(0.955973353f, 0.215860500f, 0.168269400f, 1f), // Salmon (#FA8072)
            new Color(0.904661174f, 0.371237680f, 0.116970668f, 1f), // Sandy Brown (#F4A460)
            new Color(0.027320892f, 0.258182853f, 0.095307467f, 1f), // Sea Green (#2E8B57)
            new Color(1.000000000f, 0.913098652f, 0.854992608f, 1f), // Seashell (#FFF5EE)
            new Color(0.351532600f, 0.084376212f, 0.026241222f, 1f), // Sienna (#A0522D)
            new Color(0.527115126f, 0.527115126f, 0.527115126f, 1f), // Silver (#C0C0C0)
            new Color(0.242281122f, 0.617206562f, 0.830769877f, 1f), // Sky Blue (#87CEEB)
            new Color(0.144128471f, 0.102241733f, 0.610495571f, 1f), // Slate Blue (#6A5ACD)
            new Color(0.162029376f, 0.215860500f, 0.278894263f, 1f), // Slate Grey (#708090)
            new Color(1.000000000f, 0.955973353f, 0.955973353f, 1f), // Snow (#FFFAFA)
            new Color(0.000000000f, 1.000000000f, 0.212230757f, 1f), // Spring Green (#00FF7F)
            new Color(0.061246054f, 0.223227957f, 0.456411023f, 1f), // Steel Blue (#4682B4)
            new Color(0.644479682f, 0.456411023f, 0.262250658f, 1f), // Tan (#D2B48C)
            new Color(0.000000000f, 0.215860500f, 0.215860500f, 1f), // Teal (#008080)
            new Color(0.686685312f, 0.520995573f, 0.686685312f, 1f), // Thistle (#D8BFD8)
            new Color(1.000000000f, 0.124771818f, 0.063010018f, 1f), // Tomato (#FF6347)
            new Color(0.051269458f, 0.745404210f, 0.630757136f, 1f), // Pure Teal (#40E0D0)
            new Color(0.854992608f, 0.223227957f, 0.854992608f, 1f), // Violet (#EE82EE)
            new Color(0.913098652f, 0.730460740f, 0.450785783f, 1f), // Wheat (#F5DEB3)
            new Color(1.000000000f, 1.000000000f, 1.000000000f, 1f), // White (#FFFFFF)
            new Color(0.913098652f, 0.913098652f, 0.913098652f, 1f), // White Smoke (#F5F5F5)
            new Color(1.000000000f, 1.000000000f, 0.000000000f, 1f), // Yellow (#FFFF00)
            new Color(0.323143209f, 0.610495571f, 0.031896033f, 1f)  // Yellow Green (#9ACD32)
        };
        
        private static string[] presetNames;
        private static Color[] presetColors;
        private static Vector3[] presetLab;
        private static HashSet<string> presetNamesSet;
        
        private static void EnsureInitialized()
        {
            if (presetNames != null && presetColors != null && presetLab != null && presetNames.Length > 0 && presetColors.Length > 0 && presetLab.Length == presetColors.Length)
            {
                return;
            }
            
            presetNames = CachedPresetNames;
            presetColors = CachedPresetColors;
            
            if (presetLab == null || presetLab.Length != presetColors.Length)
            {
                presetLab = new Vector3[presetColors.Length];
                for (int i = 0; i < presetColors.Length; i++)
                {
                    presetLab[i] = ColorToLab(presetColors[i]);
                }
            }
            
            // Initialize HashSet for O(1) name lookups
            if (presetNamesSet == null)
            {
                presetNamesSet = new HashSet<string>(presetNames);
            }
        }
        
        public static string[] GetColorNamesFromArray(Color[] inputColors)
        {
            if (inputColors == null || inputColors.Length == 0)
            {
                return new string[0];
            }
            EnsureInitialized();
            
            string[] outputNames = new string[inputColors.Length];
            for (int i = 0; i < inputColors.Length; i++)
            {
                outputNames[i] = GetNearestColorName(inputColors[i]);
            }
            return outputNames;
        }
        
        public static string GetNearestColorName(Color color)
        {
            EnsureInitialized();
            
            Color target = color;
            
            if (presetColors == null || presetNames == null || presetColors.Length == 0 || presetNames.Length == 0)
            {
                return string.Empty;
            }

            Vector3 targetLab = ColorToLab(target);
            
            float bestDistance = float.MaxValue;
            string bestName = "";
            int comparisonCount = Mathf.Min(presetColors.Length, presetNames.Length);
            for (int i = 0; i < comparisonCount; i++)
            {
                float dist = ColorDistance(targetLab, i);
                if (dist < bestDistance)
                {
                    bestDistance = dist;
                    bestName = presetNames[i];
                }
            }
            return bestName;
        }
        
        public static string GetNearestColorName(Color color, bool hasColor)
        {
            if (!hasColor)
            {
                return string.Empty;
            }
            
            return GetNearestColorName(color);
        }
        
        public static bool IsKnownColorName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }
            
            EnsureInitialized();
            
            if (presetNamesSet == null || presetNamesSet.Count == 0)
            {
                return false;
            }
            
            return presetNamesSet.Contains(name);
        }
        
        private static float ColorDistance(Vector3 targetLab, int presetIndex)
        {
            return CalculateDeltaE2000(targetLab, presetLab[presetIndex]);
        }
        
        private static Vector3 ColorToLab(Color color)
        {
            Vector3 xyz = RgbToXyz(color);
            return XyzToLab(xyz);
        }
        
        private static Vector3 RgbToXyz(Color color)
        {
            float r = Mathf.Clamp01(color.r);
            float g = Mathf.Clamp01(color.g);
            float b = Mathf.Clamp01(color.b);
            
            float x = r * 0.4124564f + g * 0.3575761f + b * 0.1804375f;
            float y = r * 0.2126729f + g * 0.7151522f + b * 0.0721750f;
            float z = r * 0.0193339f + g * 0.1191920f + b * 0.9503041f;
            
            return new Vector3(x * 100f, y * 100f, z * 100f);
        }
        
        private static Vector3 XyzToLab(Vector3 xyz)
        {
            const float refX = 95.047f;
            const float refY = 100f;
            const float refZ = 108.883f;
            const float epsilon = 216f / 24389f;
            const float kappa = 24389f / 27f;
            
            float xr = xyz.x / refX;
            float yr = xyz.y / refY;
            float zr = xyz.z / refZ;
            
            float fx = xr > epsilon ? Mathf.Pow(xr, 1f / 3f) : (kappa * xr + 16f) / 116f;
            float fy = yr > epsilon ? Mathf.Pow(yr, 1f / 3f) : (kappa * yr + 16f) / 116f;
            float fz = zr > epsilon ? Mathf.Pow(zr, 1f / 3f) : (kappa * zr + 16f) / 116f;
            
            float l = 116f * fy - 16f;
            float a = 500f * (fx - fy);
            float b = 200f * (fy - fz);
            
            return new Vector3(l, a, b);
        }
        
        private static float CalculateDeltaE2000(Vector3 lab1, Vector3 lab2)
        {
            float lBarPrime = 0.5f * (lab1.x + lab2.x);
            float c1 = Mathf.Sqrt(lab1.y * lab1.y + lab1.z * lab1.z);
            float c2 = Mathf.Sqrt(lab2.y * lab2.y + lab2.z * lab2.z);
            float cBar = 0.5f * (c1 + c2);
            
            float cBar7 = Mathf.Pow(cBar, 7f);
            float g = 0.5f * (1f - Mathf.Sqrt(cBar7 / (cBar7 + Mathf.Pow(25f, 7f))));
            
            float a1Prime = lab1.y * (1f + g);
            float a2Prime = lab2.y * (1f + g);
            
            float c1Prime = Mathf.Sqrt(a1Prime * a1Prime + lab1.z * lab1.z);
            float c2Prime = Mathf.Sqrt(a2Prime * a2Prime + lab2.z * lab2.z);
            
            float h1Prime = Mathf.Atan2(lab1.z, a1Prime);
            if (h1Prime < 0f)
            {
                h1Prime += 2f * Mathf.PI;
            }
            
            float h2Prime = Mathf.Atan2(lab2.z, a2Prime);
            if (h2Prime < 0f)
            {
                h2Prime += 2f * Mathf.PI;
            }
            
            float deltaLPrime = lab2.x - lab1.x;
            float deltaCPrime = c2Prime - c1Prime;
            
            float hPrimeDiff = h2Prime - h1Prime;
            float deltaHPrime;
            if (c1Prime * c2Prime == 0f)
            {
                deltaHPrime = 0f;
            }
            else if (Mathf.Abs(hPrimeDiff) <= Mathf.PI)
            {
                deltaHPrime = hPrimeDiff;
            }
            else if (hPrimeDiff > Mathf.PI)
            {
                deltaHPrime = hPrimeDiff - 2f * Mathf.PI;
            }
            else
            {
                deltaHPrime = hPrimeDiff + 2f * Mathf.PI;
            }
            
            float deltaHp = 2f * Mathf.Sqrt(c1Prime * c2Prime) * Mathf.Sin(deltaHPrime / 2f);
            
            float hBarPrime;
            if (c1Prime * c2Prime == 0f)
            {
                hBarPrime = h1Prime + h2Prime;
            }
            else if (Mathf.Abs(hPrimeDiff) > Mathf.PI)
            {
                hBarPrime = (h1Prime + h2Prime + 2f * Mathf.PI) / 2f;
            }
            else
            {
                hBarPrime = (h1Prime + h2Prime) / 2f;
            }
            
            float t = 1f - 0.17f * Mathf.Cos(hBarPrime - Mathf.PI / 6f) +
            0.24f * Mathf.Cos(2f * hBarPrime) +
            0.32f * Mathf.Cos(3f * hBarPrime + Mathf.PI / 30f) -
            0.20f * Mathf.Cos(4f * hBarPrime - 63f * Mathf.PI / 180f);
            
            float deltaTheta = 30f * Mathf.PI / 180f * Mathf.Exp(-Mathf.Pow((180f / Mathf.PI * hBarPrime - 275f) / 25f, 2f));
            float cBarPrime = 0.5f * (c1Prime + c2Prime);
            float sL = 1f + (0.015f * Mathf.Pow(lBarPrime - 50f, 2f)) / Mathf.Sqrt(20f + Mathf.Pow(lBarPrime - 50f, 2f));
            float sC = 1f + 0.045f * cBarPrime;
            float sH = 1f + 0.015f * cBarPrime * t;
            float rT = -2f * Mathf.Sqrt(Mathf.Pow(cBarPrime, 7f) / (Mathf.Pow(cBarPrime, 7f) + Mathf.Pow(25f, 7f))) * Mathf.Sin(2f * deltaTheta);
            
            float deltaE = Mathf.Sqrt(
            Mathf.Pow(deltaLPrime / sL, 2f) +
            Mathf.Pow(deltaCPrime / sC, 2f) +
            Mathf.Pow(deltaHp / sH, 2f) +
            rT * (deltaCPrime / sC) * (deltaHp / sH)
            );
            
            return deltaE;
        }
    }
}
#endif
