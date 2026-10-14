using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace TimerApp.Helpers;

/// <summary>
/// bool -&gt; Visibility. С ConverterParameter="Invert" — наоборот.
/// </summary>
public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool flag = value is bool b && b;
        bool invert = parameter is string s &&
                      s.Equals("Invert", StringComparison.OrdinalIgnoreCase);

        if (invert)
            flag = !flag;

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        bool visible = value is Visibility v && v == Visibility.Visible;
        bool invert = parameter is string s &&
                      s.Equals("Invert", StringComparison.OrdinalIgnoreCase);
        return invert ? !visible : visible;
    }
}
