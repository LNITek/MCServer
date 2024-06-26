﻿using System.Globalization;
using System.Windows.Data;

namespace MCServer.Helpers;

[ValueConversion(typeof(bool), typeof(bool))]
public class BoolInverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return !(bool)value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

[ValueConversion(typeof(string), typeof(bool), ParameterType = typeof(string))]
public class StringEquals : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value.ToString().Equals(parameter.ToString());
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}