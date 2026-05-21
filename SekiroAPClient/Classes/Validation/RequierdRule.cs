using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace InjustUILibrary.Validation
{
    public class RequierdRule : ValidationRule
    {
        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            if (String.IsNullOrEmpty(value?.ToString()))
                return new ValidationResult(false, $"Field is required!");
            else
                return ValidationResult.ValidResult;
        }
    }
}
