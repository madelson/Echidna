using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Data.Templating;

internal enum SqlTemplateFragmentType
{
    Default,
    Raw,
    Value,
    Parameter,
}

internal readonly record struct SqlTemplateFragment(object? Value, SqlTemplateFragmentType FragmentType);
