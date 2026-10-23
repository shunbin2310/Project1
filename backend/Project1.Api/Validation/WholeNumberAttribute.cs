using System.ComponentModel.DataAnnotations;

namespace Project1.Api.Validation;

[AttributeUsage(AttributeTargets.Property)]
public sealed class WholeNumberAttribute : ValidationAttribute
{
    public WholeNumberAttribute() : base("{0} must be a whole number.")
    {
    }

    public override bool IsValid(object? value) =>
        value is null || value is decimal number && number == decimal.Truncate(number);
}

public static class QuantityRules
{
    public const decimal Maximum = 999999999999999m;

    public static bool IsValid(decimal value) =>
        value >= 0 && value <= Maximum && value == decimal.Truncate(value);
}
