using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace MCPhappey.Tools.StabilityAI.Enums;

public enum StabilityAISD3ModelType
{
    [EnumMember(Value = "sd3.5-large")]
    large,
    [EnumMember(Value = "sd3.5-large-turbo")]
    large_turbo,
    [EnumMember(Value = "sd3.5-medium")]
    medium,
    [EnumMember(Value = "sd3.5-flash")]
    flash
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StylePreset
{
    [EnumMember(Value = "3d-model")]
    Model3D,

    [EnumMember(Value = "analog-film")]
    AnalogFilm,

    [EnumMember(Value = "anime")]
    Anime,

    [EnumMember(Value = "cinematic")]
    Cinematic,

    [EnumMember(Value = "comic-book")]
    ComicBook,

    [EnumMember(Value = "digital-art")]
    DigitalArt,

    [EnumMember(Value = "enhance")]
    Enhance,

    [EnumMember(Value = "fantasy-art")]
    FantasyArt,

    [EnumMember(Value = "isometric")]
    Isometric,

    [EnumMember(Value = "line-art")]
    LineArt,

    [EnumMember(Value = "low-poly")]
    LowPoly,

    [EnumMember(Value = "modeling-compound")]
    ModelingCompound,

    [EnumMember(Value = "neon-punk")]
    NeonPunk,

    [EnumMember(Value = "origami")]
    Origami,

    [EnumMember(Value = "photographic")]
    Photographic,

    [EnumMember(Value = "pixel-art")]
    PixelArt,

    [EnumMember(Value = "tile-texture")]
    TileTexture
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AspectRatio
{
    [EnumMember(Value = "1:1")]
    Square,

    [EnumMember(Value = "16:9")]
    Landscape16x9,

    [EnumMember(Value = "21:9")]
    Cinematic21x9,

    [EnumMember(Value = "2:3")]
    Portrait23,

    [EnumMember(Value = "3:2")]
    Landscape32,

    [EnumMember(Value = "4:5")]
    Portrait45,

    [EnumMember(Value = "5:4")]
    Landscape54,

    [EnumMember(Value = "9:16")]
    Portrait916,

    [EnumMember(Value = "9:21")]
    Cinematic921
}