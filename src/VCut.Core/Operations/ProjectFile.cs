using System.Text.Json;
using System.Text.Json.Serialization;
using VCut.Core.Models;

namespace VCut.Core.Operations;

/// <summary>프로젝트 파일(.vcproj) 저장/열기. docx '프로젝트 파일 저장/열기'.</summary>
public static class ProjectFile
{
    /// <summary>v-cut 프로젝트 파일 확장자.</summary>
    public const string Extension = ".vcproj";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static async Task SaveAsync(VCutProject project, string path, CancellationToken ct = default)
    {
        await using var fs = File.Create(path);
        await JsonSerializer.SerializeAsync(fs, project, Options, ct).ConfigureAwait(false);
    }

    public static async Task<VCutProject> LoadAsync(string path, CancellationToken ct = default)
    {
        await using var fs = File.OpenRead(path);
        var project = await JsonSerializer.DeserializeAsync<VCutProject>(fs, Options, ct).ConfigureAwait(false);
        return project ?? throw new InvalidDataException("프로젝트 파일을 읽을 수 없습니다.");
    }
}
