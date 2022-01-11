using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Data.Mapping;

internal abstract class CompositeTypeMappingStrategy
{
    protected static bool TryCreateFor(
        Type type,
        [NotNullWhen(returnValue: true)] out CompositeTypeMappingStrategy? strategy,
        [NotNullWhen(returnValue: false)] out string? errorMessage)
    {
        return TryCreateFor(type, nullabilityInfo: null, out strategy, out errorMessage);
    }

    protected static bool TryCreateFor(
        Type type, 
        NullabilityInfo? nullabilityInfo,
        [NotNullWhen(returnValue: true)] out CompositeTypeMappingStrategy? strategy, 
        [NotNullWhen(returnValue: false)] out string? errorMessage)
    {
        if (type.IsGenericTypeDefinition) { throw new ArgumentException("generic type definition provided", nameof(type)); }
        
        if (type.IsPrimitive || type == typeof(string)) { return Error("primitive type provided", out strategy, out errorMessage); }

        if (DictionaryTypeMappingStrategy.TryCreateDictionaryStrategyFor(type, nullabilityInfo, out var dictionaryStrategy, out var dictionaryErrorMessage))
        {
            return Success(dictionaryStrategy, out strategy, out errorMessage);
        }

        if (PocoTypeMappingStrategy.TryCreatePocoStrategyFor(type, nullabilityInfo, out var pocoStrategy, out var pocoErrorMessage))
        {
            return Success(pocoStrategy, out strategy, out errorMessage);
        }

        return Error(dictionaryErrorMessage + Environment.NewLine + pocoErrorMessage, out strategy, out errorMessage);
    }

    protected static bool Error(
        string message,
        [NotNullWhen(returnValue: true)] out CompositeTypeMappingStrategy? strategy,
        [NotNullWhen(returnValue: false)] out string? errorMessage)
    {
        strategy = null;
        errorMessage = message;
        return false;
    }

    protected static bool Success(
        CompositeTypeMappingStrategy createdStrategy,
        [NotNullWhen(returnValue: true)] out CompositeTypeMappingStrategy? strategy,
        [NotNullWhen(returnValue: false)] out string? errorMessage)
    {
        strategy = createdStrategy;
        errorMessage = null;
        return true;
    }
}
