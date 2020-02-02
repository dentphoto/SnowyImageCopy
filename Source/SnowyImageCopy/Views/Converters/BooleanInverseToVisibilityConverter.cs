﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;

namespace SnowyImageCopy.Views.Converters
{
	/// <summary>
	/// Inverses Boolean and then converts it to Visibility.
	/// </summary>
	[ValueConversion(typeof(bool), typeof(Visibility))]
	public class BooleanInverseToVisibilityConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (!(value is bool sourceValue))
				return DependencyProperty.UnsetValue;

			return !sourceValue ? Visibility.Visible : Visibility.Collapsed;
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (!(value is Visibility sourceValue))
				return DependencyProperty.UnsetValue;

			return (sourceValue != Visibility.Visible);
		}
	}
}