using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Data.Mapping;

internal sealed class MappingException : Exception
{
    // todo revisit constructor scheme here
    public MappingException(string message) : base(message) { }

    public MappingException(Exception innerException, string destinationDescriptor)
        : base($"Could not map to {destinationDescriptor}. See inner exception for details.", innerException)
    {
    }
}
