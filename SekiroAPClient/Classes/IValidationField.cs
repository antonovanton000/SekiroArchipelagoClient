using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InjustUILibrary.Classes
{
    public interface IValidationField
    {
        public bool Validate();

        public bool IsError { get; set; }
        public string ErrorMessage { get; set; }
        public string BindPropertyName { get; set; }

    }

    public class AdditionalValidationResult
    {
        public string BindPropertyName { get; set; } = null!;
        public string ErrorMessage { get; set; } = null!;
    }
}
