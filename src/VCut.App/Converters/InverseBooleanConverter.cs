using Microsoft.UI.Xaml.Data;

namespace VCut.App.Converters;

/// <summary>bool 값을 반전. 예: 개수 분할이 켜지면 시간 입력은 비활성.</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is bool b ? !b : value;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is bool b ? !b : value;
}
