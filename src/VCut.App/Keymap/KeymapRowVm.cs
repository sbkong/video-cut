using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VCut.App.Keymap;

/// <summary>설정창 '단축키' 목록의 행 하나(액션 이름 + 현재 단축키 표시용).</summary>
public sealed class KeymapRowVm(string id, string name, string category) : INotifyPropertyChanged
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string Category { get; } = category;

    private string _shortcut = "";
    public string Shortcut
    {
        get => _shortcut;
        set
        {
            if (_shortcut == value) return;
            _shortcut = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
