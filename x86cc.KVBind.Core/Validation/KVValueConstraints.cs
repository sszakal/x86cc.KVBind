using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace x86cc.KVBind.Core;

// The value rules a field can declare besides "required": string length, pattern and a lower/upper bound.
// Shared by field options, profile rules and group rules so all three report the same codes and messages.
// An unset value (null, or an empty string) is never reported here; that is what "required" is for.
internal sealed class KVValueConstraints
{
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromSeconds(1);

    private Regex? patternRegex;

    public int? MinLength { get; private set; }

    public int? MaxLength { get; private set; }

    public string? Pattern { get; private set; }

    public object? Min { get; private set; }

    public object? Max { get; private set; }

    public void SetMinLength(int minLength)
    {
        if (minLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minLength), "MinLength must be greater than zero.");
        }

        this.MinLength = minLength;
        this.EnsureLengthOrder();
    }

    public void SetMaxLength(int maxLength)
    {
        if (maxLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLength), "MaxLength must be greater than zero.");
        }

        this.MaxLength = maxLength;
        this.EnsureLengthOrder();
    }

    public void SetPattern(string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        // Constructing the Regex here reports an invalid pattern when the definition is built.
        this.patternRegex = new Regex(pattern, RegexOptions.CultureInvariant, PatternTimeout);
        this.Pattern = pattern;
    }

    public void SetMin(object? min)
    {
        this.Min = EnsureBound(min, nameof(min));
        this.EnsureBoundOrder();
    }

    public void SetMax(object? max)
    {
        this.Max = EnsureBound(max, nameof(max));
        this.EnsureBoundOrder();
    }

    public void Evaluate(string path, object? value, List<KVValidationError> errors)
    {
        if (value is null)
        {
            return;
        }

        if (value is string text)
        {
            if (text.Length == 0)
            {
                return;
            }

            if (this.MinLength.HasValue && text.Length < this.MinLength.Value)
            {
                errors.Add(new KVValidationError(path, "min_length", $"'{path}' must be at least {this.MinLength.Value} characters."));
            }

            if (this.MaxLength.HasValue && text.Length > this.MaxLength.Value)
            {
                errors.Add(new KVValidationError(path, "max_length", $"'{path}' must be at most {this.MaxLength.Value} characters."));
            }

            if (this.patternRegex is not null && !IsFullMatch(this.patternRegex, text))
            {
                errors.Add(new KVValidationError(path, "pattern", $"'{path}' does not match the required format."));
            }
        }

        if (this.Min is not null && Compare(value, this.Min) < 0)
        {
            errors.Add(new KVValidationError(path, "min", $"'{path}' must be at least {Format(this.Min)}."));
        }

        if (this.Max is not null && Compare(value, this.Max) > 0)
        {
            errors.Add(new KVValidationError(path, "max", $"'{path}' must be at most {Format(this.Max)}."));
        }
    }

    // Length and pattern rules only mean something for text.
    internal static void EnsureTextField(Type valueType, string rule)
    {
        if (valueType != typeof(string) && valueType != typeof(object))
        {
            throw new InvalidOperationException($"{rule} applies to string fields; the field type is '{valueType.FullName}'.");
        }
    }

    // Numbers of any CLR type compare by value; everything else (dates, times, text) compares only with a
    // bound of its own type. Null means "not comparable", which reports nothing.
    private static int? Compare(object value, object bound)
    {
        if (TryGetNumber(value, out var number) && TryGetNumber(bound, out var boundNumber))
        {
            return number.CompareTo(boundNumber);
        }

        return value.GetType() == bound.GetType() && value is IComparable comparable
            ? comparable.CompareTo(bound)
            : null;
    }

    private static bool TryGetNumber(object value, out decimal number)
    {
        number = 0m;
        if (value is not (sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal))
        {
            return false;
        }

        try
        {
            number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool IsFullMatch(Regex regex, string text)
    {
        try
        {
            var match = regex.Match(text);
            return match.Success && match.Index == 0 && match.Length == text.Length;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static object EnsureBound(object? bound, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(bound, parameterName);
        if (bound is not IComparable)
        {
            throw new InvalidOperationException($"A bound of type '{bound.GetType().FullName}' cannot be compared; Min/Max need a comparable value.");
        }

        return bound;
    }

    private static string Format(object bound) => bound switch
    {
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
        _ => Convert.ToString(bound, CultureInfo.InvariantCulture) ?? string.Empty
    };

    private void EnsureLengthOrder()
    {
        if (this.MinLength > this.MaxLength)
        {
            throw new InvalidOperationException($"MinLength ({this.MinLength}) cannot be greater than MaxLength ({this.MaxLength}).");
        }
    }

    private void EnsureBoundOrder()
    {
        if (this.Min is not null && this.Max is not null && Compare(this.Min, this.Max) > 0)
        {
            throw new InvalidOperationException($"Min ({Format(this.Min)}) cannot be greater than Max ({Format(this.Max)}).");
        }
    }
}
