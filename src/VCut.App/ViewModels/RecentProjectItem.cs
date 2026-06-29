namespace VCut.App.ViewModels;

public sealed class RecentProjectItem(string path)
{
    public string Path { get; } = path;
    public string FileName { get; } = System.IO.Path.GetFileNameWithoutExtension(path);
    public string Directory { get; } = System.IO.Path.GetDirectoryName(path) ?? "";
    public bool IsMissing { get; } = !System.IO.File.Exists(path);
    public double MissingOpacity => IsMissing ? 0.45 : 1.0;
}
