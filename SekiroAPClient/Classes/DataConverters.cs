using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace SekiroAPClient.Classes;

public class NullToVisibility : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null)
            return Visibility.Visible;

        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Visibility.Visible;
    }
}

public class NullToBool : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null)
            return false;

        return true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return null;
    }
}

public class NotNullToBool : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null)
            return true;

        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return null;
    }
}

public class NotNullToVisibility : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null)
            return Visibility.Collapsed;

        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Visibility.Collapsed;
    }
}

public class NotBoolToVisibility : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value != null)
        {
            if ((bool)value == true)
                return Visibility.Collapsed;
            else
                return Visibility.Visible;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Visibility.Collapsed;
    }
}

public class NotConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {

        return !((bool)value);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return !((bool)value);

    }
}

public class TooltipTimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var minutes = Math.Round((DateTime.Now - ((DateTime)value)).TotalMinutes, 0);
        if (minutes == 0)
            return "less than minute ago";
        else
        {
            return $"{minutes} minutes ago";
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return null;

    }
}

public class EqualVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {       
        if (value == parameter)
            return Visibility.Visible;
        else
        
            return Visibility.Collapsed;        
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return null;

    }
}

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null)
            return Visibility.Collapsed;
        else

            return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return null;

    }
}

public class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null)
            return Visibility.Visible;
        else

            return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return null;

    }
}

public class BoolToHiddenConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {

        return ((bool)value) ? Visibility.Visible : Visibility.Hidden;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return !((bool)value);

    }
}

public class CountToMarginConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {

        switch ((int)value)
        {
            case 2:
                return new Thickness(-190, 0, 190, 0);
            case 3:
                return new Thickness(-190, 0, 190, 0);
            case 4:
                return new Thickness(-58, 0, 0, 0);
            case 5:
                return new Thickness(-63, 0, 0, 0);
        }

        return new Thickness();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();

    }
}

public class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {

        if ((int)value > 0)
        {
            return Visibility.Visible;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();

    }
}

public class NotCountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {

        if ((int)value == 0)
        {
            return Visibility.Visible;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();

    }
}
public class TrackProgressConverter : IMultiValueConverter
{
    public object Convert(
        object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        return (double)((int)values[0] * 1000 / (long)values[1]);
    }

    public object[] ConvertBack(
        object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public class MinusConverter : IMultiValueConverter
{
    public object Convert(
        object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        return ((double)values[0] - (double)values[1]);
    }

    public object[] ConvertBack(
        object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public class IsEnumConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var t = value.GetType();
        var val = Enum.ToObject(t, byte.Parse(parameter.ToString()));
        var res = val.Equals(value);
        return res;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return false;

    }
}

public class IsEnumToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var t = value.GetType();
        var val = Enum.ToObject(t, byte.Parse(parameter.ToString()));
        var res = val.Equals(value);
        return res ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Visibility.Collapsed;

    }
}

public class EqualConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var t = value.GetType();
        var res = value.Equals(parameter);
        return res;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return false;

    }
}

public class MilisecondsToTimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var ms = (long)value;
        var str = TimeSpan.FromMilliseconds(ms).ToString(@"mm\:ss\.ff");

        return str;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var str = (string)value;
        var ms = TimeSpan.Parse(str).TotalMilliseconds;
        
        return ms;
    }
}

public class BooleanAndConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        foreach (object value in values)
        {
            if ((value is bool) && (bool)value == false)
            {
                return false;
            }
        }
        return true;
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotSupportedException("BooleanAndConverter is a OneWay converter.");
    }
}

public class BooleanOrConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        return (values.Any(i => ((i is bool) && ((bool)i == true))));
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotSupportedException("BooleanAndConverter is a OneWay converter.");
    }
}

public class InverseAndBooleansToBooleanConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (values.LongLength > 0)
        {
            foreach (var value in values)
            {
                if (value is bool && (bool)value)
                {
                    return false;
                }
            }
        }
        return true;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class PercentageConverter : IValueConverter
{
    public object Convert(object value,
        Type targetType,
        object parameter,
        System.Globalization.CultureInfo culture)
    {
        return System.Convert.ToDouble(value) *
               System.Convert.ToDouble(parameter);
    }

    public object ConvertBack(object value,
        Type targetType,
        object parameter,
        System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
