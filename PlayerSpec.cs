using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NBoardLocalGameServer
{
    /// <summary>
    /// マッチ設定JSON内のプレイヤー指定. player設定JSONへのパス(文字列)か, インラインのPlayerConfig(オブジェクト)のどちらかで書ける.
    /// </summary>
    [JsonConverter(typeof(PlayerSpecConverter))]
    internal sealed record PlayerSpec(string? FilePath, PlayerConfig? Inline)
    {
        /// <summary>インライン指定ならそのまま, パス指定ならファイルから読み込んで返す.</summary>
        public PlayerConfig? Resolve() => Inline ?? (FilePath is null ? null : PlayerConfig.Load(FilePath));
    }

    internal class PlayerSpecConverter : JsonConverter<PlayerSpec>
    {
        public override PlayerSpec? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.Null => null,
                JsonTokenType.String => new PlayerSpec(reader.GetString(), null),
                JsonTokenType.StartObject => new PlayerSpec(null, JsonSerializer.Deserialize<PlayerConfig>(ref reader, options)),
                _ => throw new JsonException("A player must be a path string or a player config object.")
            };
        }

        public override void Write(Utf8JsonWriter writer, PlayerSpec value, JsonSerializerOptions options)
        {
            if (value.Inline is not null)
                JsonSerializer.Serialize(writer, value.Inline, options);
            else
                writer.WriteStringValue(value.FilePath);
        }
    }
}
