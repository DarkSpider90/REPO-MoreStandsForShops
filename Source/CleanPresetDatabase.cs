using System.Collections.Generic;
using UnityEngine;

namespace MoreStandsForShops;

/// <summary>
/// Deterministic preset database for the additional Upgrade Stand.
/// Points are stored only by the main shop N-module; extra modules are not used
/// for placement filtering because these anchors are validated at runtime.
/// </summary>
public static class CleanPresetDatabase
{
    private const string LevelPrefix = "Level Generator/Level/";

    public static List<SpawnPointData> GetSpawnPoints()
    {
        return new List<SpawnPointData>
        {
            // Center Extract
            Point(
                "center_extract_right_rear",
                "Module - Shop - N - Center Extract(Clone)",
                7.04f, 0.03f, -5.075f, 270f, 34,
                new[] { "Main/Level Generator/Level/Module - Shop - N - Center Extract(Clone)/WALLS/RIGHT/Not Connected/shop sign lynx" }),

            Point(
                "center_extract_right_mid",
                "Module - Shop - N - Center Extract(Clone)",
                7.04f, 0.03f, -1.635f, 270f, 1,
                System.Array.Empty<string>()),

            Point(
                "center_extract_top_mid",
                "Module - Shop - N - Center Extract(Clone)",
                0.471f, 0.03f, 7.04f, 180f, 1,
                new[]
                {
                    "Main/Level Generator/Level/Module - Shop - N - Center Extract(Clone)/WALLS/TOP/Not Connected/Candy Shelf 2",
                    "Main/Level Generator/Level/Module - Shop - N - Center Extract(Clone)/WALLS/TOP/Not Connected/Shop prop fridge"
                }),

            // Corner Stands
            Point(
                "corner_stands_bottom_left",
                "Module - Shop - N - Corner Stands(Clone)",
                -1.72f, 0.03f, -7.04f, 0f, 1,
                new[]
                {
                    "Main/Level Generator/Level/Module - Shop - N - Corner Stands(Clone)/WALLS/BOT/Not Connected/Shop Ice Cream Freezer",
                    "Main/Level Generator/Level/Module - Shop - N - Corner Stands(Clone)/WALLS/BOT/Not Connected/shop sign tyre (1)",
                    "Main/Level Generator/Level/Module - Shop - N - Corner Stands(Clone)/WALLS/BOT/Not Connected/shop sign tyre (2)"
                }),

            Point(
                "corner_stands_bottom_mid",
                "Module - Shop - N - Corner Stands(Clone)",
                0.637f, 0.03f, -7.04f, 0f, 8,
                new[]
                {
                    "Main/Level Generator/Level/Module - Shop - N - Corner Stands(Clone)/WALLS/BOT/Not Connected/Candy Shelf 2",
                    "Main/Level Generator/Level/Module - Shop - N - Corner Stands(Clone)/WALLS/BOT/Not Connected/Shop Ice Cream Freezer",
                    "Main/Level Generator/Level/Module - Shop - N - Corner Stands(Clone)/WALLS/BOT/Not Connected/shop sign tyre (1)"
                }),

            Point(
                "corner_stands_left_mid",
                "Module - Shop - N - Corner Stands(Clone)",
                -7.04f, 0.03f, -1.868f, 90f, 5,
                new[] { "Main/Level Generator/Level/Module - Shop - N - Corner Stands(Clone)/WALLS/LEFT/Not Connected/Shop Ice Cream Freezer (1)" }),

            Point(
                "corner_stands_right_secret",
                "Module - Shop - N - Corner Stands(Clone)",
                7.04f, 0.03f, -1.349f, 270f, 1,
                new[]
                {
                    "Main/Level Generator/Level/Module - Shop - N - Corner Stands(Clone)/WALLS/RIGHT/Not Connected/Shop Magazine Holder",
                    "Main/Level Generator/Level/Module - Shop - N - Corner Stands(Clone)/WALLS/RIGHT/Not Connected/shop sign lynx"
                }),

            Point(
                "corner_stands_right_mid",
                "Module - Shop - N - Corner Stands(Clone)",
                7.04f, 0.03f, -0.682f, 270f, 5,
                new[] { "Main/Level Generator/Level/Module - Shop - N - Corner Stands(Clone)/WALLS/RIGHT/Not Connected/Shop Magazine Holder" }),

            Point(
                "corner_stands_top_mid",
                "Module - Shop - N - Corner Stands(Clone)",
                0.054f, 0.03f, 7.04f, 180f, 9,
                System.Array.Empty<string>()),

            // Middle Stands
            Point(
                "middle_stands_left_secret",
                "Module - Shop - N - Middle Stands(Clone)",
                -7.04f, 0.03f, -1.385f, 90f, 1,
                new[] { "Main/Level Generator/Level/Module - Shop - N - Middle Stands(Clone)/WALLS/LEFT/Not Connected/shop sign ai" }),

            Point(
                "middle_stands_left_mid",
                "Module - Shop - N - Middle Stands(Clone)",
                -7.04f, 0.03f, -0.481f, 90f, 1,
                new[]
                {
                    "Main/Level Generator/Level/Module - Shop - N - Middle Stands(Clone)/WALLS/LEFT/Not Connected/shop sign ai",
                    "Main/Level Generator/Level/Module - Shop - N - Middle Stands(Clone)/WALLS/LEFT/Not Connected/shop sign hotdog"
                }),

            Point(
                "middle_stands_right_mid",
                "Module - Shop - N - Middle Stands(Clone)",
                7.04f, 0.03f, -0.639f, 270f, 5,
                new[] { "Main/Level Generator/Level/Module - Shop - N - Middle Stands(Clone)/WALLS/RIGHT/Not Connected/Shop Magazine Holder" }),

            Point(
                "middle_stands_top_left",
                "Module - Shop - N - Middle Stands(Clone)",
                -5.504f, 0.03f, 7.04f, 180f, 13,
                new[]
                {
                    "Main/Level Generator/Level/Module - Shop - N - Middle Stands(Clone)/WALLS/RIGHT/Not Connected/Shop Magazine Holder",
                    "Main/Level Generator/Level/Module - Shop - N - Middle Stands(Clone)/WALLS/TOP/Not Connected/Shop Magazine Stand (1)"
                }),

            Point(
                "middle_stands_top_car_service",
                "Module - Shop - N - Middle Stands(Clone)",
                -4.184f, 0.03f, 7.04f, 180f, 1,
                new[] { "Main/Level Generator/Level/Module - Shop - N - Middle Stands(Clone)/WALLS/TOP/Connected/Soda Machine (1)" }),

            Point(
                "middle_stands_top_mid",
                "Module - Shop - N - Middle Stands(Clone)",
                0.051f, 0.03f, 7.04f, 180f, 13,
                System.Array.Empty<string>())
        };
    }

    private static SpawnPointData Point(string id, string mainModule, float x, float y, float z, float yaw, int sourceCount, string[] disablePaths)
    {
        return new SpawnPointData
        {
            VariantId = id,
            MainModule = LevelPrefix + mainModule,
            LocalPosition = new Vector3(x, y, z),
            LocalYaw = yaw,
            SourceCount = sourceCount,
            DisablePaths = disablePaths,
            RejectIfPresentPaths = System.Array.Empty<string>()
        };
    }
}

public class SpawnPointData
{
    public string VariantId { get; set; }
    public string MainModule { get; set; }
    public Vector3 LocalPosition { get; set; }
    public float LocalYaw { get; set; }
    public int SourceCount { get; set; }
    public string[] DisablePaths { get; set; } = System.Array.Empty<string>();
    public string[] RejectIfPresentPaths { get; set; } = System.Array.Empty<string>();
}
